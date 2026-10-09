using System.Buffers.Binary;

namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourcePhotoRules
{
    // Plenty for a photo shown at a few hundred pixels; the app shrinks larger ones before
    // uploading them.
    public const int MaxBytes = 2 * 1024 * 1024;

    // The longest side a photo may have. The app sends at most 1600 pixels; this leaves room for
    // other clients, and keeps out a small file that opens into a huge picture: a 2 MB PNG of one
    // colour can be 20000 x 20000, more than a phone's browser can draw.
    public const int MaxSide = 4096;

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

    // Width and height, read from the image's header without decoding it: PNG's IHDR chunk,
    // JPEG's start-of-frame marker, WebP's VP8, VP8L or VP8X chunk. Null when the header can't be
    // read, or gives no size.
    public static (int Width, int Height)? ReadSize(ReadOnlySpan<byte> data)
    {
        var size = DetectContentType(data) switch
        {
            "image/png" => PngSize(data),
            "image/jpeg" => JpegSize(data),
            "image/webp" => WebpSize(data),
            _ => null,
        };
        return size is { Width: > 0, Height: > 0 } ? size : null;
    }

    // The signature, then the IHDR chunk: its length and type, then width and height as
    // big-endian 32-bit numbers. Ones that don't fit an int read as no size.
    private static (int Width, int Height)? PngSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24 || !data[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        return width <= int.MaxValue && height <= int.MaxValue ? ((int)width, (int)height) : null;
    }

    // Segments after the start-of-image marker, each FF xx and, except for a few, a length, until
    // a start-of-frame (C0-CF but C4, C8, CC): precision, then height and width as big-endian
    // 16-bit numbers. The scan or the end before any frame: no size.
    private static (int Width, int Height)? JpegSize(ReadOnlySpan<byte> data)
    {
        var at = 2;
        while (at + 3 < data.Length)
        {
            if (data[at] != 0xFF)
            {
                return null;
            }

            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++; // fill byte
                continue;
            }

            if (marker is 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                at += 2; // a marker without a length
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return at + 9 <= data.Length
                    ? (BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]))
                    : null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (length < 2)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }

    // The first chunk after RIFF....WEBP: a lossy frame (VP8: after its tag and start code,
    // 14-bit width and height), a lossless one (VP8L: after its signature byte, width - 1 and
    // height - 1 in 14 bits each), or the extended header (VP8X: canvas width - 1 and
    // height - 1 in 24 bits each).
    private static (int Width, int Height)? WebpSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
        {
            return null;
        }

        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A
                ? (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF)
                : null;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return ((data[24] | data[25] << 8 | data[26] << 16) + 1, (data[27] | data[28] << 8 | data[29] << 16) + 1);
        }

        return null;
    }
}
