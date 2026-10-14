namespace SmartDesk.Infrastructure.Persistence.Seed.DemoData;

// The demo office (DemoDataSeeder): its floors, what's on them and where, and the words people
// put in their bookings. Names and words are what a Turkish office would write; the resource
// names keep the English the seeded ones have.
public static class DemoOffice
{
    // A resource of the demo office. Those named like DemoResourceSeeder's are the seeded ones,
    // which only get their place; the others are added. X and Y: where it sits on its floor's
    // plan, as fractions (ADR-0041). CheckIn: its bookings are checked in at the door (ADR-0030).
    public sealed record Place(
        string Name,
        Guid TypeId,
        int Capacity,
        string Location,
        double? X = null,
        double? Y = null,
        bool CheckIn = false,
        params string[] Features);

    public const string SecondFloor = "2nd Floor";
    public const string ThirdFloor = "3rd Floor";
    public const string FifthFloor = "5th Floor";

    // The plans, drawn as SVG next to these PNGs (Floors/), 1600 x 1000.
    public static readonly IReadOnlyList<(string Location, string File)> Floors =
    [
        (SecondFloor, "2nd-floor.png"),
        (ThirdFloor, "3rd-floor.png"),
        (FifthFloor, "5th-floor.png"),
    ];

    public const int PlanWidth = 1600;
    public const int PlanHeight = 1000;

    public static IReadOnlyList<Place> Places { get; } = BuildPlaces();

    // Kept free from now on, for the walkthrough in docs/DEMO_UI.md, which books them at times
    // of its own; they have their past like the rest.
    public static readonly IReadOnlySet<string> KeptForTheWalkthrough =
        new HashSet<string> { "Meeting Room A", "Board Room" };

    public static readonly IReadOnlyList<string> MeetingNotes =
    [
        "Sprint planlama",
        "Haftalık durum toplantısı",
        "Müşteri görüşmesi",
        "Bütçe değerlendirmesi",
        "İşe alım mülakatı",
        "Tasarım incelemesi",
        "Birebir görüşme",
        "Ürün demosu",
        "Retrospektif",
        "Yeni proje başlangıcı",
        "Satış hedefleri",
        "Tedarikçi toplantısı",
        "Eğitim: yeni CRM",
        "Çeyrek sonu değerlendirmesi",
    ];

    public static readonly IReadOnlyList<string> CallNotes =
    [
        "Müşteri araması",
        "Mülakat (online)",
        "Tedarikçi görüşmesi",
        "Odaklanma",
    ];

    public static readonly IReadOnlyList<string> TripNotes =
    [
        "Müşteri ziyareti - Ankara",
        "Fuar - İstanbul Fuar Merkezi",
        "Tedarikçi ziyareti - Gebze",
        "Banka ve noter işleri",
        "Saha kurulumu - Kocaeli",
        "Havalimanı transferi",
    ];

    public static readonly IReadOnlyList<string> DeclineReasons =
    [
        "Aynı saatte müşteri görüşmem var.",
        "O gün izinliyim.",
        "Sahada olacağım, katılamıyorum.",
        "Başka bir toplantıyla çakışıyor.",
    ];

    private static List<Place> BuildPlaces()
    {
        var places = new List<Place>
        {
            new("Meeting Room A", ResourceTypeSeedData.MeetingRoomId, 8, SecondFloor, 0.1875, 0.15),
            new("Meeting Room C", ResourceTypeSeedData.MeetingRoomId, 6, SecondFloor, 0.475, 0.15, CheckIn: true, "TV", "Whiteboard"),
            new("Meeting Room B", ResourceTypeSeedData.MeetingRoomId, 4, SecondFloor, 0.6937, 0.15),
            new("Phone Booth 2-1", ResourceTypeSeedData.MeetingRoomId, 1, SecondFloor, 0.8313, 0.18),
            new("Board Room", ResourceTypeSeedData.MeetingRoomId, 16, FifthFloor, 0.2375, 0.15),
            new("Focus Room 5-1", ResourceTypeSeedData.MeetingRoomId, 2, FifthFloor, 0.5313, 0.11, CheckIn: true, "Whiteboard"),
            new("Company Car 34 SD 001", ResourceTypeSeedData.CompanyCarId, 5, "Parking B1"),
            new("Company Car 34 SD 002", ResourceTypeSeedData.CompanyCarId, 5, "Parking B1"),
            new("Projector P-1", ResourceTypeSeedData.ProjectorId, 1, "IT Storage"),
            new("Projector P-2", ResourceTypeSeedData.ProjectorId, 1, "IT Storage", Features: "HDMI"),
            new("Camera C-1", ResourceTypeSeedData.CameraId, 1, "IT Storage"),
        };

        // Twelve desks on the 3rd floor in three rows of four, the first two the seeded ones;
        // the new ones are hot desks, checked in at the door.
        double[] columns = [0.1313, 0.3063, 0.4813, 0.6563];
        double[] rows = [0.15, 0.42, 0.70];
        for (var i = 0; i < 12; i++)
        {
            var name = $"Desk 3-{i + 1:00}";
            places.Add(new(name, ResourceTypeSeedData.DeskId, 1, ThirdFloor, columns[i % 4], rows[i / 4], CheckIn: i >= 2, "Monitor"));
        }

        // Six on the 5th floor, two rows of three.
        double[] fifthColumns = [0.1563, 0.35, 0.5437];
        double[] fifthRows = [0.58, 0.79];
        for (var i = 0; i < 6; i++)
        {
            places.Add(new($"Desk 5-{i + 1:00}", ResourceTypeSeedData.DeskId, 1, FifthFloor, fifthColumns[i % 3], fifthRows[i / 3], Features: "Monitor"));
        }

        return places;
    }
}
