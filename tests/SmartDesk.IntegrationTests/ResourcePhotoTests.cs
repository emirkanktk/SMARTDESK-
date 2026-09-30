using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Resource photos through the real pipeline on PostgreSQL (ADR-0027): the multipart upload, the
// bytes and headers that come back, and who may do what. The demo seed gives Meeting Room B to
// the demo manager's team, which both demo employees are in.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ResourcePhotoTests(PostgresApiFixture fixture)
{
    // A real 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    [Fact]
    public async Task A_manager_puts_a_photo_on_their_teams_resource_and_its_members_see_it()
    {
        var manager = await ClientAsync("manager@smartdesk.local");
        var room = await FindAsync(manager, "Meeting Room B");
        room.PhotoUpdatedAt.Should().BeNull();

        var put = await manager.PutAsync($"/api/resources/{room.Id}/photo", Upload(Png, "room.png", "image/png"));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<ResourceRow>())!.PhotoUpdatedAt.Should().NotBeNull();

        var member = await ClientAsync("employee1@smartdesk.local");
        var photo = await member.GetAsync($"/api/resources/{room.Id}/photo");
        photo.StatusCode.Should().Be(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        photo.Headers.CacheControl!.Private.Should().BeTrue();
        photo.Headers.CacheControl.NoCache.Should().BeTrue();
        photo.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await FindAsync(member, "Meeting Room B")).PhotoUpdatedAt.Should().NotBeNull();

        // Asked again with its ETag: nothing to send.
        var again = new HttpRequestMessage(HttpMethod.Get, $"/api/resources/{room.Id}/photo");
        again.Headers.IfNoneMatch.Add(photo.Headers.ETag!);
        (await member.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        // Removed: the type's picture shows instead.
        (await manager.DeleteAsync($"/api/resources/{room.Id}/photo")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await member.GetAsync($"/api/resources/{room.Id}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await FindAsync(member, "Meeting Room B")).PhotoUpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Only_real_images_are_taken_whatever_the_upload_claims()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var board = await FindAsync(admin, "Board Room");

        // Script dressed up as a PNG.
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();
        var refused = await admin.PutAsync($"/api/resources/{board.Id}/photo", Upload(svg, "photo.png", "image/png"));
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors["Photo"]
            .Should().Equal("The photo must be a JPEG, PNG or WebP image.");

        // A PNG that says it's a GIF is still served as the PNG it is.
        (await admin.PutAsync($"/api/resources/{board.Id}/photo", Upload(Png, "photo.gif", "image/gif")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/resources/{board.Id}/photo")).Content.Headers.ContentType!.MediaType
            .Should().Be("image/png");

        // No file in the form, or no form at all.
        var noFile = new MultipartFormDataContent { { new StringContent("hello"), "note" } };
        var missing = await admin.PutAsync($"/api/resources/{board.Id}/photo", noFile);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors["Photo"]
            .Should().Equal("Choose a photo to upload.");
        (await admin.PutAsJsonAsync($"/api/resources/{board.Id}/photo", new { photo = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        (await admin.DeleteAsync($"/api/resources/{board.Id}/photo")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Employees_cant_change_photos_and_managers_cant_change_shared_ones()
    {
        var employee = await ClientAsync("employee2@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var room = await FindAsync(employee, "Meeting Room B");
        var board = await FindAsync(manager, "Board Room");

        (await employee.PutAsync($"/api/resources/{room.Id}/photo", Upload(Png, "room.png", "image/png")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await employee.DeleteAsync($"/api/resources/{room.Id}/photo")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PutAsync($"/api/resources/{board.Id}/photo", Upload(Png, "board.png", "image/png")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"/api/resources/{Guid.NewGuid()}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static MultipartFormDataContent Upload(byte[] data, string fileName, string contentType)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "photo", fileName } };
    }

    private static async Task<ResourceRow> FindAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<ResourceRow>>("/api/resources"))!.Single(r => r.Name == name);

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

    private sealed record ResourceRow(Guid Id, string Name, DateTime? PhotoUpdatedAt);

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
