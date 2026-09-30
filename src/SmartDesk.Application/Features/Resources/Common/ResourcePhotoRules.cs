namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourcePhotoRules
{
    // Plenty for a photo shown at a few hundred pixels; the app shrinks larger ones before
    // uploading them.
    public const int MaxBytes = 2 * 1024 * 1024;

    // The request may be larger than the photo it carries (multipart framing), and a photo a
    // little over the limit should get the friendly 400, not the server's own 413.
    public const int MaxRequestBytes = 8 * 1024 * 1024;

    // JPEG, PNG or WebP, recognised by their first bytes rather than by the type the upload
    // claims. No SVG, which can carry script, and nothing else a browser would render as a
    // page. Null when it's none of these.
    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
