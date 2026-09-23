namespace SmartDesk.Infrastructure.Persistence;

public static class ReservationConstraints
{
    public const string EndAfterStart = "CK_Reservations_EndAfterStart";

    // EXCLUDE USING gist ("ResourceId" WITH =, tstzrange("StartDateTime", "EndDateTime", '[)') WITH &&)
    // WHERE ("Status" = 0), i.e. no two Confirmed reservations of one resource may overlap.
    public const string NoOverlap = "EX_Reservations_NoOverlap";
}
