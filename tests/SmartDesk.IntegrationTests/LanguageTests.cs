using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// The whole pipeline answers in the language the request asks for (ADR-0023): Turkish or
// English from Accept-Language, English for anything else or nothing.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LanguageTests(PostgresApiFixture fixture)
{
    [Theory]
    [InlineData(null, "Invalid email or password.", "Unauthorized")]
    [InlineData("tr-TR,tr;q=0.9,en-US;q=0.8", "E-posta veya parola hatalı.", "Oturum gerekli")]
    [InlineData("en-US,en;q=0.9,tr;q=0.8", "Invalid email or password.", "Unauthorized")]
    [InlineData("de-DE", "Invalid email or password.", "Unauthorized")]
    public async Task Errors_follow_Accept_Language(string? acceptLanguage, string detail, string title)
    {
        var client = Client(acceptLanguage);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().Should().Be(detail);
        problem.RootElement.GetProperty("title").GetString().Should().Be(title);
    }

    [Fact]
    public async Task Validation_errors_are_translated_and_keep_their_field_names()
    {
        var client = await SignedInAsync("tr");
        var start = DateTime.UtcNow.Date.AddDays(120).AddHours(10);

        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = await RoomAsync(client, "Board Room"),
            startDateTime = start,
            endDateTime = start.AddHours(-1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("title").GetString().Should().Be("Bir veya daha fazla doğrulama hatası oluştu.");
        problem.RootElement.GetProperty("errors").GetProperty("EndDateTime")[0].GetString()
            .Should().Be("Bitiş zamanı başlangıç zamanından sonra olmalı.");
    }

    [Fact]
    public async Task A_conflict_carries_a_code_whatever_the_language()
    {
        var english = await SignedInAsync(null);
        var turkish = await SignedInAsync("tr");
        var room = await RoomAsync(english, "Board Room");
        var start = DateTime.UtcNow.Date.AddDays(121).AddHours(10);
        var body = new { resourceId = room, startDateTime = start, endDateTime = start.AddHours(1) };
        (await english.PostAsJsonAsync("/api/reservations", body)).StatusCode.Should().Be(HttpStatusCode.Created);

        var inTurkish = await turkish.PostAsJsonAsync("/api/reservations", body);
        var inEnglish = await english.PostAsJsonAsync("/api/reservations", body);

        inTurkish.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var tr = JsonDocument.Parse(await inTurkish.Content.ReadAsStringAsync());
        tr.RootElement.GetProperty("code").GetString().Should().Be("reservation-overlaps");
        tr.RootElement.GetProperty("detail").GetString().Should().Be("Board Room bu sürenin bir kısmında zaten rezerve edilmiş.");
        using var en = JsonDocument.Parse(await inEnglish.Content.ReadAsStringAsync());
        en.RootElement.GetProperty("code").GetString().Should().Be("reservation-overlaps");
        en.RootElement.GetProperty("detail").GetString().Should().Be("Board Room is already reserved for part of that time.");
    }

    private HttpClient Client(string? acceptLanguage)
    {
        var client = fixture.CreateClient();
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        return client;
    }

    private async Task<HttpClient> SignedInAsync(string? acceptLanguage)
    {
        var client = Client(acceptLanguage);
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "employee2@smartdesk.local",
            password = IdentityDataSeeder.DemoPassword,
        });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<Guid> RoomAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<NamedResource>>("/api/resources"))!.Single(r => r.Name == name).Id;

    private sealed record NamedResource(Guid Id, string Name);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);
}
