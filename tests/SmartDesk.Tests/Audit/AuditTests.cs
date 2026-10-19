using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Features.Audit.Common;
using SmartDesk.Application.Features.Audit.Queries.GetAuditEntries;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Auditing;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Audit;

// The history of changes (ADR-0052): written by the saves themselves, with who and when, what
// changed before and after, secrets kept only as whether there is one, and read back newest first.
public sealed class AuditTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    // Someone without an account, at the door (ADR-0050), and SmartDesk's own background jobs.
    private static readonly FakeCurrentUser Nobody = new(null);
    private static readonly FakeCurrentUser Job = new(null) { InRequest = false };

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten));

    public void Dispose() => _database.Dispose();

    private SmartDeskDbContext As(FakeCurrentUser user) => _database.CreateContext(new AuditInterceptor(user, _clock));

    private async Task<List<AuditEntry>> EntriesAsync()
    {
        await using var context = _database.CreateContext();
        return await context.AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    private static List<AuditChange> ChangesOf(AuditEntry entry) =>
        JsonSerializer.Deserialize<List<AuditChange>>(entry.Changes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private async Task<Reservation> BookAsync(Guid resourceId, FakeCurrentUser by)
    {
        await using var context = As(by);
        var reservation = new Reservation
        {
            ResourceId = resourceId,
            UserId = Ayse.UserId!.Value,
            StartDateTime = Ten.AddHours(1),
            EndDateTime = Ten.AddHours(2),
            Status = ReservationStatus.Confirmed,
            Note = "Sprint planning",
            ReminderMinutes = 15,
        };
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();
        return reservation;
    }

    [Fact]
    public async Task A_new_booking_is_written_with_what_it_has_by_whoever_made_it()
    {
        var room = await _database.AddResourceAsync("Board Room");

        var booking = await BookAsync(room.Id, Ayse);

        var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
        entry.EntityType.Should().Be(AuditEntityTypes.Reservation);
        entry.EntityId.Should().Be(booking.Id.ToString());
        entry.Action.Should().Be(AuditAction.Created);
        entry.Actor.Should().Be(AuditActor.User);
        entry.UserId.Should().Be(Ayse.UserId);
        entry.At.Should().Be(Ten);
        entry.ResourceId.Should().Be(room.Id);
        entry.Label.Should().Be("2026-10-19T11:00:00.0000000Z 2026-10-19T12:00:00.0000000Z");
        var changes = ChangesOf(entry);
        changes.Should().ContainEquivalentOf(new AuditChange("Status", null, "Confirmed"));
        changes.Should().ContainEquivalentOf(new AuditChange("StartDateTime", null, "2026-10-19T11:00:00.0000000Z"));
        changes.Should().ContainEquivalentOf(new AuditChange("Note", null, "Sprint planning"));
        changes.Select(c => c.Property).Should().NotContain(["Id", "ReminderSentAt", "RequestedAt", "SeriesId"]);
    }

    [Fact]
    public async Task A_change_keeps_only_what_changed_and_bookkeeping_alone_writes_nothing()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var booking = await BookAsync(room.Id, Ayse);

        await using (var context = As(Admin))
        {
            var tracked = await context.Reservations.SingleAsync(r => r.Id == booking.Id);
            tracked.Status = ReservationStatus.Cancelled;
            tracked.Note = "Moved to Friday";
            await context.SaveChangesAsync();
        }

        await using (var job = As(Job))
        {
            var tracked = await job.Reservations.SingleAsync(r => r.Id == booking.Id);
            tracked.ReminderSentAt = Ten;
            await job.SaveChangesAsync();
        }

        var entries = await EntriesAsync();
        entries.Should().HaveCount(2);
        var change = entries[1];
        change.Action.Should().Be(AuditAction.Updated);
        change.UserId.Should().Be(Admin.UserId);
        ChangesOf(change).Should().BeEquivalentTo(
        [
            new AuditChange("Status", "Confirmed", "Cancelled"),
            new AuditChange("Note", "Sprint planning", "Moved to Friday"),
        ]);
    }

    [Fact]
    public async Task A_deleted_row_keeps_what_it_had()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var block = new ResourceBlock
        {
            ResourceId = room.Id,
            StartDateTime = Ten,
            EndDateTime = Ten.AddHours(3),
            Reason = "Deep cleaning",
            CreatedByUserId = Admin.UserId!.Value,
        };
        await using (var context = As(Admin))
        {
            context.ResourceBlocks.Add(block);
            await context.SaveChangesAsync();
            context.ResourceBlocks.Remove(block);
            await context.SaveChangesAsync();
        }

        var deleted = (await EntriesAsync()).Last();
        deleted.EntityType.Should().Be(AuditEntityTypes.ResourceBlock);
        deleted.Action.Should().Be(AuditAction.Deleted);
        deleted.ResourceId.Should().Be(room.Id);
        ChangesOf(deleted).Should().ContainEquivalentOf(new AuditChange("Reason", "Deep cleaning", null));
    }

    [Fact]
    public async Task A_secret_is_kept_only_as_whether_there_is_one()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var oldCode = room.CheckInCode;

        await using (var context = As(Admin))
        {
            var tracked = await context.Resources.SingleAsync(r => r.Id == room.Id);
            tracked.CheckInCode = "f".PadLeft(32, '0');
            tracked.DoorScreenTokenHash = new string('a', 64);
            tracked.DoorScreenSetUpAt = Ten;
            tracked.MapX = 0.5;
            await context.SaveChangesAsync();
        }

        var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
        entry.Label.Should().Be("Board Room");
        ChangesOf(entry).Should().BeEquivalentTo(
        [
            new AuditChange("CheckInCode", "***", "***"),
            new AuditChange("DoorScreenTokenHash", null, "***"),
        ]);
        entry.Changes.Should().NotContain(oldCode).And.NotContain(new string('a', 64));
    }

    [Theory]
    [InlineData("user", AuditActor.User)]
    [InlineData("nobody", AuditActor.Anonymous)]
    [InlineData("job", AuditActor.System)]
    public async Task Each_change_says_who_it_came_from(string who, AuditActor actor)
    {
        var room = await _database.AddResourceAsync("Board Room");
        var by = who switch { "user" => Ayse, "nobody" => Nobody, _ => Job };

        await BookAsync(room.Id, by);

        var entry = (await EntriesAsync()).Single();
        entry.Actor.Should().Be(actor);
        entry.UserId.Should().Be(actor == AuditActor.User ? Ayse.UserId : null);
    }

    [Fact]
    public async Task What_the_history_leaves_out_writes_nothing()
    {
        var room = await _database.AddResourceAsync("Board Room");
        await using (var context = As(Ayse))
        {
            context.FavoriteResources.Add(new FavoriteResource { UserId = Ayse.UserId!.Value, ResourceId = room.Id, CreatedAt = Ten });
            context.InboxMessages.Add(new InboxMessage
            {
                Id = Guid.NewGuid(),
                RecipientId = Ayse.UserId!.Value,
                Kind = InboxMessageKind.IssueResolved,
                ResourceName = "Board Room",
                StartDateTime = Ten,
                EndDateTime = Ten,
                CreatedAt = Ten,
            });
            await context.SaveChangesAsync();
        }

        (await EntriesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_save_that_failed_and_is_tried_again_writes_once()
    {
        var room = await _database.AddResourceAsync("Board Room");
        await using var context = As(Ayse);
        var booking = new Reservation
        {
            ResourceId = Guid.NewGuid(),
            UserId = Ayse.UserId!.Value,
            StartDateTime = Ten,
            EndDateTime = Ten.AddHours(1),
        };
        context.Reservations.Add(booking);

        await FluentActions.Awaiting(() => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        booking.ResourceId = room.Id;
        await context.SaveChangesAsync();

        (await EntriesAsync()).Should().ContainSingle().Which.ResourceId.Should().Be(room.Id);
    }

    [Fact]
    public async Task An_account_is_kept_without_the_bookkeeping_of_signing_in_and_a_role_by_its_name()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "mehmet@smartdesk.local",
            NormalizedUserName = "MEHMET@SMARTDESK.LOCAL",
            Email = "mehmet@smartdesk.local",
            NormalizedEmail = "MEHMET@SMARTDESK.LOCAL",
            FirstName = "Mehmet",
            LastName = "Demir",
            PasswordHash = "AQAAAAIAAYagAAAAE-not-a-real-hash",
            SecurityStamp = "STAMP",
        };
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = Roles.Manager, NormalizedName = "MANAGER" };
        await using (var context = As(Admin))
        {
            context.Users.Add(user);
            context.Roles.Add(role);
            context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
            await context.SaveChangesAsync();
        }

        await using (var context = As(Admin))
        {
            var tracked = await context.Users.SingleAsync(u => u.Id == user.Id);
            tracked.AccessFailedCount = 3;
            await context.SaveChangesAsync();
        }

        var entries = await EntriesAsync();
        entries.Should().HaveCount(2, "a failed sign-in counted isn't a change worth telling");
        var account = entries.Single(e => e.EntityType == AuditEntityTypes.User);
        account.Label.Should().Be("Mehmet Demir");
        ChangesOf(account).Select(c => c.Property).Should().BeEquivalentTo(
            ["FirstName", "LastName", "Email", "Status", "MustChangePassword", "PasswordHash"]);
        account.Changes.Should().NotContain("not-a-real-hash").And.NotContain("STAMP");
        var given = entries.Single(e => e.EntityType == AuditEntityTypes.UserRole);
        given.Label.Should().Be(Roles.Manager);
        given.EntityId.Should().Be($"{user.Id}|{role.Id}");
    }

    // --- Reading it back ---

    private async Task<AuditPageDto> ReadAsync(GetAuditEntriesQuery query)
    {
        await using var context = _database.CreateContext();
        var names = new FakeIdentityService(new Dictionary<Guid, string>
        {
            [Admin.UserId!.Value] = "Selin Admin",
            [Ayse.UserId!.Value] = "Ayşe Yılmaz",
        });
        return await new GetAuditEntriesQueryHandler(context, names).Handle(query, CancellationToken.None);
    }

    [Fact]
    public async Task It_reads_newest_first_a_page_at_a_time()
    {
        var room = await _database.AddResourceAsync("Board Room");
        for (var i = 0; i < 3; i++)
        {
            await BookAsync(room.Id, Ayse);
        }

        var first = await ReadAsync(new GetAuditEntriesQuery(Take: 2));
        var second = await ReadAsync(new GetAuditEntriesQuery(Take: 2, Before: first.Items[^1].Id));

        first.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        first.Items[0].Id.Should().BeGreaterThan(first.Items[1].Id);
        second.Items.Should().ContainSingle();
        second.HasMore.Should().BeFalse();
        second.Items[0].Id.Should().BeLessThan(first.Items[1].Id);
    }

    [Fact]
    public async Task It_filters_by_area_resource_row_person_and_time_and_names_whom_it_mentions()
    {
        var board = await _database.AddResourceAsync("Board Room");
        var desk = await _database.AddResourceAsync("Desk 3-01");
        var booking = await BookAsync(board.Id, Ayse);
        _clock.Advance(TimeSpan.FromHours(1));
        await using (var context = As(Admin))
        {
            var tracked = await context.Resources.SingleAsync(r => r.Id == desk.Id);
            tracked.Status = ResourceStatus.Maintenance;
            await context.SaveChangesAsync();
        }

        (await ReadAsync(new GetAuditEntriesQuery(Area: "bookings"))).Items.Should().ContainSingle()
            .Which.EntityId.Should().Be(booking.Id.ToString());
        (await ReadAsync(new GetAuditEntriesQuery(Area: "resources"))).Items.Should().ContainSingle()
            .Which.ResourceId.Should().Be(desk.Id);
        (await ReadAsync(new GetAuditEntriesQuery(ResourceId: board.Id))).Items.Should().ContainSingle();
        (await ReadAsync(new GetAuditEntriesQuery(EntityType: "Resource", EntityId: desk.Id.ToString()))).Items
            .Should().ContainSingle().Which.Action.Should().Be(AuditAction.Updated);
        (await ReadAsync(new GetAuditEntriesQuery(UserId: Admin.UserId))).Items.Should().ContainSingle();
        (await ReadAsync(new GetAuditEntriesQuery(From: Ten.AddMinutes(30)))).Items.Should().ContainSingle()
            .Which.ResourceId.Should().Be(desk.Id);
        (await ReadAsync(new GetAuditEntriesQuery(To: Ten.AddMinutes(30)))).Items.Should().ContainSingle()
            .Which.ResourceId.Should().Be(board.Id);

        var all = await ReadAsync(new GetAuditEntriesQuery());
        all.People.Should().Contain(Ayse.UserId!.Value, "Ayşe Yılmaz").And.Contain(Admin.UserId!.Value, "Selin Admin");
        all.Resources.Should().Contain(board.Id, "Board Room").And.Contain(desk.Id, "Desk 3-01");
    }

    [Theory]
    [InlineData("everything", 50, null, false)]
    [InlineData(null, 0, null, false)]
    [InlineData(null, 201, null, false)]
    [InlineData(null, 50, "x", false)]
    [InlineData("accounts", 200, null, true)]
    public void A_question_has_a_known_area_a_sensible_page_and_a_row_with_its_kind(
        string? area, int take, string? entityId, bool valid)
    {
        var result = new GetAuditEntriesQueryValidator().Validate(
            new GetAuditEntriesQuery(Area: area, EntityId: entityId, Take: take));

        result.IsValid.Should().Be(valid);
    }
}
