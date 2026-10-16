namespace SmartDesk.Domain.Rules;

// Favourite resources (ADR-0051).
public static class FavoriteRules
{
    // A short list to book from at a glance; more than this and it's the resource list again.
    public const int MaxPerUser = 30;
}
