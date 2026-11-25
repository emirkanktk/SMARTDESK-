using System.Data.Common;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;
using SmartDesk.Application.Features.Auth.Commands.ChangePassword;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.Logout;
using SmartDesk.Application.Features.Auth.Commands.RefreshSession;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Features.Sessions.Commands.EndOtherSessions;
using SmartDesk.Application.Features.Sessions.Commands.EndSession;
using SmartDesk.Application.Features.Sessions.Queries.GetMySessions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Common.Security;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// Sign-in sessions (ADR-0026): renewed with a refresh token that changes every time, ended by
// signing out, a password change, or the account being switched off.
public sealed class SessionTests : IDisposable
{
    private static readonly SessionOrigin Origin = new(SignInMethod.Password, "Test browser");

    // The session stamp of the account's password as it is now; the list shows only sessions
    // begun under it (ADR-0061).
    private static readonly string Stamp = UserAccessChecker.SessionStampOf("security-1");

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _userId = Guid.NewGuid();

    public SessionTests()
    {
        using var context = _database.CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = _userId,
            UserName = "ayse@company.example",
            Email = "ayse@company.example",
            SecurityStamp = "security-1",
        });
        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private UserSessions Sessions(SmartDeskDbContext? context = null) => new(
        context ?? _database.CreateContext(),
        new UserAccessCache(new MemoryCache(new MemoryCacheOptions()), new NoRealtimeSessions()),
        Options.Create(new SessionOptions { IdleDays = 14, MaxDays = 30, ReuseGraceSeconds = 30 }),
        _clock);

    // Sessions whose next write happens only after `first` has run: as if `first` had happened
    // between reading the session and writing it.
    private UserSessions SessionsOvertakenBy(Func<Task> first) =>
        Sessions(_database.CreateContext(new JustBeforeTheUpdate(first)));

    private sealed class JustBeforeTheUpdate(Func<Task> first) : DbCommandInterceptor
    {
        private bool _ran;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_ran && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _ran = true;
                await first();
            }

            return result;
        }
    }

    [Fact]
    public async Task Of_two_renewals_with_one_token_at_the_same_moment_only_one_replaces_it()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);
        SessionRenewal? first = null;
        var overtaken = SessionsOvertakenBy(async () =>
            first = await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None));

        var second = await overtaken.RenewAsync(started.RefreshToken!, CancellationToken.None);

        first!.Session.RefreshToken.Should().NotBeNull();
        second.Should().NotBeNull("it's the same browser, signed in either way");
        second!.Session.SessionId.Should().Be(started.SessionId);
        second.Session.RefreshToken.Should().BeNull("the first answer brings the new token; a second one could arrive last");
        await using (var context = _database.CreateContext())
        {
            context.UserSessions.Single().TokenHash.Should().Be(UserSessions.Hash(first.Session.RefreshToken!));
        }

        _clock.Advance(TimeSpan.FromMinutes(15));
        (await Sessions().RenewAsync(first.Session.RefreshToken!, CancellationToken.None))
            .Should().NotBeNull("the token the browser keeps is the session's current one");
    }

    [Fact]
    public async Task A_renewal_overtaken_by_a_sign_out_gets_nothing()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);
        var overtaken = SessionsOvertakenBy(() => Sessions().EndAsync(started.RefreshToken!, CancellationToken.None));

        (await overtaken.RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Each_renewal_replaces_the_refresh_token()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);

        var renewed = await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None);

        renewed.Should().NotBeNull();
        renewed!.UserId.Should().Be(_userId);
        renewed.SessionStamp.Should().Be("stamp-1");
        renewed.Session.SessionId.Should().Be(started.SessionId, "it's the same session");
        renewed.Session.RefreshToken.Should().NotBe(started.RefreshToken);
        (await Sessions().RenewAsync(renewed.Session.RefreshToken!, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Two_tabs_renewing_at_once_both_get_through()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);
        await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(20));

        (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().NotBeNull("the token it replaced still works for a moment");
    }

    [Fact]
    public async Task A_replaced_token_used_later_is_a_copy_and_ends_the_session()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);
        var renewed = (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None))!;

        _clock.Advance(TimeSpan.FromSeconds(31));

        (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(renewed.Session.RefreshToken!, CancellationToken.None)).Should().BeNull("whoever holds the newer token is signed out too");
    }

    [Fact]
    public async Task A_session_left_unused_for_14_days_is_over()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(13));
        var renewed = (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None))!;
        renewed.Session.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddDays(14), "using it keeps it going");

        _clock.Advance(TimeSpan.FromDays(14));
        (await Sessions().RenewAsync(renewed.Session.RefreshToken!, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task No_session_lasts_more_than_30_days()
    {
        var token = (await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None)).RefreshToken!;

        for (var day = 0; day < 3; day++)
        {
            _clock.Advance(TimeSpan.FromDays(10));
            var renewed = await Sessions().RenewAsync(token, CancellationToken.None);
            if (day < 2)
            {
                token = renewed!.Session.RefreshToken!;
                renewed.Session.ExpiresAt.Should().BeOnOrBefore(new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc));
            }
            else
            {
                renewed.Should().BeNull("30 days have passed, however often it was used");
            }
        }
    }

    [Fact]
    public async Task Tokens_are_stored_only_as_hashes()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);

        await using var context = _database.CreateContext();
        var stored = context.UserSessions.Single();
        stored.TokenHash.Should().Be(UserSessions.Hash(started.RefreshToken!)).And.NotContain(started.RefreshToken);
        started.RefreshToken!.Length.Should().BeGreaterThanOrEqualTo(43, "256 random bits");
    }

    [Fact]
    public async Task Signing_out_ends_just_that_session()
    {
        var laptop = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);
        var phone = await Sessions().StartAsync(_userId, "stamp-1", Origin, CancellationToken.None);

        await Sessions().EndAsync(laptop.RefreshToken!, CancellationToken.None);

        (await Sessions().RenewAsync(laptop.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(phone.RefreshToken!, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_unknown_token_gets_nothing()
    {
        (await Sessions().RenewAsync("not-a-token", CancellationToken.None)).Should().BeNull();
        await Sessions().EndAsync("not-a-token", CancellationToken.None);
    }

    // ADR-0061: the user's list of signed-in devices.

    [Fact]
    public async Task A_session_notes_how_it_began_and_on_what_browser()
    {
        var phone = await Sessions().StartAsync(
            _userId, Stamp, new SessionOrigin(SignInMethod.Passkey, "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X)"), CancellationToken.None);

        var listed = (await Sessions().ListOpenAsync(_userId, phone.SessionId, CancellationToken.None)).Should().ContainSingle().Subject;

        listed.Should().Be(new SignInSessionDto(
            phone.SessionId,
            "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X)",
            SignInMethod.Passkey,
            _clock.GetUtcNow().UtcDateTime,
            _clock.GetUtcNow().UtcDateTime,
            Current: true));
    }

    [Fact]
    public async Task The_list_has_this_device_first_then_the_others_by_their_last_use()
    {
        var here = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var laptop = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var phone = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(2));
        await Sessions().RenewAsync(laptop.RefreshToken!, CancellationToken.None);

        var list = await Sessions().ListOpenAsync(_userId, here.SessionId, CancellationToken.None);

        list.Select(s => s.Id).Should().Equal(here.SessionId, laptop.SessionId, phone.SessionId);
        list.Select(s => s.Current).Should().Equal(true, false, false);
        list[1].LastActiveAt.Should().Be(_clock.GetUtcNow().UtcDateTime, "its last renewal");
        list[1].StartedAt.Should().Be(list[2].StartedAt);
    }

    [Fact]
    public async Task Only_sessions_that_could_still_be_renewed_are_listed()
    {
        var open = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var signedOut = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        await Sessions().EndAsync(signedOut.RefreshToken!, CancellationToken.None);
        // Begun under an older password: an Admin's temporary one ends it on its next renewal.
        await Sessions().StartAsync(_userId, UserAccessChecker.SessionStampOf("security-0"), Origin, CancellationToken.None);

        (await Sessions().ListOpenAsync(_userId, null, CancellationToken.None)).Select(s => s.Id).Should().Equal(open.SessionId);

        _clock.Advance(TimeSpan.FromDays(15));
        (await Sessions().ListOpenAsync(_userId, null, CancellationToken.None)).Should().BeEmpty("14 days unused is over");
    }

    [Fact]
    public async Task Ending_one_session_from_another_device_stops_just_that_one()
    {
        var laptop = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var phone = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);

        (await Sessions().EndAsync(_userId, phone.SessionId, CancellationToken.None)).Should().BeTrue();

        (await Sessions().RenewAsync(phone.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(laptop.RefreshToken!, CancellationToken.None)).Should().NotBeNull();
        (await Sessions().EndAsync(_userId, phone.SessionId, CancellationToken.None)).Should().BeFalse("it has ended already");
        (await Sessions().EndAsync(Guid.NewGuid(), laptop.SessionId, CancellationToken.None)).Should().BeFalse("it isn't theirs");
        (await Sessions().ListOpenAsync(_userId, null, CancellationToken.None)).Select(s => s.Id).Should().Equal(laptop.SessionId);
    }

    [Fact]
    public async Task Ending_the_others_keeps_this_one()
    {
        var here = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var laptop = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);
        var phone = await Sessions().StartAsync(_userId, Stamp, Origin, CancellationToken.None);

        (await Sessions().EndOthersAsync(_userId, here.SessionId, CancellationToken.None)).Should().Be(2);

        (await Sessions().RenewAsync(laptop.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(phone.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(here.RefreshToken!, CancellationToken.None)).Should().NotBeNull();
        (await Sessions().EndOthersAsync(_userId, here.SessionId, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task A_very_long_user_agent_is_cut()
    {
        var agent = new string('x', 600);
        var started = await Sessions().StartAsync(_userId, Stamp, new SessionOrigin(SignInMethod.Password, agent), CancellationToken.None);

        var listed = (await Sessions().ListOpenAsync(_userId, started.SessionId, CancellationToken.None)).Single();
        listed.UserAgent.Should().HaveLength(UserSession.MaxUserAgentLength);
    }
}

// The same through the handlers, on real Identity.
public sealed class SessionFlowTests : IDisposable
{
    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Renewing_gives_a_new_access_token_for_the_same_session()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var signedIn = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        var renewed = await _host.SendAnonymousAsync(new RefreshSessionCommand(signedIn.RefreshToken));

        renewed.Response.AccessToken.Should().NotBeNullOrEmpty();
        renewed.RefreshToken.Should().NotBe(signedIn.RefreshToken);
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().ContainSingle();
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_its_tokens()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var signedIn = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().ContainSingle();

        await _host.SendAnonymousAsync(new LogoutCommand(signedIn.RefreshToken));

        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().BeEmpty("the access token names a session that's over");
        var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(signedIn.RefreshToken));
        await renew.Should().ThrowAsync<UnauthorizedException>();
        await _host.SendAnonymousAsync(new LogoutCommand(signedIn.RefreshToken)); // twice is fine
        await _host.SendAnonymousAsync(new LogoutCommand(null));
    }

    [Fact]
    public async Task A_password_change_ends_every_session_and_starts_one_here()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var laptop = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));
        var phone = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        var changed = await _host.SendAsync(ayse, Roles.Employee, new ChangePasswordCommand(AccountsTestHost.Password, "Brand-New-Pass-7"));

        // Ended at once, not on their next renewal: their access tokens stop working too.
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().ContainSingle();
        foreach (var old in new[] { laptop, phone })
        {
            var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(old.RefreshToken));
            await renew.Should().ThrowAsync<UnauthorizedException>();
        }

        (await _host.SendAnonymousAsync(new RefreshSessionCommand(changed.RefreshToken))).Response.AccessToken.Should().NotBeNullOrEmpty();
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().ContainSingle();
    }

    [Fact]
    public async Task A_temporary_password_set_by_someone_else_ends_the_sessions()
    {
        // Nothing ends them explicitly here: the renewal sees the password changed since the
        // session began (its stamp) and ends it.
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var signedIn = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        await _host.SendAsync(admin, Roles.Admin, new SetTemporaryPasswordCommand("Temp-Pass-2026") { Id = ayse });

        var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(signedIn.RefreshToken));
        await renew.Should().ThrowAsync<UnauthorizedException>();
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().BeEmpty("the stale session was ended on the way");
    }

    [Fact]
    public async Task A_switched_off_account_cant_renew()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var signedIn = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        await _host.SendAsync(admin, Roles.Admin, new SetAccountStatusCommand(ayse, Enabled: false));

        var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(signedIn.RefreshToken));
        await renew.Should().ThrowAsync<UnauthorizedException>().WithMessage("Your session has ended. Sign in again.");
    }

    [Fact]
    public async Task Without_a_cookie_there_is_nothing_to_renew()
    {
        var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(null));

        await renew.Should().ThrowAsync<UnauthorizedException>();
    }

    // ADR-0061: the devices a user is signed in on, and signing one out from another.

    private const string LaptopBrowser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/142.0";
    private const string PhoneBrowser = "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) Version/26.1 Mobile/15E148 Safari/604.1";

    private async Task<SignedIn> SignInOnAsync(string browser, string email = "ayse@company.example")
    {
        _host.UserAgent = browser;
        try
        {
            return await _host.SendAnonymousAsync(new LoginCommand(email, AccountsTestHost.Password));
        }
        finally
        {
            _host.UserAgent = null;
        }
    }

    // The session its access token names, as JwtBearerSetup reads it.
    private static Guid SessionOf(SignedIn signedIn)
    {
        var payload = signedIn.Response.AccessToken.Split('.')[1];
        using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(payload)));
        return Guid.Parse(claims.RootElement.GetProperty(SmartDeskClaims.Session).GetString()!);
    }

    [Fact]
    public async Task Each_sign_in_is_a_device_in_the_list_and_one_signs_another_out()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var laptop = await SignInOnAsync(LaptopBrowser);
        var phone = await SignInOnAsync(PhoneBrowser);

        var list = await _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(laptop), new GetMySessionsQuery());
        list.Select(s => (s.UserAgent, s.Method, s.Current)).Should().Equal(
            (LaptopBrowser, SignInMethod.Password, true),
            (PhoneBrowser, SignInMethod.Password, false));

        _host.RealtimeSessions.Ended.Clear();
        await _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(laptop), new EndSessionCommand(SessionOf(phone)));

        // The phone's access token names a session that's over, and it can't be renewed.
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().Equal(SessionOf(laptop));
        var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(phone.RefreshToken));
        await renew.Should().ThrowAsync<UnauthorizedException>();
        _host.RealtimeSessions.Ended.Should().Contain(ayse, "the phone's open app is cut off at once, and finds out");
        (await _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(laptop), new GetMySessionsQuery()))
            .Should().ContainSingle().Which.Current.Should().BeTrue();
    }

    [Fact]
    public async Task Nobody_ends_a_session_of_someone_else()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        await _host.AddUserAsync("mehmet@company.example", Roles.Employee);
        var hers = await SignInOnAsync(LaptopBrowser);
        var his = await SignInOnAsync(PhoneBrowser, "mehmet@company.example");

        var end = () => _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(hers), new EndSessionCommand(SessionOf(his)));

        await end.Should().ThrowAsync<NotFoundException>();
        (await _host.SendAnonymousAsync(new RefreshSessionCommand(his.RefreshToken))).Response.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Signing_out_everywhere_else_keeps_this_device()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var here = await SignInOnAsync(LaptopBrowser);
        var phone = await SignInOnAsync(PhoneBrowser);
        var tablet = await SignInOnAsync(PhoneBrowser);

        var ended = await _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(here), new EndOtherSessionsCommand());

        ended.Ended.Should().Be(2);
        (await _host.GetAccessAsync(ayse))!.OpenSessions.Should().Equal(SessionOf(here));
        foreach (var other in new[] { phone, tablet })
        {
            var renew = () => _host.SendAnonymousAsync(new RefreshSessionCommand(other.RefreshToken));
            await renew.Should().ThrowAsync<UnauthorizedException>();
        }
    }

    [Fact]
    public async Task A_password_change_starts_this_device_again_as_a_password_sign_in()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var before = await SignInOnAsync(LaptopBrowser);

        _host.UserAgent = LaptopBrowser;
        var changed = await _host.SendInSessionAsync(
            ayse, Roles.Employee, SessionOf(before), new ChangePasswordCommand(AccountsTestHost.Password, "Brand-New-Pass-7"));
        _host.UserAgent = null;

        var list = await _host.SendInSessionAsync(ayse, Roles.Employee, SessionOf(changed), new GetMySessionsQuery());
        list.Should().ContainSingle().Which.Should().Match<SignInSessionDto>(s =>
            s.Current && s.UserAgent == LaptopBrowser && s.Method == SignInMethod.Password);
    }
}
