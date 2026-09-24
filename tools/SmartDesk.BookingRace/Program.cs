// Reproduces the concurrency test behind ADR-0011: many clients try to book the same slot of
// the same resource at the same instant, over several rounds. The API must let exactly one
// booking per slot through and answer every other request with 409 - never 500.
//
//   dotnet run --project tools/SmartDesk.BookingRace -- [options]
//
//   --base-url <url>     API root                        (default http://localhost:5291)
//   --email <email>      account to book with            (default employee1@smartdesk.local)
//   --password <pw>                                      (default SmartDesk123!)
//   --resource <name>    resource to fight over          (default "Board Room")
//   --parallel <n>       simultaneous requests per slot  (default 30)
//   --rounds <n>         slots to test, one per round    (default 10)
//   --keep               leave the winning reservations in place (default: cancel them)
//
// Needs the API running against PostgreSQL with the demo data (Development). Exit code 0 when
// every round had exactly one 201 and only 409s otherwise.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

var options = RaceOptions.Parse(args);

using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = options.Parallel + 4 };
using var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };

var token = await LogInAsync(http, options);
http.DefaultRequestHeaders.Authorization = new("Bearer", token);

var resourceId = await FindResourceAsync(http, options.Resource);
Console.WriteLine($"Racing {options.Parallel} bookings per slot on \"{options.Resource}\" over {options.Rounds} rounds.\n");

// Open the connections up front, so that in each round the requests leave together instead
// of being staggered by connection setup.
await Task.WhenAll(Enumerable.Range(0, options.Parallel).Select(_ => http.GetAsync("/health")));

var winners = new List<Guid>();
var failedRounds = 0;
// Slots far enough ahead to be free and never in the past.
var firstDay = DateTime.UtcNow.Date.AddDays(60);

for (var round = 0; round < options.Rounds; round++)
{
    var start = firstDay.AddDays(round).AddHours(9);
    var body = new { resourceId, startDateTime = start, endDateTime = start.AddHours(1), note = "booking race" };

    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requests = Enumerable.Range(0, options.Parallel).Select(async _ =>
    {
        await gate.Task;
        return await http.PostAsJsonAsync("/api/reservations", body);
    }).ToList();
    gate.SetResult();
    var responses = await Task.WhenAll(requests);

    var codes = responses.GroupBy(r => (int)r.StatusCode).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
    var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
    var ok = created.Count == 1 && codes.Keys.All(code => code is 201 or 409);
    if (!ok)
    {
        failedRounds++;
    }

    foreach (var response in created)
    {
        winners.Add((await response.Content.ReadFromJsonAsync<IdOnly>())!.Id);
    }

    Console.WriteLine($"round {round + 1,2}  {start:yyyy-MM-dd HH:mm}Z  " +
        string.Join("  ", codes.Select(c => $"{c.Key}x{c.Value}")) + (ok ? "" : "   <-- expected exactly one 201, rest 409"));
}

if (!options.Keep)
{
    foreach (var id in winners)
    {
        await http.PostAsync($"/api/reservations/{id}/cancel", content: null);
    }
    Console.WriteLine($"\nCancelled the {winners.Count} winning reservation(s); rerun with --keep to inspect them.");
}

Console.WriteLine(failedRounds == 0
    ? $"\nPASS: every round had exactly one 201 and {options.Parallel - 1} x 409."
    : $"\nFAIL: {failedRounds} of {options.Rounds} round(s) broke the rule.");
return failedRounds == 0 ? 0 : 1;

static async Task<string> LogInAsync(HttpClient http, RaceOptions options)
{
    var response = await http.PostAsJsonAsync("/api/auth/login", new { email = options.Email, password = options.Password });
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Login as {options.Email} failed: {(int)response.StatusCode}. Is the API running with the demo data?");
    }
    return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
}

static async Task<Guid> FindResourceAsync(HttpClient http, string name)
{
    var resources = await http.GetFromJsonAsync<List<NamedResource>>("/api/resources") ?? [];
    return resources.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
        ?? throw new InvalidOperationException(
            $"No resource named \"{name}\". Available: {string.Join(", ", resources.Select(r => r.Name))}");
}

internal sealed record IdOnly(Guid Id);

internal sealed record NamedResource(Guid Id, string Name);

internal sealed record RaceOptions(
    string BaseUrl, string Email, string Password, string Resource, int Parallel, int Rounds, bool Keep)
{
    public static RaceOptions Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        return new RaceOptions(
            Value("--base-url", "http://localhost:5291"),
            Value("--email", "employee1@smartdesk.local"),
            Value("--password", "SmartDesk123!"),
            Value("--resource", "Board Room"),
            int.Parse(Value("--parallel", "30")),
            int.Parse(Value("--rounds", "10")),
            args.Contains("--keep"));
    }
}
