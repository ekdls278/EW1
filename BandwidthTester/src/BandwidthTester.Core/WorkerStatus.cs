namespace BandwidthTester.Core;

public enum WorkerStatus
{
    Stopped,
    Starting,
    Listening,
    Connecting,
    Connected,
    Error
}

/// <summary>Point-in-time snapshot of one socket's traffic counters, for UI binding/logging.</summary>
public sealed record SocketStats(
    WorkerStatus Status,
    long TxBytesTotal,
    long RxBytesTotal,
    long TxPacketsTotal,
    long RxPacketsTotal,
    double TxBytesPerSecond,
    double RxBytesPerSecond,
    string? RemoteEndpoint,
    string? LastError)
{
    public static SocketStats Idle { get; } = new(WorkerStatus.Stopped, 0, 0, 0, 0, 0, 0, null, null);
}
