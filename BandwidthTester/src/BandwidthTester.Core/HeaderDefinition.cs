using System.Buffers.Binary;
using System.Globalization;

namespace BandwidthTester.Core;

/// <summary>
/// The fixed 20-byte struct prefixed to every message a socket sends. Its layout
/// (field names, types, order) is entirely user-configurable via <see cref="Fields"/>,
/// as long as the field sizes sum to exactly <see cref="TotalSize"/> bytes.
/// </summary>
public sealed class HeaderDefinition
{
    public const int TotalSize = 20;

    public List<HeaderFieldDefinition> Fields { get; set; } = new();

    /// <summary>Validates every field and that their sizes sum to exactly 20 bytes.</summary>
    public void Validate()
    {
        if (Fields.Count == 0)
            throw new FormatException("Header must define at least one field.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (var field in Fields)
        {
            field.Validate();
            if (!names.Add(field.Name))
                throw new FormatException($"Duplicate header field name '{field.Name}'.");
            total += field.Size;
        }

        if (total != TotalSize)
            throw new FormatException($"Header fields sum to {total} bytes; they must sum to exactly {TotalSize}.");
    }

    /// <summary>
    /// Writes the 20-byte header into <paramref name="destination"/> (must be exactly
    /// <see cref="TotalSize"/> bytes) honoring each field's multi-byte order per <paramref name="order"/>.
    /// </summary>
    public void Encode(Span<byte> destination, ByteOrder order, ulong sequenceNumber, int payloadLength)
    {
        if (destination.Length != TotalSize)
            throw new ArgumentException($"Destination must be exactly {TotalSize} bytes.", nameof(destination));

        int offset = 0;
        foreach (var field in Fields)
        {
            var slot = destination.Slice(offset, field.Size);
            WriteField(field, slot, order, sequenceNumber, payloadLength);
            offset += field.Size;
        }
    }

    private static void WriteField(HeaderFieldDefinition field, Span<byte> slot, ByteOrder order,
        ulong sequenceNumber, int payloadLength)
    {
        if (field.Type == HeaderFieldType.Bytes)
        {
            // Raw byte blobs are copied as configured; endianness does not apply to opaque bytes.
            field.FixedBytesLittleEndian.CopyTo(slot);
            return;
        }

        if (field.Type is HeaderFieldType.Float32 or HeaderFieldType.Float64)
        {
            double d = field.Auto switch
            {
                HeaderFieldAuto.Sequence => sequenceNumber,
                HeaderFieldAuto.TimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                HeaderFieldAuto.PayloadLength => payloadLength,
                _ => field.Type == HeaderFieldType.Float32
                    ? BinaryPrimitives.ReadSingleLittleEndian(field.FixedBytesLittleEndian)
                    : BinaryPrimitives.ReadDoubleLittleEndian(field.FixedBytesLittleEndian)
            };
            if (field.Type == HeaderFieldType.Float32)
            {
                if (order == ByteOrder.LittleEndian) BinaryPrimitives.WriteSingleLittleEndian(slot, (float)d);
                else BinaryPrimitives.WriteSingleBigEndian(slot, (float)d);
            }
            else
            {
                if (order == ByteOrder.LittleEndian) BinaryPrimitives.WriteDoubleLittleEndian(slot, d);
                else BinaryPrimitives.WriteDoubleBigEndian(slot, d);
            }
            return;
        }

        long value = field.Auto switch
        {
            HeaderFieldAuto.Sequence => unchecked((long)sequenceNumber),
            HeaderFieldAuto.TimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            HeaderFieldAuto.PayloadLength => payloadLength,
            _ => ReadFixedAsInt64(field)
        };

        WriteInteger(slot, value, order);
    }

    private static long ReadFixedAsInt64(HeaderFieldDefinition field)
    {
        var bytes = field.FixedBytesLittleEndian;
        return field.Size switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadInt32LittleEndian(bytes),
            8 => BinaryPrimitives.ReadInt64LittleEndian(bytes),
            _ => throw new InvalidOperationException()
        };
    }

    private static void WriteInteger(Span<byte> slot, long value, ByteOrder order)
    {
        switch (slot.Length)
        {
            case 1:
                slot[0] = unchecked((byte)value);
                break;
            case 2:
                if (order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt16LittleEndian(slot, unchecked((short)value));
                else BinaryPrimitives.WriteInt16BigEndian(slot, unchecked((short)value));
                break;
            case 4:
                if (order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt32LittleEndian(slot, unchecked((int)value));
                else BinaryPrimitives.WriteInt32BigEndian(slot, unchecked((int)value));
                break;
            case 8:
                if (order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt64LittleEndian(slot, value);
                else BinaryPrimitives.WriteInt64BigEndian(slot, value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported integer field size {slot.Length}.");
        }
    }

    /// <summary>
    /// Decodes a received 20-byte header (exactly <see cref="TotalSize"/> bytes) into a
    /// name -> value map for display/inspection, honoring <paramref name="order"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object> Decode(ReadOnlySpan<byte> source, ByteOrder order)
    {
        if (source.Length != TotalSize)
            throw new ArgumentException($"Source must be exactly {TotalSize} bytes.", nameof(source));

        var result = new Dictionary<string, object>(Fields.Count);
        int offset = 0;
        foreach (var field in Fields)
        {
            var slot = source.Slice(offset, field.Size);
            result[field.Name] = DecodeField(field, slot, order);
            offset += field.Size;
        }
        return result;
    }

    private static object DecodeField(HeaderFieldDefinition field, ReadOnlySpan<byte> slot, ByteOrder order)
    {
        switch (field.Type)
        {
            case HeaderFieldType.Bytes:
                return Convert.ToHexString(slot);
            case HeaderFieldType.UInt8:
                return slot[0];
            case HeaderFieldType.Int8:
                return unchecked((sbyte)slot[0]);
            case HeaderFieldType.UInt16:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(slot)
                    : BinaryPrimitives.ReadUInt16BigEndian(slot);
            case HeaderFieldType.Int16:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadInt16LittleEndian(slot)
                    : BinaryPrimitives.ReadInt16BigEndian(slot);
            case HeaderFieldType.UInt32:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(slot)
                    : BinaryPrimitives.ReadUInt32BigEndian(slot);
            case HeaderFieldType.Int32:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(slot)
                    : BinaryPrimitives.ReadInt32BigEndian(slot);
            case HeaderFieldType.UInt64:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadUInt64LittleEndian(slot)
                    : BinaryPrimitives.ReadUInt64BigEndian(slot);
            case HeaderFieldType.Int64:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadInt64LittleEndian(slot)
                    : BinaryPrimitives.ReadInt64BigEndian(slot);
            case HeaderFieldType.Float32:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadSingleLittleEndian(slot)
                    : BinaryPrimitives.ReadSingleBigEndian(slot);
            case HeaderFieldType.Float64:
                return order == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadDoubleLittleEndian(slot)
                    : BinaryPrimitives.ReadDoubleBigEndian(slot);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>Convenience factory: a default 20-byte header (magic:4, seq:4, timestampMs:8, length:4).</summary>
    public static HeaderDefinition CreateDefault() => new()
    {
        Fields = new List<HeaderFieldDefinition>
        {
            new() { Name = "magic", Type = HeaderFieldType.UInt32, Value = "0x42575354" }, // "BWST"
            new() { Name = "seq", Type = HeaderFieldType.UInt32, Auto = HeaderFieldAuto.Sequence },
            new() { Name = "timestampMs", Type = HeaderFieldType.UInt64, Auto = HeaderFieldAuto.TimestampMs },
            new() { Name = "payloadLength", Type = HeaderFieldType.UInt32, Auto = HeaderFieldAuto.PayloadLength }
        }
    };
}
