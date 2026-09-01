namespace BandwidthTester.Core;

/// <summary>
/// One socket's full configuration: connection settings, message/bandwidth settings,
/// endianness, and its 20-byte header layout. The application holds an unbounded list
/// of these (<see cref="AppConfig.Sockets"/>) so the user can add as many as they like.
/// </summary>
public sealed class SocketProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-facing label, shown in the socket list.</summary>
    public string Name { get; set; } = "New Socket";

    /// <summary>If false, the profile is kept in the list but not started automatically.</summary>
    public bool Enabled { get; set; } = true;

    public SocketRole Role { get; set; } = SocketRole.Client;
    public TransportProtocol Protocol { get; set; } = TransportProtocol.Tcp;

    /// <summary>Local bind address. "0.0.0.0" (or "::" for IPv6) binds any interface.</summary>
    public string LocalIp { get; set; } = "0.0.0.0";

    /// <summary>Local bind port. 0 lets the OS pick an ephemeral port (client only).</summary>
    public int LocalPort { get; set; } = 0;

    /// <summary>Target address: TCP connect target (client) or UDP datagram destination.</summary>
    public string RemoteIp { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 9000;

    public ByteOrder SendByteOrder { get; set; } = ByteOrder.LittleEndian;
    public ByteOrder ReceiveByteOrder { get; set; } = ByteOrder.LittleEndian;

    /// <summary>Payload size in bytes per message, in addition to the fixed 20-byte header.</summary>
    public int MessageSize { get; set; } = 1024;

    /// <summary>
    /// Target send throughput in bytes/sec (payload + header). 0 means unlimited
    /// (send as fast as the socket allows).
    /// </summary>
    public long TargetBandwidthBytesPerSec { get; set; } = 1_000_000;

    /// <summary>Whether this socket actively sends traffic (in addition to always receiving).</summary>
    public bool SendEnabled { get; set; } = true;

    public HeaderDefinition Header { get; set; } = HeaderDefinition.CreateDefault();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new FormatException("Socket profile Name must not be empty.");
        if (MessageSize < 0)
            throw new FormatException($"'{Name}': MessageSize must be >= 0.");
        if (TargetBandwidthBytesPerSec < 0)
            throw new FormatException($"'{Name}': TargetBandwidthBytesPerSec must be >= 0.");
        if (Role == SocketRole.Client && (string.IsNullOrWhiteSpace(RemoteIp) || RemotePort is <= 0 or > 65535))
            throw new FormatException($"'{Name}': a Client socket needs a valid RemoteIp/RemotePort.");
        if (LocalPort is < 0 or > 65535)
            throw new FormatException($"'{Name}': LocalPort must be between 0 and 65535.");
        if (Role == SocketRole.Server && LocalPort == 0)
            throw new FormatException($"'{Name}': a Server socket needs a fixed LocalPort (not 0).");
        Header.Validate();
    }
}

/// <summary>Root of the JSON configuration file: an open-ended list of socket profiles.</summary>
public sealed class AppConfig
{
    public List<SocketProfile> Sockets { get; set; } = new();
}
