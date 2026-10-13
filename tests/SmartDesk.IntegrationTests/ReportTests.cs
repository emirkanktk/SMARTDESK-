using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// The usage report as an Excel workbook over HTTP (ADR-0045).
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportTests(PostgresApiFixture fixture)
{
    private const string Query = "from=2026-09-01&to=2026-09-30&timeZone=Europe/Istanbul";

    [Fact]
    public async Task An_admin_downloads_the_report_as_a_workbook_in_their_language()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        admin.DefaultRequestHeaders.Add("Accept-Language", "tr");

        var response = await admin.GetAsync($"/api/reports/usage/workbook?{Query}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("smartdesk-usage-2026-09-01-2026-09-30.xlsx");
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        workbook.Worksheets.First().Name.Should().Be("Özet");
        workbook.Worksheet("Kaynaklar").Cell(1, 1).GetString().Should().Be("Kaynak");
    }

    [Fact]
    public async Task Only_an_admin_gets_it_and_the_range_is_checked_as_for_the_report()
    {
        var employee = await ClientAsync("employee1@smartdesk.local");
        (await employee.GetAsync($"/api/reports/usage/workbook?{Query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await ClientAsync("admin@smartdesk.local");
        var backwards = await admin.GetAsync("/api/reports/usage/workbook?from=2026-09-30&to=2026-09-01");
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
}
