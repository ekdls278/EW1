using System.Diagnostics;

namespace BandwidthTester.Core;

/// <summary>
/// Paces sends to a target throughput (bytes/sec) using a simple leaky-bucket clock:
/// it tracks how many bytes "should" have been sent by now at the target rate, and
/// makes the caller wait whenever it is running ahead of that schedule.
/// A target of 0 bytes/sec means unlimited (never waits).
/// </summary>
public sealed class BandwidthLimiter
{
    private readonly long _bytesPerSecond;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private long _bytesAccounted;

    public BandwidthLimiter(long bytesPerSecond)
    {
        if (bytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        _bytesPerSecond = bytesPerSecond;
    }

    /// <summary>Target rate this limiter enforces, in bytes/sec. 0 = unlimited.</summary>
    public long BytesPerSecond => _bytesPerSecond;

    /// <summary>
    /// Waits (if needed) until sending <paramref name="byteCount"/> more bytes would stay
    /// on pace for the configured rate, then reserves that budget.
    /// </summary>
    public async Task WaitAsync(int byteCount, CancellationToken cancellationToken = default)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (_bytesPerSecond == 0)
            return; // unlimited

        TimeSpan delay;
        lock (_gate)
        {
            _bytesAccounted += byteCount;
            // Time at which _bytesAccounted bytes should have been sent, at the target rate.
            double dueSeconds = _bytesAccounted / (double)_bytesPerSecond;
            TimeSpan due = TimeSpan.FromSeconds(dueSeconds);
            TimeSpan elapsed = _clock.Elapsed;
            delay = due - elapsed;
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resets the pacing clock/budget, e.g. after a long pause.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _clock.Restart();
            _bytesAccounted = 0;
        }
    }
}
