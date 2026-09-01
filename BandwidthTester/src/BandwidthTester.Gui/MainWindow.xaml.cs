using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BandwidthTester.Core;
using Microsoft.Win32;

namespace BandwidthTester.Gui;

public partial class MainWindow : Window
{
    private readonly SocketSessionManager _manager = new();
    private readonly ObservableCollection<SocketRowViewModel> _rows = new();
    private bool _closeConfirmed;

    // Auto-save target: defaults next to the exe so state survives an app restart with
    // zero setup. 설정 불러오기/다른 이름으로 저장 both repoint this so later edits keep
    // autosaving to whichever file the user is actually working with.
    private string _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    public MainWindow()
    {
        InitializeComponent();
        gridSockets.ItemsSource = _rows;
        _manager.LogEmitted += OnLogEmitted;

        LoadAutoSavedConfigOnStartup();
    }

    private void LoadAutoSavedConfigOnStartup()
    {
        if (!File.Exists(_configPath))
            return;

        try
        {
            var config = ConfigStore.Load(_configPath);
            foreach (var profile in config.Sockets)
            {
                var worker = _manager.Add(profile);
                _rows.Add(new SocketRowViewModel(worker));
            }
            OnLogEmitted(null!, $"이전 설정을 자동으로 불러왔습니다 ({config.Sockets.Count}개): {_configPath}");
        }
        catch (Exception ex)
        {
            OnLogEmitted(null!, $"자동 불러오기 실패: {ex.Message}");
        }
    }

    /// <summary>Writes the current socket list to <see cref="_configPath"/> right away, so a
    /// setting change is never at risk of being lost - no separate "저장" click required.</summary>
    private void SaveConfigSilently()
    {
        try
        {
            ConfigStore.Save(_manager.ToConfig(), _configPath);
        }
        catch (Exception ex)
        {
            OnLogEmitted(null!, $"자동 저장 실패: {ex.Message}");
        }
    }

    private void OnLogEmitted(SocketWorker worker, string message)
    {
        Dispatcher.Invoke(() =>
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            txtLog.ScrollToEnd();
        });
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SocketProfile { Name = $"socket-{_rows.Count + 1}" };
        var dialog = new SocketEditWindow(profile) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } created)
        {
            var worker = _manager.Add(created);
            _rows.Add(new SocketRowViewModel(worker));
            SaveConfigSilently();
        }
    }

    private async void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (gridSockets.SelectedItem is not SocketRowViewModel row)
        {
            MessageBox.Show(this, "편집할 소켓을 선택하세요.", "알림");
            return;
        }

        bool wasRunning = row.Worker.IsRunning;
        var dialog = new SocketEditWindow(row.Profile) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edited)
            return;

        // Rebuild the worker so a fresh BandwidthLimiter/state picks up the new settings,
        // but keep the row's position in the list (and re-select it) so the edit is visible
        // in place instead of jumping to the bottom - otherwise it looks like nothing happened,
        // and the row that shifts up to fill the gap ends up taking start/stop clicks meant
        // for the edited socket.
        int index = _rows.IndexOf(row);
        await _manager.RemoveAsync(row.Profile.Id).ConfigureAwait(true);
        var worker = _manager.Add(edited);
        var newRow = new SocketRowViewModel(worker);
        if (index >= 0)
            _rows[index] = newRow;
        else
            _rows.Add(newRow);
        gridSockets.SelectedItem = newRow;

        if (wasRunning)
            worker.Start();

        SaveConfigSilently();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (gridSockets.SelectedItem is not SocketRowViewModel row)
        {
            MessageBox.Show(this, "복사할 소켓을 선택하세요.", "알림");
            return;
        }

        var copy = ClonedProfile(row.Profile);
        var worker = _manager.Add(copy);
        var newRow = new SocketRowViewModel(worker);

        int index = _rows.IndexOf(row);
        if (index >= 0)
            _rows.Insert(index + 1, newRow); // right next to the original, not at the end
        else
            _rows.Add(newRow);
        gridSockets.SelectedItem = newRow;

        SaveConfigSilently();
    }

    private static SocketProfile ClonedProfile(SocketProfile source) => new()
    {
        // Id is intentionally left at its default (a fresh Guid) so the copy is a distinct socket.
        Name = $"{source.Name} - 복사",
        Role = source.Role,
        Protocol = source.Protocol,
        LocalIp = source.LocalIp,
        LocalPort = source.LocalPort,
        RemoteIp = source.RemoteIp,
        RemotePort = source.RemotePort,
        SendByteOrder = source.SendByteOrder,
        ReceiveByteOrder = source.ReceiveByteOrder,
        MessageSize = source.MessageSize,
        TargetBandwidthBytesPerSec = source.TargetBandwidthBytesPerSec,
        SendEnabled = source.SendEnabled,
        Header = new HeaderDefinition
        {
            Fields = source.Header.Fields
                .Select(f => new HeaderFieldDefinition { Name = f.Name, Type = f.Type, Size = f.Size, Auto = f.Auto, Value = f.Value })
                .ToList()
        }
    };

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = gridSockets.SelectedItems.Cast<SocketRowViewModel>().ToList();
        if (selected.Count == 0)
            return;

        foreach (var row in selected)
        {
            await _manager.RemoveAsync(row.Profile.Id).ConfigureAwait(true);
            _rows.Remove(row);
        }
        SaveConfigSilently();
    }

    private void BtnStartAll_Click(object sender, RoutedEventArgs e) => _manager.StartAll();

    private async void BtnStopAll_Click(object sender, RoutedEventArgs e) =>
        await _manager.StopAllAsync().ConfigureAwait(true);

    private void BtnRowStart_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SocketRowViewModel row)
            row.Worker.Start();
    }

    private async void BtnRowStop_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SocketRowViewModel row)
            await row.Worker.StopAsync().ConfigureAwait(true);
    }

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON 설정 파일 (*.json)|*.json|모든 파일|*.*" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var config = ConfigStore.Load(dialog.FileName);
            await _manager.LoadAsync(config).ConfigureAwait(true);
            _rows.Clear();
            foreach (var worker in _manager.Workers)
                _rows.Add(new SocketRowViewModel(worker));
            _configPath = dialog.FileName; // further edits autosave here from now on
            OnLogEmitted(null!, $"설정 {config.Sockets.Count}개 소켓을 불러왔습니다: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"설정 불러오기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON 설정 파일 (*.json)|*.json|모든 파일|*.*", FileName = "config.json" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ConfigStore.Save(_manager.ToConfig(), dialog.FileName);
            _configPath = dialog.FileName; // further edits autosave here from now on
            OnLogEmitted(null!, $"설정을 저장했습니다: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"설정 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        await _manager.StopAllAsync().ConfigureAwait(true);
        _closeConfirmed = true;
        Close();
    }
}
