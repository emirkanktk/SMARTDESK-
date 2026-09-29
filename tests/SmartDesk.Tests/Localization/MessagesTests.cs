using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Localization;

// The message catalogue itself (ADR-0023): complete in both languages, and English exactly as
// the API answered before it could speak Turkish.
public sealed partial class MessagesTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static Dictionary<string, string> Catalogue(string culture) =>
        Messages.ResourceManager
            .GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void Every_message_has_a_Turkish_translation_with_the_same_placeholders()
    {
        var english = Catalogue("");
        var turkish = Catalogue("tr");

        turkish.Keys.Should().BeEquivalentTo(english.Keys);
        foreach (var (key, text) in english)
        {
            Placeholders(turkish[key]).Should().BeEquivalentTo(Placeholders(text), $"'{key}' must fill in the same values");
            turkish[key].Should().NotBe(text, $"'{key}' should be translated").And.NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void English_field_names_are_the_ones_FluentValidation_would_show_anyway()
    {
        foreach (var (key, name) in Catalogue("").Where(e => e.Key.StartsWith("Property_", StringComparison.Ordinal)))
        {
            var member = key["Property_".Length..];
            name.Should().Be(Regex.Replace(member, "(?<=[a-z])(?=[A-Z])", " "), "English messages mustn't change");
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Identitys_password_rules_read_in_the_requests_language(string culture)
    {
        var ours = new LocalizedIdentityErrorDescriber();
        var identity = new IdentityErrorDescriber();
        using var language = UiCulture.Use(culture);

        IdentityError[] mine =
        [
            ours.PasswordTooShort(8), ours.PasswordRequiresDigit(), ours.PasswordRequiresLower(),
            ours.PasswordRequiresUpper(), ours.PasswordRequiresNonAlphanumeric(), ours.PasswordRequiresUniqueChars(3),
        ];
        IdentityError[] theirs =
        [
            identity.PasswordTooShort(8), identity.PasswordRequiresDigit(), identity.PasswordRequiresLower(),
            identity.PasswordRequiresUpper(), identity.PasswordRequiresNonAlphanumeric(), identity.PasswordRequiresUniqueChars(3),
        ];

        mine.Select(e => e.Code).Should().Equal(theirs.Select(e => e.Code));
        if (culture == "en-US")
        {
            mine.Select(e => e.Description).Should().Equal(theirs.Select(e => e.Description), "English is Identity's own wording");
        }
        else
        {
            mine[0].Description.Should().Be("Parola en az 8 karakter olmalı.");
            mine[5].Description.Should().Be("Parolada en az 3 farklı karakter kullanılmalı.");
        }
    }

    [Fact]
    public async Task Validation_messages_and_field_names_follow_the_language()
    {
        await using var context = _database.CreateContext();
        var validator = new CreateResourceCommandValidator(context);
        var blank = new CreateResourceCommand("", Guid.NewGuid(), 5, "Parking", null, null);

        var english = await validator.ValidateAsync(blank);
        english.Errors.Should().Contain(e => e.PropertyName == "Name" && e.ErrorMessage == "'Name' must not be empty.");
        english.Errors.Should().Contain(e => e.PropertyName == "ResourceTypeId" && e.ErrorMessage == "Resource type does not exist.");

        using (UiCulture.Use("tr-TR"))
        {
            var turkish = await validator.ValidateAsync(blank);
            turkish.Errors.Should().Contain(e => e.PropertyName == "Name" && e.ErrorMessage.Contains("'Ad'") && !e.ErrorMessage.Contains("must"));
            turkish.Errors.Should().Contain(e => e.PropertyName == "ResourceTypeId" && e.ErrorMessage == "Böyle bir kaynak türü yok.");
        }
    }

    [Fact]
    public void A_rule_built_before_the_request_still_answers_in_its_language()
    {
        // Validators are built once and reused, so their messages must be looked up when they fail.
        var validator = new CreateReservationCommandValidator(TimeProvider.System);
        var backwards = new CreateReservationCommand(Guid.NewGuid(), DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(1), null);

        using var turkish = UiCulture.Use("tr");

        validator.Validate(backwards).Errors.Should()
            .Contain(e => e.PropertyName == "EndDateTime" && e.ErrorMessage == "Bitiş zamanı başlangıç zamanından sonra olmalı.");
    }

    [Fact]
    public void Not_found_names_the_thing_in_either_language()
    {
        new NotFoundException("Resource", "r-1").Message.Should().Be("Resource 'r-1' was not found.");
        new NotFoundException("Widget", "w-1").Message.Should().Be("Widget 'w-1' was not found.", "an unknown name stays as it is");

        using var turkish = UiCulture.Use("tr");
        new NotFoundException("Resource", "r-1").Message.Should().Be("Kaynak bulunamadı: 'r-1'.");
        new ForbiddenAccessException().Message.Should().Be("Bu işlemi yapma yetkiniz yok.");
        new UnauthorizedException().Message.Should().Be("Oturum açmanız gerekiyor.");
    }

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("tr-TR", "tr")]
    [InlineData("TR", "tr")]
    [InlineData("en-GB", "en")]
    [InlineData("de", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_the_languages_we_speak_are_recognised(string? given, string? expected) =>
        Languages.Normalize(given).Should().Be(expected);

    [Fact]
    public void Someone_without_a_saved_language_is_written_to_in_the_requests()
    {
        Languages.CultureFor("tr").Name.Should().Be("tr");
        Languages.CultureFor(null).Name.Should().Be("en");
        Languages.CultureFor("de").Name.Should().Be("en");

        using var turkish = UiCulture.Use("tr-TR");
        Languages.Current.Should().Be("tr");
        Languages.CultureFor(null).Name.Should().Be("tr");
        Languages.CultureFor("en").Name.Should().Be("en", "a saved language wins over the request's");
    }

    [Fact]
    public void A_status_inside_a_message_is_translated()
    {
        Languages.Describe(ResourceStatus.Maintenance).Should().Be("Maintenance");

        using var turkish = UiCulture.Use("tr");
        Languages.Describe(ResourceStatus.Maintenance).Should().Be("Bakımda");
        Languages.Describe(ResourceStatus.Inactive).Should().Be("Etkin değil");
    }
}
