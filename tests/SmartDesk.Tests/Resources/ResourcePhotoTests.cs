using System.Buffers.Binary;
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

    private static readonly byte[] Jpeg = JpegOf(2, 1);
    private static readonly byte[] Png = PngOf(2, 1);
    private static readonly byte[] Webp = WebpOf("VP8 ", 2, 1);

    // The smallest headers that say what a picture is and how big: all the rules read, since
    // they never decode one.
    private static byte[] PngOf(uint width, uint height)
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. "IHDR"u8, .. new byte[17]];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    // Start of image, a JFIF APP0 segment, then a baseline start-of-frame with the size.
    private static byte[] JpegOf(ushort width, ushort height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
    ];

    // RIFF....WEBP and a first chunk of each kind: lossy, lossless or extended.
    private static byte[] WebpOf(string chunk, int width, int height)
    {
        byte[] data = [.. "RIFF"u8, 0x16, 0x00, 0x00, 0x00, .. "WEBP"u8, .. System.Text.Encoding.ASCII.GetBytes(chunk), 0x0A, 0x00, 0x00, 0x00, .. new byte[10]];
        switch (chunk)
        {
            case "VP8 ":
                data[23] = 0x9D;
                data[24] = 0x01;
                data[25] = 0x2A;
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)height);
                break;
            case "VP8L":
                data[20] = 0x2F;
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21), (uint)((width - 1) | ((height - 1) << 14)));
                break;
            default:
                data[24] = (byte)(width - 1);
                data[25] = (byte)((width - 1) >> 8);
                data[26] = (byte)((width - 1) >> 16);
                data[27] = (byte)(height - 1);
                data[28] = (byte)((height - 1) >> 8);
                data[29] = (byte)((height - 1) >> 16);
                break;
        }

        return data;
    }

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
        Errors(PngOf(5000, 10)).Should().Equal("Photo: Fotoğrafın eni ve boyu en fazla 4096 piksel olabilir.");
    }

    [Fact]
    public void TheSize_IsReadFromTheHeader_OfEveryKind()
    {
        ResourcePhotoRules.ReadSize(PngOf(1600, 1200)).Should().Be((1600, 1200));
        ResourcePhotoRules.ReadSize(JpegOf(1600, 900)).Should().Be((1600, 900));
        ResourcePhotoRules.ReadSize(WebpOf("VP8 ", 1280, 720)).Should().Be((1280, 720));
        ResourcePhotoRules.ReadSize(WebpOf("VP8L", 1024, 768)).Should().Be((1024, 768));
        ResourcePhotoRules.ReadSize(WebpOf("VP8X", 5000, 4000)).Should().Be((5000, 4000));
    }

    [Fact]
    public void AHeaderWithoutASize_IsNotAPhoto()
    {
        byte[] signatureOnly = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        byte[] jpegWithoutAFrame = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00];

        ResourcePhotoRules.ReadSize(signatureOnly).Should().BeNull();
        ResourcePhotoRules.ReadSize(jpegWithoutAFrame).Should().BeNull();
        ResourcePhotoRules.ReadSize(PngOf(0, 10)).Should().BeNull();
        ResourcePhotoRules.ReadSize(PngOf(uint.MaxValue, 10)).Should().BeNull();
        Errors(signatureOnly).Should().Equal("Photo: The photo must be a JPEG, PNG or WebP image.");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public void APictureLargerThanTheLongestSide_IsRefused_HoweverSmallItsFile(string kind)
    {
        var (wide, tall, fits) = kind switch
        {
            "png" => (PngOf(4097, 10), PngOf(10, 20000), PngOf(4096, 4096)),
            "jpeg" => (JpegOf(4097, 10), JpegOf(10, 20000), JpegOf(4096, 4096)),
            _ => (WebpOf("VP8X", 4097, 10), WebpOf("VP8X", 10, 20000), WebpOf("VP8X", 4096, 4096)),
        };
        const string tooLarge = "Photo: The photo can be at most 4096 pixels wide and 4096 pixels tall.";

        Errors(wide).Should().Equal(tooLarge);
        Errors(tall).Should().Equal(tooLarge);
        Errors(fits).Should().BeEmpty();
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
