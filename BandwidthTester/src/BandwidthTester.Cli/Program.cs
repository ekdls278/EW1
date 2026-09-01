using BandwidthTester.Core;

if (args.Length < 1)
{
    Console.WriteLine("사용법: BandwidthTesterCli.exe <config.json>");
    Console.WriteLine("  config.json 안의 모든 소켓을 시작하고, Ctrl+C를 누르면 정지합니다.");
    return 1;
}

string configPath = args[0];
AppConfig config;
try
{
    config = ConfigStore.Load(configPath);
}
catch (Exception ex)
{
    Console.WriteLine($"설정 파일을 불러오지 못했습니다: {ex.Message}");
    return 1;
}

var manager = new SocketSessionManager();
manager.LogEmitted += (worker, message) =>
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

manager.StatsUpdated += (worker, stats) =>
{
    if (stats.Status is WorkerStatus.Stopped or WorkerStatus.Starting)
        return;
    Console.WriteLine(
        $"[{DateTime.Now:HH:mm:ss}] {worker.Profile.Name,-20} " +
        $"status={stats.Status,-10} " +
        $"tx={FormatRate(stats.TxBytesPerSecond),10} rx={FormatRate(stats.RxBytesPerSecond),10} " +
        $"totalTx={stats.TxBytesTotal,12} totalRx={stats.RxBytesTotal,12}" +
        (stats.LastError is { } err ? $" ERROR={err}" : ""));
};

foreach (var profile in config.Sockets)
    manager.Add(profile);

Console.WriteLine($"{config.Sockets.Count}개 소켓을 불러왔습니다. 시작합니다... (Ctrl+C로 정지)");
foreach (var socket in config.Sockets.Where(s => s.Enabled))
    manager.Start(socket.Id);

var stopSignal = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopSignal.TrySetResult();
};

await stopSignal.Task;
Console.WriteLine("정지 중...");
await manager.StopAllAsync();
Console.WriteLine("정지 완료.");
return 0;

static string FormatRate(double bytesPerSec)
{
    double v = bytesPerSec;
    string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
    int i = 0;
    while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
    return $"{v:0.#} {units[i]}";
}
