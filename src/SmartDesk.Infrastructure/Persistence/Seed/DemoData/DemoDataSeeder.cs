using System.Globalization;
using System.Text;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.Infrastructure.Persistence.Seed.DemoData;

// A lived-in office for a demo (the internship plan's day 10): three teams of people, rooms and
// desks on three floors with their plans, six weeks of bookings behind and two ahead - checked
// in, released, cancelled, meetings with people invited and weekly series - a few problem
// reports, a blocked window and a waiting list. So the usage report, the floor plan and the
// calendars have something to show from the first minute.
//
// Random with Bogus, from a fixed seed: every run on a fresh database gives the same office,
// relative to today (ADR-0047). It runs on `--demo-data` (Program.cs), in Development only,
// after the usual seed, and only into a database with no bookings yet: it writes straight to
// the database, past the API's checks, so it keeps to the rules itself (DemoBookings).
// Everything goes in one transaction, or nothing does.
public sealed class DemoDataSeeder(
    SmartDeskDbContext db,
    UserManager<ApplicationUser> users,
    TimeProvider timeProvider,
    ILogger<DemoDataSeeder> logger)
{
    public const int Seed = 20261014;
    public const string EmailDomain = "smartdesk.local";

    // How many more people each manager's team gets: the first is the seeded manager's, who has
    // the two seeded employees already.
    private static readonly int[] TeamGrowth = [4, 6, 5];

    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Reservations.AnyAsync(cancellationToken))
        {
            logger.LogWarning("Demo data skipped: the database has bookings already. It's for a fresh one.");
            return false;
        }

        var faker = new Faker("tr") { Random = new Randomizer(Seed) };
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var office = OfficeWeek.Of(await db.OfficeHours.AsNoTracking().SingleAsync(cancellationToken));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var people = await PeopleAsync(faker, now, cancellationToken);
        var resources = await ResourcesAsync(cancellationToken);
        await FloorPlansAsync(now, cancellationToken);

        var admin = people.Single(p => p.IsAdmin);
        var bookings = new DemoBookings(
            faker,
            office,
            now,
            people,
            resources
                .Where(r => r.Status == ResourceStatus.Active)
                .Select(r => new DemoResource(
                    r.Id,
                    r.Name,
                    r.ResourceTypeId,
                    r.Capacity,
                    r.CheckInRequired,
                    r.Teams.Select(t => (Guid?)t.TeamId).FirstOrDefault(),
                    DemoOffice.KeptForTheWalkthrough.Contains(r.Name)))
                .ToList());
        bookings.Generate(admin.Id);

        db.ReservationSeries.AddRange(bookings.Series);
        db.Reservations.AddRange(bookings.Reservations);
        db.ResourceBlocks.AddRange(bookings.Blocks);
        db.WaitlistEntries.AddRange(bookings.Waiting);
        db.InboxMessages.AddRange(bookings.Messages);
        var issues = Issues(faker, now, people, resources);
        db.IssueReports.AddRange(issues.Reports);
        db.InboxMessages.AddRange(issues.Messages);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Demo data: {People} people, {Resources} resources, {Reservations} bookings ({Series} weekly series), " +
            "{Issues} problem reports, {Waiting} waiting",
            people.Count, resources.Count, bookings.Reservations.Count, bookings.Series.Count, issues.Reports.Count, bookings.Waiting.Count);
        return true;
    }

    // The seeded admin, manager and two employees, two more managers, and their teams: names
    // from Bogus's Turkish lists, emails made of them, the demo password for all.
    private async Task<List<DemoPerson>> PeopleAsync(Faker faker, DateTime now, CancellationToken cancellationToken)
    {
        var admin = await SeededAsync("admin@smartdesk.local");
        var manager = await SeededAsync("manager@smartdesk.local");
        var people = new List<DemoPerson> { new(admin.Id, null, false, IsAdmin: true), new(manager.Id, manager.Id, true) };
        foreach (var employee in IdentityDataSeeder.DemoUsers.Where(u => u.ManagerEmail == manager.Email))
        {
            people.Add(new((await SeededAsync(employee.Email)).Id, manager.Id, false));
        }

        var managers = new List<Guid> { manager.Id };
        for (var i = 1; i < TeamGrowth.Length; i++)
        {
            var newManager = await NewPersonAsync(faker, now, Roles.Manager, null, cancellationToken);
            managers.Add(newManager.Id);
            people.Add(new(newManager.Id, newManager.Id, true));
        }

        for (var team = 0; team < TeamGrowth.Length; team++)
        {
            for (var i = 0; i < TeamGrowth[team]; i++)
            {
                var employee = await NewPersonAsync(faker, now, Roles.Employee, managers[team], cancellationToken);
                people.Add(new(employee.Id, managers[team], false));
            }
        }

        return people;

        async Task<ApplicationUser> SeededAsync(string email) =>
            await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"{email} isn't there: demo data goes on the Development seed, so run in Development.");
    }

    private async Task<ApplicationUser> NewPersonAsync(
        Faker faker, DateTime now, string role, Guid? managerId, CancellationToken cancellationToken)
    {
        var gender = faker.PickRandom<Bogus.DataSets.Name.Gender>();
        var first = faker.Name.FirstName(gender);
        var last = faker.Name.LastName(gender);
        var email = $"{Ascii(first)}.{Ascii(last)}@{EmailDomain}";
        for (var n = 2; await users.FindByEmailAsync(email) is not null; n++)
        {
            email = $"{Ascii(first)}.{Ascii(last)}{n}@{EmailDomain}";
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = first,
            LastName = last,
            ManagerId = managerId,
            Language = "tr",
            CreatedAt = now.AddDays(-faker.Random.Int(60, 400)),
        };
        Succeeded(await users.CreateAsync(user, IdentityDataSeeder.DemoPassword), $"create {email}");
        Succeeded(await users.AddToRoleAsync(user, role), $"make {email} {role}");
        cancellationToken.ThrowIfCancellationRequested();
        return user;
    }

    // The demo office's rooms, desks, cars and equipment: those the Development seed made get
    // their place on the plan; the others are added.
    private async Task<List<Resource>> ResourcesAsync(CancellationToken cancellationToken)
    {
        var resources = await db.Resources.Include(r => r.Teams).ToListAsync(cancellationToken);
        foreach (var place in DemoOffice.Places)
        {
            var resource = resources.FirstOrDefault(r => r.Name == place.Name);
            if (resource is null)
            {
                resource = new Resource
                {
                    Name = place.Name,
                    ResourceTypeId = place.TypeId,
                    Capacity = place.Capacity,
                    Location = place.Location,
                    CheckInRequired = place.CheckIn,
                    Features = place.Features.Select(f => new ResourceFeature { Name = f }).ToList(),
                };
                db.Resources.Add(resource);
                resources.Add(resource);
            }

            if (place.X is { } x && place.Y is { } y && resource.Location == place.Location)
            {
                resource.MapX = x;
                resource.MapY = y;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return resources;
    }

    private async Task FloorPlansAsync(DateTime now, CancellationToken cancellationToken)
    {
        foreach (var (location, file) in DemoOffice.Floors)
        {
            if (await db.FloorPlans.AnyAsync(p => p.Location == location, cancellationToken))
            {
                continue;
            }

            await using var image = typeof(DemoDataSeeder).Assembly.GetManifestResourceStream($"DemoFloors.{file}")
                ?? throw new InvalidOperationException($"The floor plan {file} isn't in the assembly.");
            using var data = new MemoryStream();
            await image.CopyToAsync(data, cancellationToken);
            db.FloorPlans.Add(new FloorPlan
            {
                Location = location,
                ContentType = "image/png",
                Data = data.ToArray(),
                Width = DemoOffice.PlanWidth,
                Height = DemoOffice.PlanHeight,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Problems reported at doors (ADR-0037): two open, one being fixed - the camera that's in
    // maintenance - and two fixed, whose reporters were told.
    private static (List<IssueReport> Reports, List<InboxMessage> Messages) Issues(
        Faker faker, DateTime now, IReadOnlyList<DemoPerson> people, IReadOnlyList<Resource> resources)
    {
        var reporters = people.Where(p => !p.IsAdmin && !p.IsManager).ToList();
        (string Resource, IssueCategory Category, string Text, IssueStatus Status, bool Blocking, double DaysAgo)[] specs =
        [
            ("Meeting Room C", IssueCategory.Display, "Ekranın HDMI girişi görüntü vermiyor.", IssueStatus.Open, false, 0.8),
            ("Desk 3-07", IssueCategory.Furniture, "Sandalyenin tekerleklerinden biri kırık.", IssueStatus.Open, false, 0.2),
            ("Camera C-1", IssueCategory.AudioVideo, "Lensin üzerinde çizik var, görüntü bulanık.", IssueStatus.InProgress, true, 5),
            ("Meeting Room A", IssueCategory.AirConditioning, "Klima çok soğuk üflüyor.", IssueStatus.Resolved, false, 9),
            ("Projector P-1", IssueCategory.Projector, "Uzaktan kumandanın pili bitmiş.", IssueStatus.Resolved, false, 12),
        ];

        var reports = new List<IssueReport>();
        var messages = new List<InboxMessage>();
        foreach (var spec in specs)
        {
            var resource = resources.FirstOrDefault(r => r.Name == spec.Resource);
            if (resource is null)
            {
                continue;
            }

            var reporter = faker.PickRandom(reporters);
            var report = new IssueReport
            {
                Id = Guid.CreateVersion7(),
                ResourceId = resource.Id,
                ReportedByUserId = reporter.Id,
                Category = spec.Category,
                Description = spec.Text,
                IsBlocking = spec.Blocking,
                Status = spec.Status,
                CreatedAt = now.AddDays(-spec.DaysAgo),
            };
            if (spec.Status == IssueStatus.Resolved)
            {
                report.ResolvedAt = report.CreatedAt.AddDays(1.5);
                messages.Add(new InboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    RecipientId = reporter.Id,
                    Kind = InboxMessageKind.IssueResolved,
                    ResourceName = resource.Name,
                    StartDateTime = report.CreatedAt,
                    EndDateTime = report.ResolvedAt.Value,
                    IssueCategory = spec.Category,
                    CreatedAt = report.ResolvedAt.Value,
                    ReadAt = report.ResolvedAt.Value.AddHours(3),
                });
            }

            reports.Add(report);
        }

        return (reports, messages);
    }

    // "Şükrü Çağlar" -> "sukru" and "caglar": an email's letters.
    private static string Ascii(string name)
    {
        var lower = name.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Replace("ı", "i");
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => c is >= 'a' and <= 'z');
        return new string(letters.ToArray());
    }

    private static void Succeeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Demo data: couldn't {what}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }
    }
}
