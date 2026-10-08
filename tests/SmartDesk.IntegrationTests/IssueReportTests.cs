using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Reported problems through the real pipeline on PostgreSQL (ADR-0037): the multipart form, the
// duplicate rule, who sees and moves what, and the reporter's inbox. The demo seed gives Meeting
// Room B and Projector P-1 to the demo manager's team, which both demo employees are in.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IssueReportTests(PostgresApiFixture fixture)
{
    // A real 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_problem_reported_at_the_door_is_fixed_by_the_teams_manager_and_its_reporter_is_told()
    {
        var reporter = await ClientAsync("employee1@smartdesk.local");
        var colleague = await ClientAsync("employee2@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var room = await FindAsync(reporter, "Meeting Room B");

        var created = await reporter.PostAsync(
            $"/api/resources/{room.Id}/issues", Form("Display", "The TV shows no picture", blocking: true, Png));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var issue = (await created.Content.ReadFromJsonAsync<IssueRow>(Json))!;
        issue.Should().Match<IssueRow>(i =>
            i.ResourceId == room.Id && i.Category == "Display" && i.Status == "Open" && i.IsBlocking && i.HasPhoto
            && i.Description == "The TV shows no picture" && i.ReportedByName == "Ayşe Yılmaz");

        // Someone else at the door sees it, and can't report it a second time.
        (await colleague.GetFromJsonAsync<List<IssueRow>>($"/api/resources/{room.Id}/issues", Json))!
            .Select(i => i.Id).Should().Contain(issue.Id);
        var again = await colleague.PostAsync($"/api/resources/{room.Id}/issues", Form("Display", "TV broken"));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<Problem>(Json))!.Code.Should().Be("issue-already-reported");

        // The team's manager has it in their list, with its photo, and resolves it.
        (await manager.GetFromJsonAsync<List<IssueRow>>("/api/issues?status=Open", Json))!
            .Select(i => i.Id).Should().Contain(issue.Id);
        var photo = await manager.GetAsync($"/api/issues/{issue.Id}/photo");
        photo.StatusCode.Should().Be(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        photo.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(Png);

        // Employees don't move reports along.
        (await reporter.PatchAsJsonAsync($"/api/issues/{issue.Id}", new { status = "Resolved" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resolved = await manager.PatchAsJsonAsync($"/api/issues/{issue.Id}", new { status = "Resolved" });
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resolved.Content.ReadFromJsonAsync<IssueRow>(Json))!.ResolvedAt.Should().NotBeNull();
        (await colleague.GetFromJsonAsync<List<IssueRow>>($"/api/resources/{room.Id}/issues", Json))!
            .Select(i => i.Id).Should().NotContain(issue.Id);

        var inbox = (await reporter.GetFromJsonAsync<List<InboxRow>>("/api/inbox", Json))!;
        inbox.Should().ContainSingle(m => m.Kind == "IssueResolved" && m.IssueCategory == "Display"
            && m.ResourceName == "Meeting Room B" && m.Note == "The TV shows no picture" && m.SenderName != null);

        // Reopening isn't possible: the problem coming back is a new report, which is allowed again.
        var reopen = await manager.PatchAsJsonAsync($"/api/issues/{issue.Id}", new { status = "Open" });
        reopen.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reopen.Content.ReadFromJsonAsync<Problem>(Json))!.Code.Should().Be("issue-resolved");
        var newReport = await colleague.PostAsync($"/api/resources/{room.Id}/issues", Form("Display", "TV broken again"));
        newReport.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = (await newReport.Content.ReadFromJsonAsync<IssueRow>(Json))!;
        (await manager.PatchAsJsonAsync($"/api/issues/{second.Id}", new { status = "Resolved" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_form_without_a_category_or_with_a_fake_photo_is_refused_in_the_callers_language()
    {
        var client = await ClientAsync("employee2@smartdesk.local");
        var board = await FindAsync(client, "Board Room");

        var noCategory = new MultipartFormDataContent { { new StringContent("The room is cold"), "description" } };
        var refused = await client.PostAsync($"/api/resources/{board.Id}/issues", noCategory);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<ValidationProblem>(Json))!.Errors.Should().ContainKey("Category");

        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();
        var turkish = new HttpRequestMessage(HttpMethod.Post, $"/api/resources/{board.Id}/issues")
        {
            Content = Form("AirConditioning", "Klima çalışmıyor", photo: svg),
        };
        turkish.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("tr-TR"));
        var fake = await client.SendAsync(turkish);
        fake.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fake.Content.ReadFromJsonAsync<ValidationProblem>(Json))!.Errors["Photo"]
            .Should().Equal("Fotoğraf JPEG, PNG ya da WebP biçiminde olmalı.");
    }

    [Fact]
    public async Task Another_teams_manager_and_resources_stay_out_of_reach()
    {
        var outsider = await ClientAsync("employee2@smartdesk.local");
        var admin = await ClientAsync("admin@smartdesk.local");
        var projector = await FindAsync(admin, "Projector P-1");
        var board = await FindAsync(admin, "Board Room");

        var created = await admin.PostAsync($"/api/resources/{board.Id}/issues", Form("Cleaning", "Coffee on the table"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var onBoard = (await created.Content.ReadFromJsonAsync<IssueRow>(Json))!;

        // A shared resource's reports are the Admins' to move; the list is for Admins and Managers.
        var manager = await ClientAsync("manager@smartdesk.local");
        (await manager.PatchAsJsonAsync($"/api/issues/{onBoard.Id}", new { status = "InProgress" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetFromJsonAsync<List<IssueRow>>("/api/issues", Json))!
            .Select(i => i.Id).Should().NotContain(onBoard.Id);
        (await outsider.GetAsync("/api/issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/resources/{Guid.NewGuid()}/issues")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/resources/{projector.Id}/issues")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PatchAsJsonAsync($"/api/issues/{onBoard.Id}", new { status = "Resolved" })).EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent Form(string category, string description, bool blocking = false, byte[]? photo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(category), "category" },
            { new StringContent(description), "description" },
            { new StringContent(blocking ? "true" : "false"), "isBlocking" },
        };
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "photo", "photo.png");
        }

        return form;
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

    private sealed record ResourceRow(Guid Id, string Name);

    private sealed record IssueRow(
        Guid Id,
        Guid ResourceId,
        string Category,
        string Description,
        bool IsBlocking,
        string Status,
        bool HasPhoto,
        string? ReportedByName,
        DateTime? ResolvedAt);

    private sealed record InboxRow(string Kind, string? IssueCategory, string ResourceName, string? Note, string? SenderName);

    private sealed record Problem(string? Code);

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
