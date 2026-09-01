using System.Net;
using System.Net.Sockets;

namespace BandwidthTester.Core;

/// <summary>
/// Runs one <see cref="SocketProfile"/>: opens the socket per its local/remote/protocol/role
/// settings, paces an optional send loop to the configured bandwidth, and always runs a
/// receive loop that decodes the 20-byte header and tallies throughput.
/// </summary>
public sealed class SocketWorker : IAsyncDisposable
{
    public SocketProfile Profile { get; }

    public event Action<SocketWorker, string>? LogEmitted;
    public event Action<SocketWorker, SocketStats>? StatsUpdated;

    /// <summary>Raised for every received packet with its decoded 20-byte header, for inspection/logging.</summary>
    public event Action<SocketWorker, IReadOnlyDictionary<string, object>>? HeaderReceived;

    private readonly BandwidthLimiter _limiter;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Task? _statsTask;

    private long _txBytes, _rxBytes, _txPackets, _rxPackets;
    private long _txBytesPrev, _rxBytesPrev;
    private WorkerStatus _status = WorkerStatus.Stopped;
    private string? _lastError;
    private string? _remoteEndpointText;

    public SocketWorker(SocketProfile profile)
    {
        Profile = profile;
        _limiter = new BandwidthLimiter(profile.TargetBandwidthBytesPerSec);
    }

    public bool IsRunning => _runTask is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
            return;

        Profile.Validate();
        _txBytes = _rxBytes = _txPackets = _rxPackets = _txBytesPrev = _rxBytesPrev = 0;
        _lastError = null;
        _remoteEndpointText = null;
        _limiter.Reset();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetStatus(WorkerStatus.Starting);
        _runTask = RunAsync(token);
        _statsTask = StatsLoopAsync(token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        try
        {
            if (_runTask is not null) await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"stop error: {ex.Message}");
        }
        try
        {
            if (_statsTask is not null) await _statsTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        _cts.Dispose();
        _cts = null;
        SetStatus(WorkerStatus.Stopped);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private void Log(string message) => LogEmitted?.Invoke(this, $"[{Profile.Name}] {message}");

    private void SetStatus(WorkerStatus status)
    {
        _status = status;
        PublishStats();
    }

    private void PublishStats()
    {
        StatsUpdated?.Invoke(this, new SocketStats(
            _status,
            Interlocked.Read(ref _txBytes),
            Interlocked.Read(ref _rxBytes),
            Interlocked.Read(ref _txPackets),
            Interlocked.Read(ref _rxPackets),
            0, 0,
            _remoteEndpointText,
            _lastError));
    }

    private async Task StatsLoopAsync(CancellationToken ct)
    {
        try
        {
            // _runTask is only assigned after Start() calls SetStatus(Starting), so an
            // IsRunning-derived UI indicator (e.g. a "start" button's enabled state) would
            // otherwise stay stale until the first 1-second tick below. Publish once
            // immediately - by now _runTask is assigned, so IsRunning already reads correctly.
            PublishStats();

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                long tx = Interlocked.Read(ref _txBytes);
                long rx = Interlocked.Read(ref _rxBytes);
                double txRate = tx - _txBytesPrev;
                double rxRate = rx - _rxBytesPrev;
                _txBytesPrev = tx;
                _rxBytesPrev = rx;

                StatsUpdated?.Invoke(this, new SocketStats(
                    _status, tx, rx,
                    Interlocked.Read(ref _txPackets), Interlocked.Read(ref _rxPackets),
                    txRate, rxRate,
                    _remoteEndpointText, _lastError));
            }
        }
        catch (OperationCanceledException) { }
    }

    private static AddressFamily FamilyOf(string ip) =>
        IPAddress.Parse(ip).AddressFamily;

    /// <summary>
    /// Tunes a socket for sustained bandwidth-test throughput: a generous send/receive
    /// buffer (the OS default is often just tens of KB, which throttles throughput on
    /// higher-bandwidth-delay-product links) and, for TCP, Nagle's algorithm disabled so
    /// paced sends actually go out on schedule instead of being coalesced/delayed - a
    /// bandwidth *test* tool should never be the bottleneck it's trying to measure.
    /// </summary>
    private static void TuneForThroughput(Socket socket)
    {
        const int bufferSize = 1 << 20; // 1 MB
        socket.SendBufferSize = bufferSize;
        socket.ReceiveBufferSize = bufferSize;
        if (socket.ProtocolType == ProtocolType.Tcp)
            socket.NoDelay = true;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            if (Profile.Protocol == TransportProtocol.Tcp)
                await RunTcpAsync(ct).ConfigureAwait(false);
            else
                await RunUdpAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            SetStatus(WorkerStatus.Error);
            Log($"fatal error: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- TCP --

    private async Task RunTcpAsync(CancellationToken ct)
    {
        if (Profile.Role == SocketRole.Server)
        {
            using var listener = new Socket(FamilyOf(Profile.LocalIp), SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Parse(Profile.LocalIp), Profile.LocalPort));
            listener.Listen(16);
            SetStatus(WorkerStatus.Listening);
            Log($"listening on {listener.LocalEndPoint}");

            var connections = new List<Task>();
            while (!ct.IsCancellationRequested)
            {
                Socket accepted;
                try
                {
                    accepted = await listener.AcceptAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }

                Log($"accepted connection from {accepted.RemoteEndPoint}");
                connections.Add(HandleTcpConnectionAsync(accepted, ct));
                connections.RemoveAll(t => t.IsCompleted);
            }

            try { await Task.WhenAll(connections).ConfigureAwait(false); } catch { }
        }
        else
        {
            using var socket = new Socket(FamilyOf(Profile.LocalIp), SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Parse(Profile.LocalIp), Profile.LocalPort));

            SetStatus(WorkerStatus.Connecting);
            var remote = new IPEndPoint(IPAddress.Parse(Profile.RemoteIp), Profile.RemotePort);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await socket.ConnectAsync(remote, ct).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (SocketException ex)
                {
                    Log($"connect failed ({ex.SocketErrorCode}), retrying in 1s");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
            }

            if (!ct.IsCancellationRequested)
                await HandleTcpConnectionAsync(socket, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleTcpConnectionAsync(Socket socket, CancellationToken ct)
    {
        TuneForThroughput(socket);
        _remoteEndpointText = socket.RemoteEndPoint?.ToString();
        SetStatus(WorkerStatus.Connected);
        Log($"connected: local={socket.LocalEndPoint} remote={socket.RemoteEndPoint}");

        var receiveTask = TcpReceiveLoopAsync(socket, ct);

        if (Profile.SendEnabled)
        {
            var sendTask = TcpSendLoopAsync(socket, ct);
            // Either direction ending (peer closed, or send loop cancelled) tears down the connection.
            await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false);
            try { socket.Shutdown(SocketShutdown.Both); } catch { }
            socket.Close();
            try { await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false); } catch { }
        }
        else
        {
            try { await receiveTask.ConfigureAwait(false); } catch { }
            try { socket.Shutdown(SocketShutdown.Both); } catch { }
            socket.Close();
        }

        Log("connection closed");
    }

    private async Task TcpSendLoopAsync(Socket socket, CancellationToken ct)
    {
        int packetSize = HeaderDefinition.TotalSize + Profile.MessageSize;
        var packet = new byte[packetSize];
        FillPayloadPattern(packet.AsSpan(HeaderDefinition.TotalSize));
        ulong seq = 0;

        while (!ct.IsCancellationRequested)
        {
            Profile.Header.Encode(packet.AsSpan(0, HeaderDefinition.TotalSize), Profile.SendByteOrder, seq, Profile.MessageSize);
            await _limiter.WaitAsync(packetSize, ct).ConfigureAwait(false);

            int sent = 0;
            while (sent < packetSize)
            {
                int n = await socket.SendAsync(packet.AsMemory(sent), SocketFlags.None, ct).ConfigureAwait(false);
                if (n == 0) return;
                sent += n;
            }

            Interlocked.Add(ref _txBytes, packetSize);
            Interlocked.Increment(ref _txPackets);
            seq++;
        }
    }

    private async Task TcpReceiveLoopAsync(Socket socket, CancellationToken ct)
    {
        int packetSize = HeaderDefinition.TotalSize + Profile.MessageSize;
        var buffer = new byte[packetSize];

        while (!ct.IsCancellationRequested)
        {
            int received = 0;
            while (received < packetSize)
            {
                int n = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, ct).ConfigureAwait(false);
                if (n == 0)
                    return; // peer closed
                received += n;
            }

            if (packetSize >= HeaderDefinition.TotalSize)
            {
                var decoded = Profile.Header.Decode(buffer.AsSpan(0, HeaderDefinition.TotalSize), Profile.ReceiveByteOrder);
                HeaderReceived?.Invoke(this, decoded);
            }

            Interlocked.Add(ref _rxBytes, packetSize);
            Interlocked.Increment(ref _rxPackets);
        }
    }

    // ---------------------------------------------------------------- UDP --

    private async Task RunUdpAsync(CancellationToken ct)
    {
        using var socket = new Socket(FamilyOf(Profile.LocalIp), SocketType.Dgram, ProtocolType.Udp);
        TuneForThroughput(socket);
        socket.Bind(new IPEndPoint(IPAddress.Parse(Profile.LocalIp), Profile.LocalPort));
        SetStatus(WorkerStatus.Connected);
        Log($"udp bound on {socket.LocalEndPoint}");

        EndPoint? sendTarget = Profile.RemotePort != 0 && !string.IsNullOrWhiteSpace(Profile.RemoteIp)
            ? new IPEndPoint(IPAddress.Parse(Profile.RemoteIp), Profile.RemotePort)
            : null;
        _remoteEndpointText = sendTarget?.ToString();

        var receiveTask = UdpReceiveLoopAsync(socket, ct);
        var sendTask = (Profile.SendEnabled && sendTarget is not null)
            ? UdpSendLoopAsync(socket, sendTarget, ct)
            : Task.CompletedTask;

        await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
    }

    private async Task UdpSendLoopAsync(Socket socket, EndPoint target, CancellationToken ct)
    {
        int packetSize = HeaderDefinition.TotalSize + Profile.MessageSize;
        var packet = new byte[packetSize];
        FillPayloadPattern(packet.AsSpan(HeaderDefinition.TotalSize));
        ulong seq = 0;

        while (!ct.IsCancellationRequested)
        {
            Profile.Header.Encode(packet.AsSpan(0, HeaderDefinition.TotalSize), Profile.SendByteOrder, seq, Profile.MessageSize);
            await _limiter.WaitAsync(packetSize, ct).ConfigureAwait(false);

            await socket.SendToAsync(packet.AsMemory(0, packetSize), SocketFlags.None, target, ct).ConfigureAwait(false);

            Interlocked.Add(ref _txBytes, packetSize);
            Interlocked.Increment(ref _txPackets);
            seq++;
        }
    }

    private async Task UdpReceiveLoopAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[Math.Max(HeaderDefinition.TotalSize, 65_507)];
        EndPoint any = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            _remoteEndpointText ??= result.RemoteEndPoint.ToString();

            if (result.ReceivedBytes >= HeaderDefinition.TotalSize)
            {
                var decoded = Profile.Header.Decode(buffer.AsSpan(0, HeaderDefinition.TotalSize), Profile.ReceiveByteOrder);
                HeaderReceived?.Invoke(this, decoded);
            }

            Interlocked.Add(ref _rxBytes, result.ReceivedBytes);
            Interlocked.Increment(ref _rxPackets);
        }
    }

    private static void FillPayloadPattern(Span<byte> payload)
    {
        for (int i = 0; i < payload.Length; i++)
            payload[i] = unchecked((byte)i);
    }
}
