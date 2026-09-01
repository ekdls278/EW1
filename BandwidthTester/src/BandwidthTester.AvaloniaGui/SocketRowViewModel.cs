using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using BandwidthTester.Core;

namespace BandwidthTester.AvaloniaGui;

/// <summary>One row in the main socket list: a profile plus its live worker/stats.</summary>
public sealed class SocketRowViewModel : INotifyPropertyChanged
{
    public SocketWorker Worker { get; }
    public SocketProfile Profile => Worker.Profile;

    public event PropertyChangedEventHandler? PropertyChanged;

    private WorkerStatus _status = WorkerStatus.Stopped;
    private long _txTotal, _rxTotal;
    private double _txRate, _rxRate;
    private string? _remoteEndpoint;
    private string? _lastError;

    public SocketRowViewModel(SocketWorker worker)
    {
        Worker = worker;
        Worker.StatsUpdated += OnStatsUpdated;
    }

    public string Name => Profile.Name;
    public string RoleProtocol => $"{Profile.Role}/{Profile.Protocol}";
    public string Local => $"{Profile.LocalIp}:{Profile.LocalPort}";
    public string Remote => $"{Profile.RemoteIp}:{Profile.RemotePort}";
    public int MessageSize => Profile.MessageSize;
    public string Bandwidth => Profile.TargetBandwidthBytesPerSec == 0 ? "무제한" : FormatRate(Profile.TargetBandwidthBytesPerSec);
    public string Endianness => $"TX:{Short(Profile.SendByteOrder)} / RX:{Short(Profile.ReceiveByteOrder)}";

    public WorkerStatus Status
    {
        get => _status;
        private set { _status = value; Raise(); Raise(nameof(IsRunning)); Raise(nameof(IsNotRunning)); }
    }

    public bool IsRunning => Worker.IsRunning;

    /// <summary>Bound to the row's 시작 button's IsEnabled, so only the valid action is clickable.</summary>
    public bool IsNotRunning => !Worker.IsRunning;

    public long TxTotal { get => _txTotal; private set { _txTotal = value; Raise(); } }
    public long RxTotal { get => _rxTotal; private set { _rxTotal = value; Raise(); } }
    public string TxRateText => FormatRate((long)_txRate);
    public string RxRateText => FormatRate((long)_rxRate);

    /// <summary>TX/RX send+receive rate combined into one column, to keep the window narrower.</summary>
    public string RateText => $"{TxRateText} / {RxRateText}";

    /// <summary>TX/RX cumulative bytes combined into one column, to keep the window narrower.</summary>
    public string TotalText => $"{TxTotal:N0} / {RxTotal:N0}";

    public string? RemoteEndpoint { get => _remoteEndpoint; private set { _remoteEndpoint = value; Raise(); } }
    public string? LastError { get => _lastError; private set { _lastError = value; Raise(); } }

    private static string Short(ByteOrder order) => order == ByteOrder.LittleEndian ? "LE" : "BE";

    private static string FormatRate(long bytesPerSec)
    {
        double v = bytesPerSec;
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }

    private void OnStatsUpdated(SocketWorker worker, SocketStats stats)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Status = stats.Status;
            TxTotal = stats.TxBytesTotal;
            RxTotal = stats.RxBytesTotal;
            _txRate = stats.TxBytesPerSecond;
            _rxRate = stats.RxBytesPerSecond;
            RemoteEndpoint = stats.RemoteEndpoint;
            LastError = stats.LastError;
            Raise(nameof(TxRateText));
            Raise(nameof(RxRateText));
            Raise(nameof(RateText));
            Raise(nameof(TotalText));
        });
    }

    /// <summary>Refreshes the config-derived display columns after the profile was edited.</summary>
    public void RefreshFromProfile()
    {
        Raise(nameof(Name));
        Raise(nameof(RoleProtocol));
        Raise(nameof(Local));
        Raise(nameof(Remote));
        Raise(nameof(MessageSize));
        Raise(nameof(Bandwidth));
        Raise(nameof(Endianness));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
