using FluentAssertions;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.RefreshSession;
using SmartDesk.Application.Features.Auth.Commands.UpdateProfile;
using SmartDesk.Domain.Constants;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// Changing your own name (PUT /api/auth/me): the same rules as signing up, only your own
// account, and your sessions go on with tokens that carry the new name.
public sealed class ProfileTests : IDisposable
{
    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_user_changes_their_name_and_keeps_their_session()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var signedIn = await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        var profile = await _host.SendAsync(ayse, Roles.Employee, new UpdateProfileCommand("  Ayşe Nur ", " Demir "));

        profile.FirstName.Should().Be("Ayşe Nur");
        profile.LastName.Should().Be("Demir");
        profile.Email.Should().Be("ayse@company.example");
        profile.Roles.Should().Equal(Roles.Employee);
        (await _host.FindAsync(ayse))!.DisplayName.Should().Be("Ayşe Nur Demir");

        var renewed = await _host.SendAnonymousAsync(new RefreshSessionCommand(signedIn.RefreshToken));
        var token = new JsonWebToken(renewed.Response.AccessToken);
        token.GetClaim(JwtRegisteredClaimNames.GivenName).Value.Should().Be("Ayşe Nur");
        token.GetClaim(JwtRegisteredClaimNames.FamilyName).Value.Should().Be("Demir");
    }

    [Fact]
    public async Task Only_the_caller_is_renamed()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);
        var mehmet = await _host.AddUserAsync("mehmet@company.example", Roles.Employee);
        var before = (await _host.FindAsync(mehmet))!.DisplayName;

        await _host.SendAsync(ayse, Roles.Employee, new UpdateProfileCommand("Ayşe", "Kaya"));

        (await _host.FindAsync(mehmet))!.DisplayName.Should().Be(before);
    }

    [Theory]
    [InlineData("", "Demir")]
    [InlineData("Ayşe", "   ")]
    [InlineData("Ayşe\nKaya", "Demir")]
    public async Task Names_follow_the_sign_up_rules(string firstName, string lastName)
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);

        var rename = () => _host.SendAsync(ayse, Roles.Employee, new UpdateProfileCommand(firstName, lastName));

        await rename.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_name_can_be_at_most_100_characters()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee);

        var tooLong = () => _host.SendAsync(ayse, Roles.Employee, new UpdateProfileCommand(new string('a', 101), "Demir"));
        await tooLong.Should().ThrowAsync<ValidationException>();

        var longest = await _host.SendAsync(ayse, Roles.Employee, new UpdateProfileCommand(new string('a', 100), "Demir"));
        longest.FirstName.Should().HaveLength(100);
    }
}
