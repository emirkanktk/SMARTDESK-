namespace SmartDesk.Application.Common.Extensions;

public static class DateTimeExtensions
{
    // Timestamps are UTC over the wire (docs/API_CONTRACTS.md), but how a DateTime arrives
    // depends on how it was bound: query-string binding turns "...Z" into local time, JSON
    // without an offset gives Kind=Unspecified. Npgsql refuses anything but Kind=Utc for
    // timestamptz columns, so every incoming timestamp goes through this first.
    // Unspecified is taken to already be UTC, per the API convention.
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
