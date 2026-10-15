namespace SmartDesk.Infrastructure.Persistence;

public static class ReservationConstraints
{
    public const string EndAfterStart = "CK_Reservations_EndAfterStart";

    // EXCLUDE USING gist ("ResourceId" WITH =, tstzrange("StartDateTime", "EndDateTime", '[)') WITH &&)
    // WHERE ("Status" IN (0, 2)), i.e. no two reservations of one resource that hold their time -
    // Confirmed, or waiting for approval (ADR-0049) - may overlap.
    public const string NoOverlap = "EX_Reservations_NoOverlap";
}
