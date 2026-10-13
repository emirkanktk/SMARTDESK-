namespace SmartDesk.Domain.Entities;

public class ResourceType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // How this kind of resource may be booked (ADR-0046), as an Admin sets it. Each is unset
    // until then, and only the rules for every booking apply.

    // The longest a booking may last.
    public int? MaxDurationMinutes { get; set; }

    // How many days after today a booking may start: 0 is today only.
    public int? MaxDaysAhead { get; set; }

    // How many bookings of this kind one person may have that haven't ended yet.
    public int? MaxActivePerUser { get; set; }

    // Booked within the office's hours (OfficeHours) only.
    public bool OfficeHoursOnly { get; set; }

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
}
