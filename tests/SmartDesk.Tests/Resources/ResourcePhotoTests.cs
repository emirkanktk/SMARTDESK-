using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Resources.Commands.RemoveResourcePhoto;
using SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetResourcePhoto;
using SmartDesk.Application.Features.Resources.Queries.GetResources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

// Resource photos (ADR-0027): what counts as a photo, who may set and see one, and how the
// resource lists tell a new one from a cached one.
public sealed class ResourcePhotoTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Zeynep = Guid.NewGuid();
    private static readonly FakeCurrentUser Admin = TestAccess.Admin;
    private static readonly FakeCurrentUser ZeynepManager = new(Zeynep, Roles.Manager) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Selin = new(Guid.NewGuid(), Roles.Employee) { TeamId = Guid.NewGuid() };

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _time = new(Now);
    private Resource _shared = null!;
    private Resource _zeyneps = null!;

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync()
    {
        _shared = await _database.AddResourceAsync("Board Room");
        _zeyneps = await _database.AddResourceAsync("Meeting Room B");
        await using var context = _database.CreateContext();
        context.ResourceTeams.Add(new ResourceTeam { ResourceId = _zeyneps.Id, TeamId = Zeynep });
        await context.SaveChangesAsync();
    }

    private async Task<ResourceDto> SetAsync(FakeCurrentUser user, Resource resource, byte[] data)
    {
        await using var context = _database.CreateContext();
        return await new SetResourcePhotoCommandHandler(context, user, TestAccess.Reader(), _time)
            .Handle(new SetResourcePhotoCommand(resource.Id, data), CancellationToken.None);
    }

    private async Task RemoveAsync(FakeCurrentUser user, Resource resource)
    {
        await using var context = _database.CreateContext();
        await new RemoveResourcePhotoCommandHandler(context, user)
            .Handle(new RemoveResourcePhotoCommand(resource.Id), CancellationToken.None);
    }

    private async Task<ResourcePhotoFile> GetAsync(FakeCurrentUser user, Resource resource)
    {
        await using var context = _database.CreateContext();
        return await new GetResourcePhotoQueryHandler(context, user)
            .Handle(new GetResourcePhotoQuery(resource.Id), CancellationToken.None);
    }

    private static IReadOnlyList<string> Errors(byte[] data) =>
        new SetResourcePhotoCommandValidator()
            .Validate(new SetResourcePhotoCommand(Guid.NewGuid(), data))
            .Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList();

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public void JpegPngAndWebp_AreRecognised_ByTheirFirstBytes(string kind, string contentType)
    {
        var data = kind switch { "jpeg" => Jpeg, "png" => Png, _ => Webp };

        ResourcePhotoRules.DetectContentType(data).Should().Be(contentType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<!DOCTYPE html><html><body>Hi</body></html>")]
    [InlineData("GIF89a....")]
    [InlineData("RIFF....WAVEfmt ")]
    [InlineData("")]
    public void AnythingElse_IsNotAPhoto_WhateverItsName(string content)
    {
        ResourcePhotoRules.DetectContentType(System.Text.Encoding.UTF8.GetBytes(content)).Should().BeNull();
    }

    [Fact]
    public void TheValidator_ExplainsAMissingOversizedOrWrongFile_OnThePhotoField()
    {
        Errors([]).Should().Equal("Photo: Choose a photo to upload.");
        Errors([.. Png, .. new byte[ResourcePhotoRules.MaxBytes]]).Should().Equal("Photo: The photo can be at most 2 MB.");
        Errors("<svg/>"u8.ToArray()).Should().Equal("Photo: The photo must be a JPEG, PNG or WebP image.");
        Errors([.. Png, .. new byte[ResourcePhotoRules.MaxBytes - Png.Length]]).Should().BeEmpty("exactly 2 MB is fine");
    }

    [Fact]
    public void TheValidator_SpeaksTurkishToo()
    {
        using var _ = UiCulture.Use("tr-TR");

        Errors("<svg/>"u8.ToArray()).Should().Equal("Photo: Fotoğraf JPEG, PNG ya da WebP biçiminde olmalı.");
    }

    [Fact]
    public async Task AnAdmin_SetsAPhoto_AndItsTypeComesFromTheBytes()
    {
        await SeedAsync();

        var resource = await SetAsync(Admin, _shared, Png);

        resource.PhotoUpdatedAt.Should().Be(Now);
        var photo = await GetAsync(Ayse, _shared);
        photo.ContentType.Should().Be("image/png");
        photo.Data.Should().Equal(Png);
        photo.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task ANewPhoto_ReplacesTheOldOne_AndMovesTheTime()
    {
        await SeedAsync();
        await SetAsync(Admin, _shared, Png);
        _time.Advance(TimeSpan.FromMinutes(5));

        var resource = await SetAsync(Admin, _shared, Jpeg);

        resource.PhotoUpdatedAt.Should().Be(Now.AddMinutes(5));
        (await GetAsync(Admin, _shared)).ContentType.Should().Be("image/jpeg");
        await using var context = _database.CreateContext();
        (await context.ResourcePhotos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AManager_ChangesTheirTeamsPhoto_ButNotASharedOnes()
    {
        await SeedAsync();

        (await SetAsync(ZeynepManager, _zeyneps, Webp)).PhotoUpdatedAt.Should().Be(Now);

        var shared = () => SetAsync(ZeynepManager, _shared, Webp);
        await shared.Should().ThrowAsync<ForbiddenAccessException>();
        var remove = () => RemoveAsync(ZeynepManager, _shared);
        await remove.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task AnotherTeamsResource_IsNotFound_ForItsPhotoEither()
    {
        await SeedAsync();
        await SetAsync(Admin, _zeyneps, Png);

        var see = () => GetAsync(Selin, _zeyneps);
        (await see.Should().ThrowAsync<NotFoundException>()).WithMessage("Resource*");
        var set = () => SetAsync(Selin, _zeyneps, Png);
        await set.Should().ThrowAsync<NotFoundException>();
        (await GetAsync(Ayse, _zeyneps)).Data.Should().Equal(Png);
    }

    [Fact]
    public async Task WithoutAPhoto_ThereIsNothingToFetch()
    {
        await SeedAsync();

        var see = () => GetAsync(Ayse, _shared);

        (await see.Should().ThrowAsync<NotFoundException>()).WithMessage($"Photo '{_shared.Id}' was not found.");
    }

    [Fact]
    public async Task RemovingAPhoto_BringsBackTheTypesPicture_AndRemovingNoneIsFine()
    {
        await SeedAsync();
        await SetAsync(Admin, _shared, Png);

        await RemoveAsync(Admin, _shared);
        await RemoveAsync(Admin, _shared);

        var see = () => GetAsync(Admin, _shared);
        await see.Should().ThrowAsync<NotFoundException>();
        await using var context = _database.CreateContext();
        (await context.ResourcePhotos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TheResourceList_SaysWhenEachPhotoChanged_AndNullWithoutOne()
    {
        await SeedAsync();
        await SetAsync(Admin, _zeyneps, Png);
        await using var context = _database.CreateContext();

        var resources = await new GetResourcesQueryHandler(context, Admin, TestAccess.Reader())
            .Handle(new GetResourcesQuery(), CancellationToken.None);

        resources.Single(r => r.Id == _zeyneps.Id).PhotoUpdatedAt.Should().Be(Now);
        resources.Single(r => r.Id == _shared.Id).PhotoUpdatedAt.Should().BeNull();
    }
}
