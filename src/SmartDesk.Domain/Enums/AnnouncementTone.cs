namespace SmartDesk.Domain.Enums;

// What kind of news an announcement is (ADR-0065), which sets its colour and its icon wherever it
// shows: the dashboard, a lobby screen, a phone.
public enum AnnouncementTone
{
    // Something to know: a visitor today, a new way of doing things.
    Info = 0,

    // Good news: new chairs on the 3rd floor, breakfast on Friday.
    Good = 1,

    // Something to look out for: a floor closed, the lifts out of order.
    Warning = 2,
}
