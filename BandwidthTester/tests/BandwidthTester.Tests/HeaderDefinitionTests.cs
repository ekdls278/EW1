using BandwidthTester.Core;
using Xunit;

namespace BandwidthTester.Tests;

public class HeaderDefinitionTests
{
    [Fact]
    public void Validate_Throws_WhenFieldsDoNotSumTo20()
    {
        var header = new HeaderDefinition
        {
            Fields = { new HeaderFieldDefinition { Name = "onlyField", Type = HeaderFieldType.UInt32, Value = "1" } }
        };

        Assert.Throws<FormatException>(header.Validate);
    }

    [Fact]
    public void Validate_Throws_OnDuplicateFieldNames()
    {
        var header = new HeaderDefinition
        {
            Fields =
            {
                new HeaderFieldDefinition { Name = "x", Type = HeaderFieldType.UInt64, Value = "1" },
                new HeaderFieldDefinition { Name = "x", Type = HeaderFieldType.Bytes, Size = 12, Value = "00".PadRight(24, '0') }
            }
        };

        Assert.Throws<FormatException>(header.Validate);
    }

    [Theory]
    [InlineData(ByteOrder.LittleEndian)]
    [InlineData(ByteOrder.BigEndian)]
    public void EncodeThenDecode_RoundTripsFixedAndAutoFields(ByteOrder order)
    {
        var header = HeaderDefinition.CreateDefault(); // magic(u32 fixed), seq(u32 auto), timestampMs(u64 auto), payloadLength(u32 auto)
        header.Validate();

        Span<byte> buffer = stackalloc byte[HeaderDefinition.TotalSize];
        header.Encode(buffer, order, sequenceNumber: 42, payloadLength: 777);

        var decoded = header.Decode(buffer, order);

        Assert.Equal(0x42575354u, (uint)decoded["magic"]);
        Assert.Equal(42u, (uint)decoded["seq"]);
        Assert.Equal(777u, (uint)decoded["payloadLength"]);
        Assert.True((ulong)decoded["timestampMs"] > 0);
    }

    [Fact]
    public void Encode_ProducesDifferentBytes_ForLittleVsBigEndian_OnMultiByteField()
    {
        var header = new HeaderDefinition
        {
            Fields =
            {
                new HeaderFieldDefinition { Name = "value", Type = HeaderFieldType.UInt32, Value = "0x01020304" },
                new HeaderFieldDefinition { Name = "pad", Type = HeaderFieldType.Bytes, Size = 16, Value = new string('0', 32) }
            }
        };
        header.Validate();

        Span<byte> little = stackalloc byte[HeaderDefinition.TotalSize];
        Span<byte> big = stackalloc byte[HeaderDefinition.TotalSize];
        header.Encode(little, ByteOrder.LittleEndian, 0, 0);
        header.Encode(big, ByteOrder.BigEndian, 0, 0);

        // Little-endian: 04 03 02 01 ...   Big-endian: 01 02 03 04 ...
        Assert.Equal(0x04, little[0]);
        Assert.Equal(0x01, little[3]);
        Assert.Equal(0x01, big[0]);
        Assert.Equal(0x04, big[3]);
    }

    [Fact]
    public void BytesField_IsCopiedVerbatim_RegardlessOfEndianness()
    {
        var header = new HeaderDefinition
        {
            Fields =
            {
                new HeaderFieldDefinition { Name = "blob", Type = HeaderFieldType.Bytes, Size = 20, Value = "DEADBEEF" + new string('0', 32) }
            }
        };
        header.Validate();

        Span<byte> buffer = stackalloc byte[HeaderDefinition.TotalSize];
        header.Encode(buffer, ByteOrder.BigEndian, 0, 0);

        Assert.Equal(0xDE, buffer[0]);
        Assert.Equal(0xAD, buffer[1]);
        Assert.Equal(0xBE, buffer[2]);
        Assert.Equal(0xEF, buffer[3]);
    }

    [Fact]
    public void Sequence_And_PayloadLength_Auto_Fields_ReflectCallArguments()
    {
        var header = new HeaderDefinition
        {
            Fields =
            {
                new HeaderFieldDefinition { Name = "seq", Type = HeaderFieldType.UInt64, Auto = HeaderFieldAuto.Sequence },
                new HeaderFieldDefinition { Name = "len", Type = HeaderFieldType.UInt32, Auto = HeaderFieldAuto.PayloadLength },
                new HeaderFieldDefinition { Name = "pad", Type = HeaderFieldType.Bytes, Size = 8, Value = new string('0', 16) }
            }
        };
        header.Validate();

        Span<byte> buffer = stackalloc byte[HeaderDefinition.TotalSize];
        header.Encode(buffer, ByteOrder.LittleEndian, sequenceNumber: 123456, payloadLength: 4096);
        var decoded = header.Decode(buffer, ByteOrder.LittleEndian);

        Assert.Equal(123456UL, (ulong)decoded["seq"]);
        Assert.Equal(4096u, (uint)decoded["len"]);
    }
}
