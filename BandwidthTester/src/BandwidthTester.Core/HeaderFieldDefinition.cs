using System.Buffers.Binary;
using System.Globalization;

namespace BandwidthTester.Core;

/// <summary>
/// One field inside the fixed 20-byte user-defined header that is prefixed to every
/// outgoing message. The struct layout (field order, type, size) is entirely user
/// configurable as long as the field sizes sum to exactly 20 bytes.
/// </summary>
public sealed class HeaderFieldDefinition
{
    public required string Name { get; set; }
    public HeaderFieldType Type { get; set; }

    /// <summary>
    /// Field size in bytes. Required (and used as-is) for <see cref="HeaderFieldType.Bytes"/>.
    /// For every other type it is derived from <see cref="Type"/> and this value is ignored
    /// on write (but kept in sync by <see cref="Validate"/>).
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// How the value is produced at send time. When not <see cref="HeaderFieldAuto.None"/>,
    /// <see cref="Value"/> is ignored for sending, but is still used to seed the packet the
    /// field is decoded against for display.
    /// </summary>
    public HeaderFieldAuto Auto { get; set; } = HeaderFieldAuto.None;

    /// <summary>
    /// Fixed value, as text. Integers accept decimal or 0x-prefixed hex. Floats accept
    /// standard decimal notation. Bytes accepts a hex string whose length is exactly
    /// <see cref="Size"/> * 2 hex digits (e.g. "DEADBEEF").
    /// </summary>
    public string Value { get; set; } = "0";

    private byte[]? _fixedBytesLittleEndian;

    public static int FixedSizeFor(HeaderFieldType type) => type switch
    {
        HeaderFieldType.UInt8 or HeaderFieldType.Int8 => 1,
        HeaderFieldType.UInt16 or HeaderFieldType.Int16 => 2,
        HeaderFieldType.UInt32 or HeaderFieldType.Int32 or HeaderFieldType.Float32 => 4,
        HeaderFieldType.UInt64 or HeaderFieldType.Int64 or HeaderFieldType.Float64 => 8,
        HeaderFieldType.Bytes => -1, // caller-defined
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// Resolves <see cref="Size"/> from <see cref="Type"/> (for fixed-size types) and
    /// pre-parses <see cref="Value"/> into raw little-endian bytes so encoding is cheap
    /// on every send. Throws <see cref="FormatException"/> if the field is misconfigured.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new FormatException("Header field name must not be empty.");

        int fixedSize = FixedSizeFor(Type);
        if (fixedSize >= 0)
        {
            Size = fixedSize;
        }
        else if (Size <= 0)
        {
            throw new FormatException($"Header field '{Name}' of type Bytes must have a positive Size.");
        }

        if (Auto == HeaderFieldAuto.Sequence && Type is not (HeaderFieldType.UInt8 or HeaderFieldType.UInt16
                or HeaderFieldType.UInt32 or HeaderFieldType.UInt64 or HeaderFieldType.Int8 or HeaderFieldType.Int16
                or HeaderFieldType.Int32 or HeaderFieldType.Int64))
        {
            throw new FormatException($"Header field '{Name}' uses auto=Sequence but is not an integer type.");
        }

        if (Auto == HeaderFieldAuto.TimestampMs && Size < 4)
        {
            throw new FormatException($"Header field '{Name}' uses auto=TimestampMs but is too small to hold it (needs >= 4 bytes).");
        }

        if (Auto == HeaderFieldAuto.PayloadLength && Type is not (HeaderFieldType.UInt8 or HeaderFieldType.UInt16
                or HeaderFieldType.UInt32 or HeaderFieldType.UInt64 or HeaderFieldType.Int8 or HeaderFieldType.Int16
                or HeaderFieldType.Int32 or HeaderFieldType.Int64))
        {
            throw new FormatException($"Header field '{Name}' uses auto=PayloadLength but is not an integer type.");
        }

        _fixedBytesLittleEndian = Auto == HeaderFieldAuto.None ? ParseFixedValue() : null;
    }

    private byte[] ParseFixedValue()
    {
        if (Type == HeaderFieldType.Bytes)
        {
            string hex = Value.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];
            if (hex.Length != Size * 2)
                throw new FormatException($"Header field '{Name}' expects {Size * 2} hex chars, got {hex.Length}.");
            var bytes = new byte[Size];
            for (int i = 0; i < Size; i++)
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        if (Type is HeaderFieldType.Float32 or HeaderFieldType.Float64)
        {
            double d = double.Parse(Value, CultureInfo.InvariantCulture);
            var floatBuf = new byte[Size];
            if (Type == HeaderFieldType.Float32)
                BinaryPrimitives.WriteSingleLittleEndian(floatBuf, (float)d);
            else
                BinaryPrimitives.WriteDoubleLittleEndian(floatBuf, d);
            return floatBuf;
        }

        long signed;
        string text = Value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            signed = unchecked((long)Convert.ToUInt64(text[2..], 16));
        else
            signed = long.Parse(text, CultureInfo.InvariantCulture);

        var buf = new byte[Size];
        switch (Type)
        {
            case HeaderFieldType.UInt8 or HeaderFieldType.Int8:
                buf[0] = unchecked((byte)signed);
                break;
            case HeaderFieldType.UInt16 or HeaderFieldType.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(buf, unchecked((short)signed));
                break;
            case HeaderFieldType.UInt32 or HeaderFieldType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(buf, unchecked((int)signed));
                break;
            case HeaderFieldType.UInt64 or HeaderFieldType.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(buf, signed);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return buf;
    }

    /// <summary>Little-endian bytes for the fixed (non-auto) value. Only valid after <see cref="Validate"/>.</summary>
    internal ReadOnlySpan<byte> FixedBytesLittleEndian =>
        _fixedBytesLittleEndian ?? throw new InvalidOperationException($"Header field '{Name}' was not validated.");
}
