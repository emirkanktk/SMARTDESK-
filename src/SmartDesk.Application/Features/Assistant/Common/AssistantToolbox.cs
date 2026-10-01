using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Resources.Queries.GetResourceById;
using SmartDesk.Application.Features.Resources.Queries.GetResources;
using SmartDesk.Application.Features.ResourceTypes.Queries.GetResourceTypes;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Assistant.Common;

// What the assistant may do (ADR-0029): look things up through the same queries the screens
// use, as the signed-in user, and propose a booking or a cancellation for the user to confirm.
// Nothing here writes. Times go to and from the model as local times in the user's zone
// ("2026-10-01T14:00"), so it never has to do time-zone arithmetic.
public sealed class AssistantToolbox(ISender sender, TimeZoneInfo zone, TimeProvider timeProvider) : IAssistantToolbox
{
    public const string FindFreeResources = "find_free_resources";
    public const string FindResources = "find_resources";
    public const string ListResourceTypes = "list_resource_types";
    public const string ListMyReservations = "list_my_reservations";
    public const string ProposeBooking = "propose_booking";
    public const string ProposeCancellation = "propose_cancellation";

    // Enough to choose from; more would only make the answer long.
    public const int MaxListed = 15;

    // One reply offers a handful of cards at most.
    public const int MaxProposals = 3;

    private const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<AssistantProposalDto> _proposals = [];

    // The cards this reply offers, in the order the model proposed them.
    public IReadOnlyList<AssistantProposalDto> Proposals => _proposals;

    public IReadOnlyList<AssistantTool> Tools { get; } =
    [
        new(
            FindFreeResources,
            "Resources the user may book that are free for the whole of a time range. Use it before proposing a booking.",
            new Dictionary<string, JsonElement>
            {
                ["start"] = LocalTime("Start of the range, local time"),
                ["end"] = LocalTime("End of the range, local time"),
                ["resourceTypeId"] = Id("Only this resource type (an id from list_resource_types)"),
                ["minCapacity"] = Schema(new { type = "integer", minimum = 1, description = "At least this many people" }),
            },
            ["start", "end"]),
        new(
            FindResources,
            "Active resources the user may use, whatever their bookings: to find a resource by name, place or equipment.",
            new Dictionary<string, JsonElement>
            {
                ["search"] = Schema(new { type = "string", description = "Words in the name, location, type or equipment" }),
                ["resourceTypeId"] = Id("Only this resource type (an id from list_resource_types)"),
            },
            []),
        new(
            ListResourceTypes,
            "The kinds of resources (meeting room, desk, car...) and their ids.",
            new Dictionary<string, JsonElement>(),
            []),
        new(
            ListMyReservations,
            "The user's own confirmed reservations that overlap a range of days, earliest first. Defaults to today and the next 30 days.",
            new Dictionary<string, JsonElement>
            {
                ["from"] = Schema(new { type = "string", description = "First day, YYYY-MM-DD" }),
                ["to"] = Schema(new { type = "string", description = "Last day, YYYY-MM-DD" }),
            },
            []),
        new(
            ProposeBooking,
            "Offer the user a booking: the app shows it as a card with a Reserve button. It books nothing - only the user's click does. Checks the resource is free first.",
            new Dictionary<string, JsonElement>
            {
                ["resourceId"] = Id("The resource, an id from a tool result"),
                ["start"] = LocalTime("Start, local time"),
                ["end"] = LocalTime("End, local time"),
                ["note"] = Schema(new { type = "string", maxLength = ReservationRules.MaxNoteLength, description = "What it's for, if the user said" }),
            },
            ["resourceId", "start", "end"]),
        new(
            ProposeCancellation,
            "Offer the user to cancel one of their reservations: the app shows a card with a Cancel button. It cancels nothing - only the user's click does.",
            new Dictionary<string, JsonElement>
            {
                ["reservationId"] = Id("The reservation, an id from list_my_reservations"),
            },
            ["reservationId"]),
    ];

    public async Task<string> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
    {
        try
        {
            object result = name switch
            {
                FindFreeResources => await FindFreeAsync(input, cancellationToken),
                FindResources => await FindAsync(input, cancellationToken),
                ListResourceTypes => await sender.Send(new GetResourceTypesQuery(), cancellationToken),
                ListMyReservations => await ListMineAsync(input, cancellationToken),
                ProposeBooking => await ProposeBookingAsync(input, cancellationToken),
                ProposeCancellation => await ProposeCancellationAsync(input, cancellationToken),
                _ => Error($"There is no tool called {name}."),
            };
            return JsonSerializer.Serialize(result, Json);
        }
        catch (Exception e) when (e is ValidationException or NotFoundException or ForbiddenAccessException
                                      or ConflictException or InputException)
        {
            var message = e is ValidationException validation
                ? string.Join(" ", validation.Errors.Select(error => error.ErrorMessage).Distinct())
                : e.Message;
            return JsonSerializer.Serialize(Error(message), Json);
        }
    }

    private async Task<object> FindFreeAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var (start, end) = (RequiredTime(input, "start"), RequiredTime(input, "end"));
        var free = await sender.Send(
            new GetAvailableResourcesQuery(start, end, OptionalId(input, "resourceTypeId"), OptionalInt(input, "minCapacity")),
            cancellationToken);

        return new
        {
            start = ToLocal(start),
            end = ToLocal(end),
            count = free.Count,
            resources = free.OrderBy(r => r.Capacity).ThenBy(r => r.Name).Take(MaxListed).Select(Describe),
        };
    }

    private async Task<object> FindAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var search = OptionalString(input, "search")?.Trim();
        var resources = await sender.Send(
            new GetResourcesQuery(OptionalId(input, "resourceTypeId"), ResourceStatus.Active),
            cancellationToken);

        var matching = string.IsNullOrEmpty(search)
            ? resources
            : resources.Where(r => Mentions(r, search)).ToList();
        return new
        {
            count = matching.Count,
            resources = matching.OrderBy(r => r.Name).Take(MaxListed).Select(Describe),
        };
    }

    private async Task<object> ListMineAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, zone).Date;
        var from = OptionalDay(input, "from") ?? today;
        var to = OptionalDay(input, "to") ?? today.AddDays(30);
        if (to < from)
        {
            throw new InputException("\"to\" is before \"from\".");
        }

        var (rangeStart, rangeEnd) = (ToUtc(from), ToUtc(to.AddDays(1)));
        var mine = await sender.Send(new GetMyReservationsQuery(), cancellationToken);
        var listed = mine
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartDateTime < rangeEnd && r.EndDateTime > rangeStart)
            .OrderBy(r => r.StartDateTime)
            .ToList();

        return new
        {
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            count = listed.Count,
            reservations = listed.Take(MaxListed).Select(r => new
            {
                id = r.Id,
                resource = r.ResourceName,
                location = r.ResourceLocation,
                start = ToLocal(r.StartDateTime),
                end = ToLocal(r.EndDateTime),
                note = r.Note,
            }),
        };
    }

    private async Task<object> ProposeBookingAsync(JsonElement input, CancellationToken cancellationToken)
    {
        LimitProposals();
        var resourceId = OptionalId(input, "resourceId") ?? throw new InputException("\"resourceId\" is missing.");
        var (start, end) = (RequiredTime(input, "start"), RequiredTime(input, "end"));
        var note = OptionalString(input, "note")?.Trim();
        if (note?.Length > ReservationRules.MaxNoteLength)
        {
            throw new InputException($"The note is longer than {ReservationRules.MaxNoteLength} characters.");
        }

        if (start < timeProvider.GetUtcNow().UtcDateTime - ReservationRules.PastStartGrace)
        {
            throw new InputException("That start time has already passed.");
        }

        // Not found for a resource the user may not use, like the screens.
        var resource = await sender.Send(new GetResourceByIdQuery(resourceId), cancellationToken);
        var free = await sender.Send(new GetAvailableResourcesQuery(start, end), cancellationToken);
        if (free.All(r => r.Id != resourceId))
        {
            throw new InputException(
                $"{resource.Name} isn't free for all of {ToLocal(start)}-{ToLocal(end)} (booked, blocked or not in service). Offer something else.");
        }

        _proposals.Add(new AssistantProposalDto(
            AssistantProposalDto.Book, resource.Id, null, resource.Name, start, end, string.IsNullOrEmpty(note) ? null : note));
        return new
        {
            proposed = true,
            resource = resource.Name,
            start = ToLocal(start),
            end = ToLocal(end),
            shownAs = "A card with a Reserve button under your reply. Nothing is booked until the user presses it.",
        };
    }

    private async Task<object> ProposeCancellationAsync(JsonElement input, CancellationToken cancellationToken)
    {
        LimitProposals();
        var reservationId = OptionalId(input, "reservationId") ?? throw new InputException("\"reservationId\" is missing.");
        var reservation = (await sender.Send(new GetMyReservationsQuery(), cancellationToken))
            .SingleOrDefault(r => r.Id == reservationId)
            ?? throw new InputException("That isn't one of the user's reservations.");
        if (reservation.Status != ReservationStatus.Confirmed || reservation.EndDateTime <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new InputException("That reservation is already cancelled or over.");
        }

        _proposals.Add(new AssistantProposalDto(
            AssistantProposalDto.Cancel, reservation.ResourceId, reservation.Id, reservation.ResourceName,
            reservation.StartDateTime, reservation.EndDateTime, reservation.Note));
        return new
        {
            proposed = true,
            resource = reservation.ResourceName,
            start = ToLocal(reservation.StartDateTime),
            end = ToLocal(reservation.EndDateTime),
            shownAs = "A card with a Cancel button under your reply. Nothing is cancelled until the user presses it.",
        };
    }

    private void LimitProposals()
    {
        if (_proposals.Count >= MaxProposals)
        {
            throw new InputException($"At most {MaxProposals} cards per reply; ask the user to pick first.");
        }
    }

    private static object Describe(ResourceDto r) => new
    {
        id = r.Id,
        name = r.Name,
        type = r.ResourceTypeName,
        capacity = r.Capacity,
        location = r.Location,
        features = r.Features,
    };

    private static bool Mentions(ResourceDto r, string search)
    {
        // Turkish casing too: "İ" and "i", "I" and "ı" are the same letter for a search.
        var compare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return new[] { r.Name, r.Location, r.ResourceTypeName, r.Description ?? string.Empty }
            .Concat(r.Features)
            .Any(text => compare.IndexOf(text, search, options) >= 0);
    }

    private string ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString(LocalFormat, CultureInfo.InvariantCulture);

    private DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            throw new InputException($"{unspecified.ToString(LocalFormat, CultureInfo.InvariantCulture)} doesn't exist in {zone.Id} (the clocks change). Pick another time.");
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    // A local time as asked for; one with an offset or a Z is taken as the instant it names.
    private DateTime RequiredTime(JsonElement input, string name)
    {
        var text = OptionalString(input, name)?.Trim() ?? throw new InputException($"\"{name}\" is missing.");
        if (DateTime.TryParseExact(text, [LocalFormat, "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return ToUtc(local);
        }

        if (DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset.UtcDateTime;
        }

        throw new InputException($"\"{name}\" should be a local time like 2026-10-01T14:00.");
    }

    private static DateTime? OptionalDay(JsonElement input, string name)
    {
        var text = OptionalString(input, name);
        if (text is null)
        {
            return null;
        }

        return DateTime.TryParseExact(text[..Math.Min(10, text.Length)], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new InputException($"\"{name}\" should be a day like 2026-10-01.");
    }

    private static string? OptionalString(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? OptionalId(JsonElement input, string name)
    {
        var text = OptionalString(input, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Guid.TryParse(text, out var id) ? id : throw new InputException($"\"{name}\" isn't an id from a tool result.");
    }

    // A number, or a number in a string ("4"), which models sometimes send.
    private static int? OptionalInt(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) => number,
            _ => throw new InputException($"\"{name}\" should be a whole number."),
        };
    }

    private static JsonElement Schema(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement LocalTime(string description) =>
        Schema(new { type = "string", description = $"{description}, YYYY-MM-DDTHH:mm" });

    private static JsonElement Id(string description) =>
        Schema(new { type = "string", format = "uuid", description });

    private static object Error(string message) => new { error = message };

    // A tool input the model can put right; its message goes back to the model as the answer.
    private sealed class InputException(string message) : Exception(message);
}
