using System.Globalization;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Announcements.Commands.CreateAnnouncement;
using SmartDesk.Application.Features.Announcements.Commands.DeleteAnnouncement;
using SmartDesk.Application.Features.Announcements.Common;
using SmartDesk.Application.Features.Announcements.Queries.GetAnnouncements;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Announcements;

// Announcements (ADR-0065): an Admin posts one, it shows on everyone's dashboard until it ends,
// and goes to the phones with notifications on when asked.
public sealed class AnnouncementTests : IDisposable
{
    private static readonly DateTime NowUtc = new(2026, 11, 27, 7, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(NowUtc));
    private readonly Guid _zeynep = Guid.NewGuid();
    private readonly Guid _ayse = Guid.NewGuid();
    private readonly Guid _mehmet = Guid.NewGuid();
    private readonly CapturingPushSender _push = new();
    private readonly FakeIdentityService _names;

    public AnnouncementTests() =>
        _names = new FakeIdentityService(new Dictionary<Guid, string> { [_zeynep] = "Zeynep Kaya" });

    public void Dispose() => _database.Dispose();

    private async Task<AnnouncementDto> PostAsync(
        string title = "Breakfast on Friday",
        string? body = null,
        AnnouncementTone tone = AnnouncementTone.Good,
        DateTime? endsAt = null,
        bool notify = false)
    {
        await using var context = _database.CreateContext();
        return await new CreateAnnouncementCommandHandler(
                context, new FakeCurrentUser(_zeynep, Roles.Admin), _names, _push, _clock)
            .Handle(new CreateAnnouncementCommand(title, body, tone, endsAt ?? NowUtc.AddDays(3), notify), CancellationToken.None);
    }

    private async Task<IReadOnlyList<AnnouncementDto>> CurrentAsync()
    {
        await using var context = _database.CreateContext();
        return await new GetAnnouncementsQueryHandler(context, _names, _clock)
            .Handle(new GetCurrentAnnouncementsQuery(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<AnnouncementDto>> AllAsync()
    {
        await using var context = _database.CreateContext();
        return await new GetAnnouncementsQueryHandler(context, _names, _clock)
            .Handle(new GetAllAnnouncementsQuery(), CancellationToken.None);
    }

    private async Task AddDevicesAsync(params Guid[] owners)
    {
        await using var context = _database.CreateContext();
        foreach (var owner in owners)
        {
            context.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = owner,
                Endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}",
                P256dh = "key",
                Auth = "auth",
                SavedAt = NowUtc,
            });
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task An_admin_posts_one_and_it_shows_from_now_until_its_end_with_its_author()
    {
        var posted = await PostAsync("  Breakfast on Friday  ", "  Croissants on the 5th floor.\nFrom 9:00.  ", AnnouncementTone.Good);

        posted.Should().Match<AnnouncementDto>(a =>
            a.Title == "Breakfast on Friday"
            && a.Body == "Croissants on the 5th floor.\nFrom 9:00."
            && a.Tone == AnnouncementTone.Good
            && a.CreatedAt == NowUtc
            && a.EndsAt == NowUtc.AddDays(3)
            && a.AuthorId == _zeynep
            && a.AuthorName == "Zeynep Kaya");
        (await CurrentAsync()).Should().ContainSingle().Which.Id.Should().Be(posted.Id);

        _clock.Advance(TimeSpan.FromDays(3));
        (await CurrentAsync()).Should().BeEmpty("it has ended");
        (await AllAsync()).Should().ContainSingle("the Admins still see it");
    }

    [Fact]
    public async Task It_goes_to_everyone_elses_phones_only_when_asked()
    {
        await AddDevicesAsync(_ayse, _ayse, _mehmet, _zeynep);

        await PostAsync("Quiet one");
        _push.Queued.Should().BeEmpty();

        var loud = await PostAsync("Lifts out of order", tone: AnnouncementTone.Warning, notify: true);

        var notice = _push.Queued.Should().ContainSingle().Subject;
        notice.Topic.Should().Be(PushTopic.Announcement);
        notice.RecipientIds.Should().BeEquivalentTo([_ayse, _mehmet], "its author knows already");
        notice.Announcement!.Id.Should().Be(loud.Id);
    }

    [Fact]
    public async Task Newest_first_and_no_more_than_fit()
    {
        for (var i = 0; i < AnnouncementRules.MaxShown + 2; i++)
        {
            await PostAsync($"News {i}");
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var current = await CurrentAsync();

        current.Should().HaveCount(AnnouncementRules.MaxShown);
        current[0].Title.Should().Be($"News {AnnouncementRules.MaxShown + 1}");
        current.Should().BeInDescendingOrder(a => a.CreatedAt);
    }

    [Fact]
    public async Task One_that_would_end_before_now_or_too_late_is_refused()
    {
        var past = () => PostAsync(endsAt: NowUtc.AddMinutes(-1));
        (await past.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "EndsAt" && e.ErrorMessage == "The end has to be later than now.");

        var tooLong = () => PostAsync(endsAt: NowUtc.AddDays(AnnouncementRules.MaxDays).AddMinutes(1));
        (await tooLong.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage == $"An announcement shows for {AnnouncementRules.MaxDays} days at most.");

        (await PostAsync(endsAt: NowUtc.AddDays(AnnouncementRules.MaxDays))).EndsAt.Should().Be(NowUtc.AddDays(AnnouncementRules.MaxDays));
    }

    [Theory]
    [InlineData("Breakfast", "Line one\nline two", AnnouncementTone.Info, true)]
    [InlineData("", null, AnnouncementTone.Info, false)]
    [InlineData("Two\nlines", null, AnnouncementTone.Info, false)]
    [InlineData("Breakfast", "A bell\u0007", AnnouncementTone.Info, false)]
    [InlineData("Breakfast", null, (AnnouncementTone)42, false)]
    public void A_title_is_one_line_and_the_text_may_have_several(string title, string? body, AnnouncementTone tone, bool valid) =>
        new CreateAnnouncementCommandValidator()
            .Validate(new CreateAnnouncementCommand(title, body, tone, NowUtc.AddDays(1), false))
            .IsValid.Should().Be(valid);

    [Fact]
    public async Task An_announcement_taken_down_is_gone()
    {
        var posted = await PostAsync();

        await using (var context = _database.CreateContext())
        {
            await new DeleteAnnouncementCommandHandler(context).Handle(new DeleteAnnouncementCommand(posted.Id), CancellationToken.None);
        }

        (await AllAsync()).Should().BeEmpty();
        await using var again = _database.CreateContext();
        var twice = () => new DeleteAnnouncementCommandHandler(again).Handle(new DeleteAnnouncementCommand(posted.Id), CancellationToken.None);
        await twice.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void On_a_phone_it_reads_as_an_announcement_and_opens_the_dashboard()
    {
        var announcement = new AnnouncementDto(
            Guid.NewGuid(), "Breakfast on Friday", "Croissants on the 5th floor.\nFrom 9:00.", AnnouncementTone.Good, NowUtc, NowUtc.AddDays(3), _zeynep);
        PushContent Notice(AnnouncementDto a, string culture) => PushTexts.Compose(
            new PushNotice(PushTopic.Announcement, [_ayse]) { Announcement = a },
            CultureInfo.GetCultureInfo(culture),
            TimeZoneInfo.Utc,
            TimeSpan.FromMinutes(15));

        var turkish = Notice(announcement, "tr");
        var english = Notice(announcement, "en");

        turkish.Title.Should().Be("Duyuru: Breakfast on Friday");
        english.Title.Should().Be("Announcement: Breakfast on Friday");
        english.Body.Should().Be("Croissants on the 5th floor. From 9:00.");
        english.Url.Should().Be("/dashboard");
        english.Tag.Should().Be($"announcement-{announcement.Id}");
        Notice(announcement with { Body = null }, "tr").Body.Should().Be("Okumak için SmartDesk'i açın.");
        Notice(announcement with { Body = new string('x', 300) }, "en").Body.Should().HaveLength(PushTexts.MaxDetailLength);
    }
}
