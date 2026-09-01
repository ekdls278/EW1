namespace BandwidthTester.Core;

public enum TransportProtocol
{
    Tcp,
    Udp
}

public enum SocketRole
{
    Client,
    Server
}

public enum ByteOrder
{
    LittleEndian,
    BigEndian
}

/// <summary>
/// Field types usable inside the fixed 20-byte user-defined header.
/// Sizes are fixed by type, except <see cref="Bytes"/> which uses an explicit size.
/// </summary>
public enum HeaderFieldType
{
    UInt8,
    Int8,
    UInt16,
    Int16,
    UInt32,
    Int32,
    UInt64,
    Int64,
    Float32,
    Float64,
    Bytes
}

/// <summary>
/// How a header field's value is produced at send time.
/// </summary>
public enum HeaderFieldAuto
{
    /// <summary>Use the fixed <see cref="HeaderFieldDefinition.Value"/> on every send.</summary>
    None,
    /// <summary>Auto-incrementing packet sequence number, starting at 0.</summary>
    Sequence,
    /// <summary>Milliseconds since Unix epoch at send time (fits UInt64/Int64).</summary>
    TimestampMs,
    /// <summary>Payload size in bytes (excludes the 20-byte header itself).</summary>
    PayloadLength
}
