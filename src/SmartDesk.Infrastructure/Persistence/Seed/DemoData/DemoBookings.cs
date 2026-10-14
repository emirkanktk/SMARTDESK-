using Bogus;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Persistence.Seed.DemoData;

// Someone in the demo office. Team: their manager's id, or their own for a manager; none for
// the admin, who books nothing.
public sealed record DemoPerson(Guid Id, Guid? TeamId, bool IsManager, bool IsAdmin = false);

// A resource the demo office books. TeamId: the team it belongs to, if any (ADR-0022).
public sealed record DemoResource(
    Guid Id,
    string Name,
    Guid TypeId,
    int Capacity,
    bool CheckIn,
    Guid? TeamId,
    bool KeptFree);

// An office's bookings over six weeks behind and two ahead, worked out in memory: who books what,
// when, and what became of each - checked in, released for want of a check-in, cancelled - with
// the people invited, the weekly meetings and their invitations. Busiest mid-week, mornings and
// early afternoons; bookings thin out further ahead, as people book nearer the day. Its only
// randomness is its Faker, so a fixed seed gives the same office every time. It keeps the rules
// a booking keeps (no two Confirmed ones of a resource overlap, a team's resource is its team's)
// itself, since it writes past the API.
public sealed class DemoBookings
{
    public const int DaysBack = 42;
    public const int DaysAhead = 14;

    // CheckIn:GraceMinutes as shipped (ADR-0040): when a booking nobody checked in to is released.
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    private static readonly (TimeOnly Time, float Weight)[] MeetingStarts =
    [
        (new(9, 0), 2), (new(9, 30), 3), (new(10, 0), 6), (new(10, 30), 5), (new(11, 0), 5),
        (new(11, 30), 3), (new(12, 0), 1), (new(13, 0), 2), (new(13, 30), 3), (new(14, 0), 6),
        (new(14, 30), 5), (new(15, 0), 5), (new(15, 30), 4), (new(16, 0), 3), (new(16, 30), 2),
        (new(17, 0), 1),
    ];

    private readonly Faker _faker;
    private readonly OfficeWeek _office;
    private readonly DateTime _now;
    private readonly DateOnly _today;
    private readonly IReadOnlyList<DemoPerson> _bookers;
    private readonly IReadOnlyList<DemoResource> _resources;
    private readonly Dictionary<Guid, List<(DateTime Start, DateTime End)>> _taken = [];
    private readonly Dictionary<Guid, DemoResource> _favouriteDesk = [];

    public DemoBookings(Faker faker, OfficeWeek office, DateTime now, IReadOnlyList<DemoPerson> people, IReadOnlyList<DemoResource> resources)
    {
        _faker = faker;
        _office = office;
        _now = now;
        _today = office.DateOf(now);
        _bookers = people.Where(p => !p.IsAdmin).ToList();
        _resources = resources;
        PickFavouriteDesks();
    }

    public List<Reservation> Reservations { get; } = [];
    public List<ReservationSeries> Series { get; } = [];
    public List<InboxMessage> Messages { get; } = [];
    public List<ResourceBlock> Blocks { get; } = [];
    public List<WaitlistEntry> Waiting { get; } = [];

    private IEnumerable<DemoResource> OfType(Guid typeId) => _resources.Where(r => r.TypeId == typeId);

    // Everything, in an order that never changes: what's planned well ahead first - a blocked
    // window, the weekly meetings - then day by day.
    public void Generate(Guid adminId)
    {
        BlockWindow(adminId);
        WeeklyMeetings();
        for (var date = _today.AddDays(-DaysBack); date <= _today.AddDays(DaysAhead); date = date.AddDays(1))
        {
            // Fewer bookings the further ahead: people book nearer the day.
            var ahead = date.DayNumber - _today.DayNumber;
            var booked = ahead <= 0 ? 1.0 : Math.Max(0.25, 1 - ahead / (double)DaysAhead);
            if (_office.IsWorkday(date.DayOfWeek))
            {
                Desks(date, booked);
                Meetings(date, booked);
                Trips(date, booked);
                Equipment(date, booked);
            }
            else
            {
                WeekendTrips(date, booked);
            }
        }

        WaitingList();
    }

    // How busy a weekday is: hybrid work fills the middle of the week.
    private static double Busy(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 0.85,
        DayOfWeek.Tuesday or DayOfWeek.Wednesday => 1.0,
        DayOfWeek.Thursday => 0.95,
        DayOfWeek.Friday => 0.6,
        _ => 0.2,
    };

    // Each person sits near their team: the first team on the 3rd floor's first six desks, the
    // second on its last six, the third and the managers on the 5th floor. Spread over a group's
    // desks, so each has somebody who likes it.
    private void PickFavouriteDesks()
    {
        var thirdFloor = OfType(ResourceTypeSeedData.DeskId).Where(d => d.Name.StartsWith("Desk 3-")).OrderBy(d => d.Name).ToList();
        var fifthFloor = OfType(ResourceTypeSeedData.DeskId).Where(d => d.Name.StartsWith("Desk 5-")).OrderBy(d => d.Name).ToList();
        var teams = _bookers.Where(p => p.IsManager).Select(p => p.Id).ToList();
        var groups = _bookers.GroupBy(person =>
        {
            var team = teams.IndexOf(person.TeamId ?? Guid.Empty);
            return person.IsManager || team >= 2 ? 2 : team;
        });
        foreach (var group in groups)
        {
            var desks = group.Key switch
            {
                0 => thirdFloor.Take(6).ToList(),
                1 => thirdFloor.Skip(6).ToList(),
                _ => fifthFloor,
            };
            if (desks.Count == 0)
            {
                continue;
            }

            var shuffled = _faker.Random.Shuffle(desks).ToList();
            var next = 0;
            foreach (var person in group)
            {
                _favouriteDesk[person.Id] = shuffled[next++ % shuffled.Count];
            }
        }
    }

    private void Desks(DateOnly date, double booked)
    {
        var desks = OfType(ResourceTypeSeedData.DeskId).ToList();
        foreach (var person in _faker.Random.Shuffle(_bookers).ToList())
        {
            var comes = (person.IsManager ? 0.5 : 0.65) * Busy(date.DayOfWeek) * booked;
            if (!_faker.Random.Bool((float)comes) || !_favouriteDesk.TryGetValue(person.Id, out var favourite))
            {
                continue;
            }

            var shape = _faker.Random.Double();
            var (from, to) = shape < 0.7
                ? (_faker.Random.Bool(0.3f) ? _office.Opens.AddMinutes(30) : _office.Opens, _faker.Random.Bool(0.3f) ? _office.Closes.AddMinutes(-30) : _office.Closes)
                : shape < 0.85 ? (_office.Opens, new TimeOnly(13, 0)) : (new TimeOnly(13, 0), _office.Closes);
            var start = At(date, from);
            var end = At(date, to);

            // Their own desk, or one free near it, or any free one.
            var choices = new[] { favourite }
                .Concat(desks.Where(d => d.Name[..6] == favourite.Name[..6] && d != favourite))
                .Concat(desks.Where(d => d.Name[..6] != favourite.Name[..6]));
            foreach (var desk in choices)
            {
                if (TryBook(desk, person, start, end, note: null, attendees: []))
                {
                    break;
                }
            }
        }
    }

    private void Meetings(DateOnly date, double booked)
    {
        foreach (var room in OfType(ResourceTypeSeedData.MeetingRoomId))
        {
            var perDay = room.Capacity switch { 1 => 4.0, 2 => 2.0, <= 4 => 3.0, <= 8 => 3.5, _ => 1.5 };
            var wanted = (int)Math.Round(perDay * Busy(date.DayOfWeek) * booked * _faker.Random.Double(0.7, 1.3));
            for (int made = 0, tries = 0; made < wanted && tries < wanted * 4; tries++)
            {
                var from = Weighted(MeetingStarts.Select(s => s.Time).ToArray(), MeetingStarts.Select(s => s.Weight).ToArray());
                var minutes = room.Capacity switch
                {
                    1 => Weighted([15, 30, 45], [3f, 4f, 2f]),
                    2 => Weighted([30, 60, 90], [3f, 4f, 2f]),
                    _ => Weighted([30, 45, 60, 90, 120], [3f, 2f, 5f, 2f, 1f]),
                };
                var to = from.AddMinutes(minutes);
                if (from < _office.Opens || to > _office.Closes || to < from)
                {
                    continue;
                }

                var organizer = Organizer(room);
                var invited = room.Capacity switch
                {
                    1 => 0,
                    2 => _faker.Random.Int(0, 1),
                    _ => _faker.Random.Int(0, Math.Min(room.Capacity - 1, 5)),
                };
                var attendees = Teammates(organizer, invited);
                var note = room.Capacity <= 2
                    ? _faker.PickRandom(DemoOffice.CallNotes.ToList())
                    : _faker.Random.Bool(0.8f) ? _faker.PickRandom(DemoOffice.MeetingNotes.ToList()) : null;
                if (TryBook(room, organizer, At(date, from), At(date, to), note, attendees))
                {
                    made++;
                }
            }
        }
    }

    private void Trips(DateOnly date, double booked)
    {
        foreach (var car in OfType(ResourceTypeSeedData.CompanyCarId))
        {
            if (!_faker.Random.Bool((float)(0.35 * Busy(date.DayOfWeek) * booked)))
            {
                continue;
            }

            var shape = _faker.Random.Double();
            var (start, end) = shape < 0.6
                ? Span(date, new TimeOnly(_faker.Random.Int(9, 14), 0), _faker.Random.Int(2, 5) * 60)
                : shape < 0.9
                    ? (At(date, new TimeOnly(8, 0)), At(date, new TimeOnly(19, 0)))
                    : (At(date, new TimeOnly(9, 0)), At(date.AddDays(1), new TimeOnly(18, 0)));
            TryBook(car, _faker.PickRandom(_bookers.ToList()), start, end, _faker.PickRandom(DemoOffice.TripNotes.ToList()), []);
        }
    }

    private void WeekendTrips(DateOnly date, double booked)
    {
        foreach (var car in OfType(ResourceTypeSeedData.CompanyCarId))
        {
            if (_faker.Random.Bool((float)(0.1 * booked)))
            {
                TryBook(car, _faker.PickRandom(_bookers.ToList()), At(date, new TimeOnly(10, 0)), At(date, new TimeOnly(16, 0)),
                    _faker.PickRandom(DemoOffice.TripNotes.ToList()), []);
            }
        }
    }

    // Projectors and cameras, for the meetings that need one.
    private void Equipment(DateOnly date, double booked)
    {
        foreach (var item in _resources.Where(r => r.TypeId == ResourceTypeSeedData.ProjectorId || r.TypeId == ResourceTypeSeedData.CameraId))
        {
            if (!_faker.Random.Bool((float)(0.3 * Busy(date.DayOfWeek) * booked)))
            {
                continue;
            }

            var from = Weighted(MeetingStarts.Select(s => s.Time).ToArray(), MeetingStarts.Select(s => s.Weight).ToArray());
            var to = from.AddHours(_faker.Random.Int(1, 3));
            if (to <= _office.Closes && to > from)
            {
                TryBook(item, Organizer(item), At(date, from), At(date, to), null, []);
            }
        }
    }

    // A team's weekly meeting in the biggest checked-in room, as a series (ADR-0035) from six
    // weeks ago to four ahead: the first manager's on Mondays, the second's on Thursdays.
    private void WeeklyMeetings()
    {
        var room = OfType(ResourceTypeSeedData.MeetingRoomId)
            .Where(r => !r.KeptFree && r.TeamId is null)
            .OrderByDescending(r => r.Capacity)
            .FirstOrDefault();
        var managers = _bookers.Where(p => p.IsManager).Take(2).ToList();
        if (room is null)
        {
            return;
        }

        (DayOfWeek Day, TimeOnly From, TimeOnly To, string Note)[] meetings =
        [
            (DayOfWeek.Monday, new(10, 0), new(10, 45), "Haftalık planlama"),
            (DayOfWeek.Thursday, new(16, 0), new(17, 0), "Haftalık satış değerlendirmesi"),
        ];
        for (var i = 0; i < managers.Count && i < meetings.Length; i++)
        {
            var (day, from, to, note) = meetings[i];
            var manager = managers[i];
            var first = _today.AddDays(-DaysBack);
            while (first.DayOfWeek != day)
            {
                first = first.AddDays(1);
            }

            var series = new ReservationSeries
            {
                Id = Guid.CreateVersion7(),
                UserId = manager.Id,
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 1,
                DaysOfWeek = RecurrenceRules.Flag(day),
                Until = _today.AddDays(28),
                TimeZone = _office.Zone.Id,
                CreatedAt = At(first, from).AddDays(-3),
            };
            Series.Add(series);

            var attendees = Teammates(manager, room.Capacity - 1);
            foreach (var date in RecurrenceRules.Occurrences(At(first, from), At(first, to), series.Frequency, 1, series.DaysOfWeek, series.Until, _office.Zone))
            {
                TryBook(room, manager, date.Start, date.End, note, attendees, series);
            }

            foreach (var person in attendees)
            {
                Messages.Add(new InboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    RecipientId = person.Id,
                    Kind = InboxMessageKind.SeriesInvitation,
                    SenderId = manager.Id,
                    ResourceName = room.Name,
                    StartDateTime = At(first, from),
                    EndDateTime = At(first, to),
                    Note = note,
                    SeriesId = series.Id,
                    CreatedAt = series.CreatedAt,
                    ReadAt = series.CreatedAt.AddHours(2),
                });
            }
        }
    }

    // A small room out of use for a morning the day after tomorrow, or the next workday after.
    private void BlockWindow(Guid adminId)
    {
        var booth = OfType(ResourceTypeSeedData.MeetingRoomId).Where(r => !r.KeptFree).OrderBy(r => r.Capacity).FirstOrDefault();
        if (booth is null)
        {
            return;
        }

        var date = _today.AddDays(2);
        while (!_office.IsWorkday(date.DayOfWeek))
        {
            date = date.AddDays(1);
        }

        var block = new ResourceBlock
        {
            Id = Guid.CreateVersion7(),
            ResourceId = booth.Id,
            StartDateTime = At(date, _office.Opens),
            EndDateTime = At(date, new TimeOnly(12, 0)),
            Reason = "Ses yalıtımı onarımı",
            CreatedByUserId = adminId,
        };
        Blocks.Add(block);
        Take(booth.Id, block.StartDateTime, block.EndDateTime);
    }

    // Two people waiting (ADR-0043): for a meeting room taken tomorrow or later, and a car.
    private void WaitingList()
    {
        var upcoming = Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartDateTime > _now.AddHours(20))
            .OrderBy(r => r.StartDateTime)
            .ToList();
        var room = upcoming.FirstOrDefault(r => _resources.Single(x => x.Id == r.ResourceId).TypeId == ResourceTypeSeedData.MeetingRoomId);
        var car = upcoming.FirstOrDefault(r => _resources.Single(x => x.Id == r.ResourceId).TypeId == ResourceTypeSeedData.CompanyCarId);
        foreach (var taken in new[] { room, car }.OfType<Reservation>())
        {
            var resource = _resources.Single(r => r.Id == taken.ResourceId);
            var who = _bookers.FirstOrDefault(p => p.Id != taken.UserId && (resource.TeamId is null || p.TeamId == resource.TeamId));
            if (who is not null)
            {
                Waiting.Add(new WaitlistEntry
                {
                    Id = Guid.CreateVersion7(),
                    ResourceId = taken.ResourceId,
                    UserId = who.Id,
                    StartDateTime = taken.StartDateTime,
                    EndDateTime = taken.EndDateTime,
                    CreatedAt = _now.AddHours(-2),
                });
            }
        }
    }

    // Books [start, end) of the resource for its owner if it's free, and decides what became of
    // it: cancelled, released for want of a check-in, or checked in. False when it's taken.
    private bool TryBook(
        DemoResource resource,
        DemoPerson owner,
        DateTime start,
        DateTime end,
        string? note,
        IReadOnlyList<DemoPerson> attendees,
        ReservationSeries? series = null)
    {
        if ((resource.KeptFree && end > _now) || IsTaken(resource.Id, start, end))
        {
            return false;
        }

        var reservation = new Reservation
        {
            Id = Guid.CreateVersion7(),
            ResourceId = resource.Id,
            UserId = owner.Id,
            StartDateTime = start,
            EndDateTime = end,
            Status = ReservationStatus.Confirmed,
            Note = note,
            CheckInRequired = resource.CheckIn,
            SeriesId = series?.Id,
        };

        // Booked a few days before, and the reminder, when there is one, gone out on time.
        var bookedAt = Min(start.AddDays(-_faker.Random.Int(1, 7)).AddHours(-_faker.Random.Int(0, 8)), _now.AddMinutes(-30));
        if (_faker.Random.Bool(0.4f))
        {
            reservation.ReminderMinutes = 15;
            if (start.AddMinutes(-15) <= _now)
            {
                reservation.ReminderSentAt = start.AddMinutes(-15);
            }
        }

        var past = end <= _now;
        if (_faker.Random.Bool(past ? 0.07f : 0.04f))
        {
            reservation.Status = ReservationStatus.Cancelled;
        }
        else if (resource.CheckIn && start <= _now)
        {
            // Under way: checked in, or it would be released before anyone sees it. Over: most
            // were, and the rest released at the end of their grace.
            if (past && _faker.Random.Bool(0.18f))
            {
                reservation.Status = ReservationStatus.Cancelled;
                reservation.ReleasedAt = start + Grace;
                reservation.ReleaseWarningSentAt = start + Grace - TimeSpan.FromMinutes(5);
            }
            else
            {
                reservation.CheckedInAt = Min(start.AddMinutes(_faker.Random.Int(-8, 12)), _now);
                reservation.CheckedInByUserId = attendees.Count > 0 && _faker.Random.Bool(0.3f) ? _faker.PickRandom(attendees.ToList()).Id : owner.Id;
            }
        }

        foreach (var person in attendees)
        {
            var answer = past
                ? Weighted([AttendeeResponse.Accepted, AttendeeResponse.Declined, AttendeeResponse.Pending], [0.75f, 0.1f, 0.15f])
                : Weighted([AttendeeResponse.Accepted, AttendeeResponse.Declined, AttendeeResponse.Pending], [0.45f, 0.05f, 0.5f]);
            var answeredAt = answer == AttendeeResponse.Pending ? (DateTime?)null : Min(bookedAt.AddHours(_faker.Random.Int(1, 30)), _now);
            reservation.Attendees.Add(new ReservationAttendee
            {
                UserId = person.Id,
                Response = answer,
                RespondedAt = answeredAt,
                DeclineReason = answer == AttendeeResponse.Declined ? _faker.PickRandom(DemoOffice.DeclineReasons.ToList()) : null,
            });

            // Invitations to what's still to come are in the inboxes; a series has one of its own.
            if (series is null && start > _now && reservation.Status == ReservationStatus.Confirmed)
            {
                Messages.Add(new InboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    RecipientId = person.Id,
                    Kind = InboxMessageKind.MeetingInvitation,
                    SenderId = owner.Id,
                    ReservationId = reservation.Id,
                    ResourceName = resource.Name,
                    StartDateTime = start,
                    EndDateTime = end,
                    Note = note,
                    CreatedAt = bookedAt,
                    ReadAt = answeredAt,
                });
            }
        }

        Reservations.Add(reservation);
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            Take(resource.Id, start, end);
        }

        return true;
    }

    // Who books a resource: its team's people when it has a team, managers twice as often as
    // the rest otherwise.
    private DemoPerson Organizer(DemoResource resource)
    {
        if (resource.TeamId is { } team)
        {
            return _faker.PickRandom(_bookers.Where(p => p.TeamId == team).ToList());
        }

        var weighted = _bookers.SelectMany(p => p.IsManager ? new[] { p, p } : [p]).ToList();
        return _faker.PickRandom(weighted);
    }

    // Up to `count` of the organizer's team, never the organizer.
    private List<DemoPerson> Teammates(DemoPerson organizer, int count)
    {
        var team = _bookers.Where(p => p.Id != organizer.Id && p.TeamId == organizer.TeamId).ToList();
        return _faker.Random.Shuffle(team).Take(Math.Min(count, team.Count)).ToList();
    }

    private bool IsTaken(Guid resourceId, DateTime start, DateTime end) =>
        _taken.TryGetValue(resourceId, out var taken) && taken.Any(t => t.Start < end && start < t.End);

    private void Take(Guid resourceId, DateTime start, DateTime end)
    {
        if (!_taken.TryGetValue(resourceId, out var taken))
        {
            _taken[resourceId] = taken = [];
        }

        taken.Add((start, end));
    }

    private DateTime At(DateOnly date, TimeOnly time) => _office.Zone.LocalToUtc(date.ToDateTime(time));

    private (DateTime Start, DateTime End) Span(DateOnly date, TimeOnly from, int minutes)
    {
        var start = At(date, from);
        return (start, start.AddMinutes(minutes));
    }

    // One of the items, each as likely as its weight. Bogus's WeightedRandom wants weights that
    // add up to 1, and otherwise always picks the first; this one takes them as they are.
    private T Weighted<T>(T[] items, float[] weights)
    {
        var pick = _faker.Random.Double() * weights.Sum();
        for (var i = 0; i < items.Length; i++)
        {
            pick -= weights[i];
            if (pick < 0)
            {
                return items[i];
            }
        }

        return items[^1];
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
