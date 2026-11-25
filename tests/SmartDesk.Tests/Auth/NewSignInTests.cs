using System.Data.Common;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Common.Security;
using SmartDesk.Application.Features.Auth.Commands.ChangePassword;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.Logout;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// The browsers each account signed in on (ADR-0062): known by a token in a cookie of their own,
// kept only as a hash, and forgotten when that cookie would have expired.
public sealed class KnownDeviceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _ayse = Guid.NewGuid();
    private readonly Guid _mehmet = Guid.NewGuid();

    public KnownDeviceTests()
    {
        using var context = _database.CreateContext();
        foreach (var (id, email) in new[] { (_ayse, "ayse@company.example"), (_mehmet, "mehmet@company.example") })
        {
            context.Users.Add(new ApplicationUser { Id = id, UserName = email, Email = email });
        }

        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private KnownDevices Devices(SmartDeskDbContext? context = null) => new(context ?? _database.CreateContext(), _clock);

    private Task<DeviceCheck> SignInAsync(Guid userId, string? token) =>
        Devices().SignedInAsync(userId, token, CancellationToken.None);

    [Fact]
    public async Task The_first_browser_an_account_signs_in_on_is_noted_and_tells_nobody()
    {
        var check = await SignInAsync(_ayse, null);

        check.Familiarity.Should().Be(DeviceFamiliarity.First, "there's no other browser to tell it from");
        check.Token.Should().HaveLength(43, "256 random bits, as base64url");
    }

    [Fact]
    public async Task The_same_browser_again_is_known_and_keeps_its_token()
    {
        var laptop = await SignInAsync(_ayse, null);
        _clock.Advance(TimeSpan.FromDays(3));

        var again = await SignInAsync(_ayse, laptop.Token);

        again.Should().Be(new DeviceCheck(DeviceFamiliarity.Known, laptop.Token));
        await using var context = _database.CreateContext();
        var row = await context.KnownDevices.SingleAsync();
        row.LastSeenAt.Should().Be(_clock.GetUtcNow().UtcDateTime);
        row.FirstSeenAt.Should().Be(row.LastSeenAt.AddDays(-3));
    }

    [Fact]
    public async Task Another_browser_is_new_and_gets_a_token_of_its_own()
    {
        var laptop = await SignInAsync(_ayse, null);

        var phone = await SignInAsync(_ayse, null);

        phone.Familiarity.Should().Be(DeviceFamiliarity.New);
        phone.Token.Should().NotBe(laptop.Token);
        (await SignInAsync(_ayse, phone.Token)).Familiarity.Should().Be(DeviceFamiliarity.Known);
    }

    [Fact]
    public async Task A_browser_someone_else_signed_in_on_keeps_its_token_and_is_new_to_this_account()
    {
        await SignInAsync(_ayse, null);
        var shared = await SignInAsync(_mehmet, null);

        var ayseThere = await SignInAsync(_ayse, shared.Token);

        ayseThere.Should().Be(new DeviceCheck(DeviceFamiliarity.New, shared.Token), "one cookie for the browser, whoever signs in");
        (await SignInAsync(_mehmet, shared.Token)).Familiarity.Should().Be(DeviceFamiliarity.Known);
        (await SignInAsync(_ayse, shared.Token)).Familiarity.Should().Be(DeviceFamiliarity.Known);
    }

    [Fact]
    public async Task A_token_that_isnt_one_of_ours_is_replaced()
    {
        await SignInAsync(_ayse, null);

        var forged = await SignInAsync(_ayse, "not-a-token");

        forged.Familiarity.Should().Be(DeviceFamiliarity.New);
        forged.Token.Should().HaveLength(43).And.NotBe("not-a-token");
    }

    [Fact]
    public async Task Tokens_are_stored_only_as_hashes()
    {
        var check = await SignInAsync(_ayse, null);

        await using var context = _database.CreateContext();
        var stored = (await context.KnownDevices.SingleAsync()).TokenHash;
        stored.Should().HaveLength(64).And.NotContain(check.Token);
    }

    [Fact]
    public async Task A_browser_unused_for_400_days_is_forgotten_and_new_again()
    {
        var laptop = await SignInAsync(_ayse, null);
        _clock.Advance(TimeSpan.FromDays(300));
        var phone = await SignInAsync(_ayse, null);
        _clock.Advance(TimeSpan.FromDays(101));

        // Its cookie has expired by now; were it still there, it's no longer one the account knows.
        (await SignInAsync(_ayse, laptop.Token)).Familiarity.Should().Be(DeviceFamiliarity.New);
        (await SignInAsync(_ayse, phone.Token)).Familiarity.Should().Be(DeviceFamiliarity.Known);
    }

    [Fact]
    public async Task Of_two_sign_ins_on_one_new_browser_at_once_the_later_finds_it_known()
    {
        await SignInAsync(_ayse, null);
        var shared = await SignInAsync(_mehmet, null);
        DeviceCheck? earlier = null;

        // The earlier one adds the browser between this one looking for it and adding it too.
        await using var context = _database.CreateContext(new JustBeforeTheInsert(async () => earlier = await SignInAsync(_ayse, shared.Token)));
        var later = await Devices(context).SignedInAsync(_ayse, shared.Token, CancellationToken.None);

        earlier!.Familiarity.Should().Be(DeviceFamiliarity.New, "this one tells her");
        later.Should().Be(new DeviceCheck(DeviceFamiliarity.Known, shared.Token));
        context.ChangeTracker.Entries().Should().BeEmpty("the session that comes next mustn't add the row again");
        (await context.KnownDevices.CountAsync(d => d.UserId == _ayse)).Should().Be(2);
    }

    private sealed class JustBeforeTheInsert(Func<Task> first) : DbCommandInterceptor
    {
        private bool _ran;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RunFirstAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RunFirstAsync(command);
            return result;
        }

        private async Task RunFirstAsync(DbCommand command)
        {
            if (!_ran && command.CommandText.Contains("INSERT INTO \"KnownDevices\"", StringComparison.Ordinal))
            {
                _ran = true;
                await first();
            }
        }
    }
}

// The same through signing in (ADR-0062): its owner hears of a sign-in on a browser their account
// hasn't been used on, by email and on their devices, and of no other.
public sealed class NewSignInNoticeTests : IDisposable
{
    private const string LaptopBrowser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0";
    private const string PhoneBrowser = "Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1";

    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // Signs in on a browser as it would: with the cookie it has, if any.
    private async Task<SignedIn> SignInOnAsync(string browser, string? deviceToken, string email = "ayse@company.example")
    {
        _host.UserAgent = browser;
        _host.DeviceToken = deviceToken;
        try
        {
            return await _host.SendAnonymousAsync(new LoginCommand(email, AccountsTestHost.Password));
        }
        finally
        {
            _host.UserAgent = null;
            _host.DeviceToken = null;
        }
    }

    private static Guid SessionOf(SignedIn signedIn)
    {
        var payload = signedIn.Response.AccessToken.Split('.')[1];
        using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(payload)));
        return Guid.Parse(claims.RootElement.GetProperty(SmartDeskClaims.Session).GetString()!);
    }

    [Fact]
    public async Task A_sign_in_on_a_browser_the_account_hasnt_used_is_told_by_email_and_on_its_devices()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee, firstName: "Ayşe", language: "tr");
        using var turkish = UiCulture.Use("tr-TR");
        var laptop = await SignInOnAsync(LaptopBrowser, null);
        _host.Emails.Sent.Should().BeEmpty("her first browser has nothing to be told from");
        _host.Push.Queued.Should().BeEmpty();

        var phone = await SignInOnAsync(PhoneBrowser, null);

        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.To.Should().Be("ayse@company.example");
        email.Subject.Should().Be("SmartDesk hesabınıza yeni bir cihazdan giriş yapıldı");
        email.Text.Should().StartWith("Merhaba Ayşe,")
            .And.Contain("daha önce kullanılmadığı bir cihazdan giriş yapıldı: Safari · iPhone, ")
            .And.Contain(", parolayla.")
            .And.Contain("parolanızı değiştirin");
        CapturingEmailSender.LinkIn(email, AccountsTestHost.FrontendUrl).Should().Be($"{AccountsTestHost.FrontendUrl}/profile#sessions");

        var notice = _host.Push.Queued.Should().ContainSingle().Subject;
        notice.Topic.Should().Be(PushTopic.NewSignIn);
        notice.RecipientIds.Should().Equal(ayse);
        notice.SignIn.Should().BeEquivalentTo(new { Session = SessionOf(phone), UserAgent = PhoneBrowser, Method = SignInMethod.Password });
        laptop.DeviceToken.Should().NotBeNullOrEmpty();
        phone.DeviceToken.Should().NotBeNullOrEmpty().And.NotBe(laptop.DeviceToken);
    }

    [Fact]
    public async Task Signing_in_again_on_a_browser_it_knows_tells_nobody()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var first = await SignInOnAsync(LaptopBrowser, null);
        await SignInOnAsync(PhoneBrowser, null);
        _host.Emails.Sent.Clear();
        _host.Push.Queued.Clear();
        await _host.SendAnonymousAsync(new LogoutCommand(first.RefreshToken));

        var again = await SignInOnAsync(LaptopBrowser, first.DeviceToken);

        again.DeviceToken.Should().Be(first.DeviceToken, "the browser keeps the token it's known by");
        _host.Emails.Sent.Should().BeEmpty();
        _host.Push.Queued.Should().BeEmpty();

        // Nor does a password change, which starts a session on a browser already signed in.
        var changed = await _host.SendAsync(ayse, Roles.Employee, new ChangePasswordCommand(AccountsTestHost.Password, "Brand-New-Pass-7"));
        changed.DeviceToken.Should().BeNull("it leaves the browser's cookie as it is");
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_email_is_in_the_language_the_account_had_before_whoever_signed_in()
    {
        await _host.AddUserAsync("ayse@company.example", Roles.Employee, firstName: "Ayşe", language: "tr");
        using (UiCulture.Use("tr-TR"))
        {
            await SignInOnAsync(LaptopBrowser, null);
        }

        // Someone with her password, on an English browser of their own.
        await SignInOnAsync("curl/8.11.1", null);

        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("SmartDesk hesabınıza yeni bir cihazdan giriş yapıldı");
        email.Text.Should().Contain("giriş yapıldı: bilinmeyen bir tarayıcı, ");
    }

    [Fact]
    public async Task Each_account_has_its_own_browsers()
    {
        await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        await _host.AddUserAsync("mehmet@company.example", Roles.Employee);
        var hers = await SignInOnAsync(LaptopBrowser, null);

        // Mehmet's first sign-in, on her laptop: first for him, and nothing to do with her.
        var his = await SignInOnAsync(LaptopBrowser, hers.DeviceToken, "mehmet@company.example");

        his.DeviceToken.Should().Be(hers.DeviceToken);
        _host.Emails.Sent.Should().BeEmpty();
        (await SignInOnAsync(LaptopBrowser, hers.DeviceToken)).DeviceToken.Should().Be(hers.DeviceToken);
        _host.Emails.Sent.Should().BeEmpty();
    }
}

// What a browser is called in a notice (ADR-0062): as the app's list of devices calls it.
public sealed class DeviceNameTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/142.0.0.0 Mobile/15E148 Safari/604.1", "Chrome · iPhone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1", "Safari · iPad")]
    [InlineData("Mozilla/5.0 (Linux; Android 16; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (Linux; Android 15; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/28.0 Chrome/130.0.0.0 Safari/537.36", "Samsung Internet · Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 15.1; rv:145.0) Gecko/20100101 Firefox/145.0", "Firefox · Mac")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 OPR/125.0.0.0", "Opera · Linux")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 16433.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36", "Chrome · ChromeOS")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "Windows")]
    public void Names_the_browser_and_what_it_runs_on(string agent, string name) =>
        DeviceNames.Describe(agent).Should().Be(name);

    [Theory]
    [InlineData("curl/8.11.1")]
    [InlineData("")]
    [InlineData(null)]
    public void Says_nothing_it_does_not_know(string? agent) =>
        DeviceNames.Describe(agent).Should().BeNull();
}
