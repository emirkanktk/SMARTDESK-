using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Floor plans through the real pipeline on PostgreSQL (ADR-0041): an Admin uploads a floor's plan
// as multipart, it comes back like a photo, and resources are placed on it. Each test has a floor
// of its own, so no other test's floors are touched.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FloorPlanTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task An_admin_uploads_a_floors_plan_and_places_a_room_on_it()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee1@smartdesk.local");
        var floor = $"Floor {Guid.NewGuid():N}"[..16];
        var room = await AddRoomAsync(admin, floor);

        var upload = await admin.PutAsync(ImageUrl(floor), PlanForm(Png(1600, 900)));

        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploaded = (await upload.Content.ReadFromJsonAsync<FloorBody>())!;
        uploaded.Location.Should().Be(floor);
        uploaded.Plan.Should().BeEquivalentTo(new { Width = 1600, Height = 900 });

        var image = await employee.GetAsync(ImageUrl(floor));
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        image.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(Png(1600, 900));

        using var again = new HttpRequestMessage(HttpMethod.Get, ImageUrl(floor));
        again.Headers.IfNoneMatch.Add(image.Headers.ETag!);
        (await employee.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        var placed = await admin.PutAsJsonAsync($"/api/resources/{room.Id}/position", new { x = 0.25, y = 0.5 });
        placed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await placed.Content.ReadFromJsonAsync<RoomBody>())!.Position.Should().Be(new PositionBody(0.25, 0.5));

        var floors = (await employee.GetFromJsonAsync<List<FloorBody>>("/api/floor-plans"))!;
        floors.Should().ContainSingle(f => f.Location == floor).Which.Placed.Should().Be(1);
    }

    [Fact]
    public async Task Placing_and_uploading_say_why_they_cant()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee1@smartdesk.local");
        var floor = $"Floor {Guid.NewGuid():N}"[..16];
        var room = await AddRoomAsync(admin, floor);

        (await employee.PutAsJsonAsync($"/api/resources/{room.Id}/position", new { x = 0.5, y = 0.5 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var outside = await admin.PutAsJsonAsync($"/api/resources/{room.Id}/position", new { x = 2, y = 0.5 });
        outside.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(outside)).Should().ContainKey("Position");

        (await employee.PutAsync(ImageUrl(floor), PlanForm(Png(800, 600))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var notAnImage = await admin.PutAsync(ImageUrl(floor), PlanForm("<svg/>"u8.ToArray()));
        notAnImage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(notAnImage))["Plan"].Should().Equal("The plan must be a JPEG, PNG or WebP image.");
        (await employee.GetAsync(ImageUrl(floor))).StatusCode.Should().Be(HttpStatusCode.NotFound, "no plan yet");
    }

    private static string ImageUrl(string floor) => $"/api/floor-plans/image?location={Uri.EscapeDataString(floor)}";

    private static MultipartFormDataContent PlanForm(byte[] data)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "plan", "plan.png" } };
    }

    // The signature and the IHDR chunk: all the API reads of a PNG.
    private static byte[] Png(uint width, uint height)
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. "IHDR"u8, .. new byte[17]];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    private static async Task<RoomBody> AddRoomAsync(HttpClient admin, string location)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Plan room {Guid.NewGuid():N}"[..20],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 4,
            location,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RoomBody>())!;
    }

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record RoomBody(Guid Id, PositionBody? Position);

    private sealed record PositionBody(double X, double Y);

    private sealed record FloorBody(string Location, int Resources, int Placed, PlanBody? Plan);

    private sealed record PlanBody(int Width, int Height, DateTime UpdatedAt);
}
