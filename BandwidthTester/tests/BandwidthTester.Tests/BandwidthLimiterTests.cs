using System.Diagnostics;
using BandwidthTester.Core;
using Xunit;

namespace BandwidthTester.Tests;

public class BandwidthLimiterTests
{
    [Fact]
    public async Task Unlimited_NeverWaits()
    {
        var limiter = new BandwidthLimiter(0);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            await limiter.WaitAsync(10_000);
        Assert.True(sw.ElapsedMilliseconds < 200);
    }

    [Fact]
    public async Task LimitedRate_PacesSendsToApproximatelyTheTargetRate()
    {
        const long ratePerSec = 200_000; // 200 KB/s
        const int chunk = 20_000; // 20 KB per send -> 10 sends/sec expected
        var limiter = new BandwidthLimiter(ratePerSec);

        var sw = Stopwatch.StartNew();
        int sends = 0;
        while (sw.ElapsedMilliseconds < 1000)
        {
            await limiter.WaitAsync(chunk);
            sends++;
        }
        sw.Stop();

        double actualBytesPerSec = sends * (double)chunk / sw.Elapsed.TotalSeconds;

        // Allow generous tolerance for CI/container scheduling jitter.
        Assert.InRange(actualBytesPerSec, ratePerSec * 0.5, ratePerSec * 1.5);
    }
}
