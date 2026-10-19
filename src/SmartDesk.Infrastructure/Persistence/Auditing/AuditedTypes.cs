using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Features.Audit.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.Infrastructure.Persistence.Auditing;

// What the history keeps (ADR-0052): the rows someone may ask "who changed this, and when?"
// about, each under a name of its own, with the columns worth showing. Left out: bookkeeping a
// job writes (a reminder sent), personal lists (favourites, places in a waiting list, the inbox,
// calendar links), a series' own row (each of its bookings is in it), sign-in sessions, and
// pictures' bytes. A secret is kept only as whether there is one.
internal sealed class AuditedType(
    string name,
    Func<string, bool> audits,
    IReadOnlySet<string> secrets,
    Func<DbContext, object, Guid?> resourceOf,
    Func<DbContext, object, string?> labelOf)
{
    public string Name { get; } = name;

    public bool Audits(string property) => property != "Id" && audits(property);

    public bool IsSecret(string property) => secrets.Contains(property);

    public Guid? ResourceOf(DbContext context, object entity) => resourceOf(context, entity);

    public string? LabelOf(DbContext context, object entity) => labelOf(context, entity);
}

internal static class AuditedTypes
{
    private static readonly IReadOnlySet<string> NoSecrets = new HashSet<string>();

    private static readonly Dictionary<Type, AuditedType> ByType = new()
    {
        [typeof(Reservation)] = Of<Reservation>(
            AuditEntityTypes.Reservation,
            ignore: [
                nameof(Reservation.ReminderSentAt),
                nameof(Reservation.EndingSoonSentAt),
                nameof(Reservation.ReleaseWarningSentAt),
                nameof(Reservation.RequestedAt),
                nameof(Reservation.SeriesId),
            ],
            resource: (_, r) => r.ResourceId,
            label: (_, r) => Times(r.StartDateTime, r.EndDateTime)),
        [typeof(ReservationAttendee)] = Of<ReservationAttendee>(
            AuditEntityTypes.Attendee,
            resource: (context, a) => BookingOf(context, a)?.ResourceId,
            label: (context, a) => BookingOf(context, a) is { } r ? Times(r.StartDateTime, r.EndDateTime) : null),
        [typeof(Resource)] = Of<Resource>(
            AuditEntityTypes.Resource,
            ignore: [nameof(Resource.MapX), nameof(Resource.MapY), nameof(Resource.DoorScreenSetUpAt)],
            secrets: [nameof(Resource.CheckInCode), nameof(Resource.DoorScreenTokenHash)],
            resource: (_, r) => r.Id,
            label: (_, r) => r.Name),
        [typeof(ResourceFeature)] = Of<ResourceFeature>(
            AuditEntityTypes.ResourceFeature, resource: (_, f) => f.ResourceId, label: (_, f) => f.Name),
        [typeof(ResourceTeam)] = Of<ResourceTeam>(AuditEntityTypes.ResourceTeam, resource: (_, t) => t.ResourceId),
        [typeof(ResourcePhoto)] = Of<ResourcePhoto>(
            AuditEntityTypes.ResourcePhoto, ignore: [nameof(ResourcePhoto.Data)], resource: (_, p) => p.ResourceId),
        [typeof(ResourceBlock)] = Of<ResourceBlock>(
            AuditEntityTypes.ResourceBlock,
            resource: (_, b) => b.ResourceId,
            label: (_, b) => Times(b.StartDateTime, b.EndDateTime)),
        [typeof(ResourceType)] = Of<ResourceType>(AuditEntityTypes.ResourceType, label: (_, t) => t.Name),
        [typeof(OfficeHours)] = Of<OfficeHours>(AuditEntityTypes.OfficeHours),
        [typeof(FloorPlan)] = Of<FloorPlan>(AuditEntityTypes.FloorPlan, ignore: [nameof(FloorPlan.Data)], label: (_, p) => p.Location),
        [typeof(IssueReport)] = Of<IssueReport>(
            AuditEntityTypes.IssueReport, resource: (_, i) => i.ResourceId, label: (_, i) => i.Category.ToString()),
        // An account: who it is and what they may do, not the bookkeeping of signing in.
        [typeof(ApplicationUser)] = Of<ApplicationUser>(
            AuditEntityTypes.User,
            only: [
                nameof(ApplicationUser.FirstName),
                nameof(ApplicationUser.LastName),
                nameof(ApplicationUser.Email),
                nameof(ApplicationUser.Status),
                nameof(ApplicationUser.ManagerId),
                nameof(ApplicationUser.MustChangePassword),
                nameof(ApplicationUser.PasswordHash),
            ],
            secrets: [nameof(ApplicationUser.PasswordHash)],
            label: (_, u) => $"{u.FirstName} {u.LastName}".Trim()),
        // A role given or taken away, named as it is: role ids mean nothing to anyone.
        [typeof(IdentityUserRole<Guid>)] = Of<IdentityUserRole<Guid>>(
            AuditEntityTypes.UserRole,
            only: [nameof(IdentityUserRole<Guid>.UserId)],
            label: (context, r) => context.Set<IdentityRole<Guid>>().Find(r.RoleId)?.Name),
    };

    public static AuditedType? Find(Type type) => ByType.GetValueOrDefault(type);

    // A booking's or a block's times, as two UTC instants the screen shows in its own zone.
    private static string Times(DateTime start, DateTime end) =>
        string.Create(CultureInfo.InvariantCulture, $"{Utc(start):O} {Utc(end):O}");

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // The booking an attendee is on, when this save has it at hand.
    private static Reservation? BookingOf(DbContext context, ReservationAttendee attendee) =>
        context.Set<Reservation>().Local.FirstOrDefault(r => r.Id == attendee.ReservationId);

    private static AuditedType Of<T>(
        string name,
        string[]? ignore = null,
        string[]? only = null,
        string[]? secrets = null,
        Func<DbContext, T, Guid?>? resource = null,
        Func<DbContext, T, string?>? label = null)
    {
        var ignored = new HashSet<string>(ignore ?? []);
        var allowed = only is null ? null : new HashSet<string>(only);
        return new AuditedType(
            name,
            property => !ignored.Contains(property) && (allowed is null || allowed.Contains(property)),
            secrets is null ? NoSecrets : new HashSet<string>(secrets),
            (context, entity) => resource?.Invoke(context, (T)entity),
            (context, entity) => label?.Invoke(context, (T)entity));
    }
}
