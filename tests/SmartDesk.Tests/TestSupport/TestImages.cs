using System.Buffers.Binary;

namespace SmartDesk.Tests.TestSupport;

// The smallest PNG that says what it is and how big: the signature and the IHDR chunk, all the
// upload rules read, since they never decode a picture.
public static class TestImages
{
    public static byte[] Png(uint width, uint height)
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. "IHDR"u8, .. new byte[17]];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        return data;
    }
}
