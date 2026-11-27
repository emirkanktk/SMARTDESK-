namespace SmartDesk.Domain.Rules;

// Announcements (ADR-0065).
public static class AnnouncementRules
{
    // A headline: read at a glance on a dashboard, a lobby screen or a lock screen.
    public const int MaxTitleLength = 100;

    public const int MaxBodyLength = 1000;

    // News that stays longer than this is no longer news.
    public const int MaxDays = 60;

    // How many show at once, newest first: more is a notice board nobody reads.
    public const int MaxShown = 5;
}
