using System.Data.Common;
using FluentAssertions;
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
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// Sign-in sessions (ADR-0026): renewed with a refresh token that changes every time, ended by
// signing out, a password change, or the account being switched off.
public sealed class SessionTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _userId = Guid.NewGuid();

    public SessionTests()
    {
        using var context = _database.CreateContext();
        context.Users.Add(new ApplicationUser { Id = _userId, UserName = "ayse@company.example", Email = "ayse@company.example" });
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
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);
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
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);
        var overtaken = SessionsOvertakenBy(() => Sessions().EndAsync(started.RefreshToken!, CancellationToken.None));

        (await overtaken.RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Each_renewal_replaces_the_refresh_token()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);

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
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);
        await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(20));

        (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().NotBeNull("the token it replaced still works for a moment");
    }

    [Fact]
    public async Task A_replaced_token_used_later_is_a_copy_and_ends_the_session()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);
        var renewed = (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None))!;

        _clock.Advance(TimeSpan.FromSeconds(31));

        (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None)).Should().BeNull();
        (await Sessions().RenewAsync(renewed.Session.RefreshToken!, CancellationToken.None)).Should().BeNull("whoever holds the newer token is signed out too");
    }

    [Fact]
    public async Task A_session_left_unused_for_14_days_is_over()
    {
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(13));
        var renewed = (await Sessions().RenewAsync(started.RefreshToken!, CancellationToken.None))!;
        renewed.Session.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddDays(14), "using it keeps it going");

        _clock.Advance(TimeSpan.FromDays(14));
        (await Sessions().RenewAsync(renewed.Session.RefreshToken!, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task No_session_lasts_more_than_30_days()
    {
        var token = (await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None)).RefreshToken!;

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
        var started = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);

        await using var context = _database.CreateContext();
        var stored = context.UserSessions.Single();
        stored.TokenHash.Should().Be(UserSessions.Hash(started.RefreshToken!)).And.NotContain(started.RefreshToken);
        started.RefreshToken!.Length.Should().BeGreaterThanOrEqualTo(43, "256 random bits");
    }

    [Fact]
    public async Task Signing_out_ends_just_that_session()
    {
        var laptop = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);
        var phone = await Sessions().StartAsync(_userId, "stamp-1", CancellationToken.None);

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
}
