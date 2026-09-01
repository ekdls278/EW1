using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BandwidthTester.Core;

namespace BandwidthTester.AvaloniaGui;

public partial class MainWindow : Window
{
    private readonly SocketSessionManager _manager = new();
    private readonly ObservableCollection<SocketRowViewModel> _rows = new();
    private bool _closeConfirmed;

    // Auto-save target: defaults next to the exe so state survives an app restart with
    // zero setup. 설정 불러오기/설정 저장 both repoint this so later edits keep autosaving
    // to whichever file the user is actually working with.
    private string _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    public MainWindow()
    {
        InitializeComponent();
        gridSockets.ItemsSource = _rows;
        _manager.LogEmitted += OnLogEmitted;
        Closing += MainWindow_Closing;

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
            AppendLog($"이전 설정을 자동으로 불러왔습니다 ({config.Sockets.Count}개): {_configPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"자동 불러오기 실패: {ex.Message}");
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
            AppendLog($"자동 저장 실패: {ex.Message}");
        }
    }

    private void OnLogEmitted(SocketWorker worker, string message) => AppendLog(message);

    private void AppendLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            txtLog.CaretIndex = txtLog.Text?.Length ?? 0;
        });
    }

    private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        var profile = new SocketProfile { Name = $"socket-{_rows.Count + 1}" };
        var dialog = new SocketEditWindow(profile);
        var result = await dialog.ShowDialog<SocketProfile?>(this);
        if (result is { } created)
        {
            var worker = _manager.Add(created);
            _rows.Add(new SocketRowViewModel(worker));
            SaveConfigSilently();
        }
    }

    private async void BtnEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (gridSockets.SelectedItem is not SocketRowViewModel row)
        {
            await SimpleMessageWindow.ShowAsync(this, "편집할 소켓을 선택하세요.");
            return;
        }

        bool wasRunning = row.Worker.IsRunning;
        var dialog = new SocketEditWindow(row.Profile);
        var edited = await dialog.ShowDialog<SocketProfile?>(this);
        if (edited is null)
            return;

        // Rebuild the worker so a fresh BandwidthLimiter/state picks up the new settings,
        // but keep the row's position in the list (and re-select it) so the edit is visible
        // in place instead of jumping to the bottom - otherwise it looks like nothing happened,
        // and the row that shifts up to fill the gap ends up taking start/stop clicks meant
        // for the edited socket.
        int index = _rows.IndexOf(row);
        await _manager.RemoveAsync(row.Profile.Id);
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

    private async void BtnCopy_Click(object? sender, RoutedEventArgs e)
    {
        if (gridSockets.SelectedItem is not SocketRowViewModel row)
        {
            await SimpleMessageWindow.ShowAsync(this, "복사할 소켓을 선택하세요.");
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

    private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        var selected = gridSockets.SelectedItems.Cast<SocketRowViewModel>().ToList();
        if (selected.Count == 0)
            return;

        foreach (var row in selected)
        {
            await _manager.RemoveAsync(row.Profile.Id);
            _rows.Remove(row);
        }
        SaveConfigSilently();
    }

    private void BtnStartAll_Click(object? sender, RoutedEventArgs e) => _manager.StartAll();

    private async void BtnStopAll_Click(object? sender, RoutedEventArgs e) => await _manager.StopAllAsync();

    private void BtnRowStart_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SocketRowViewModel row)
            row.Worker.Start();
    }

    private async void BtnRowStop_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SocketRowViewModel row)
            await row.Worker.StopAsync();
    }

    private async void BtnLoad_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "설정 파일 선택",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON 설정 파일") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0)
            return;

        string path = files[0].Path.LocalPath;
        try
        {
            var config = ConfigStore.Load(path);
            await _manager.LoadAsync(config);
            _rows.Clear();
            foreach (var worker in _manager.Workers)
                _rows.Add(new SocketRowViewModel(worker));
            _configPath = path; // further edits autosave here from now on
            AppendLog($"설정 {config.Sockets.Count}개 소켓을 불러왔습니다: {path}");
        }
        catch (Exception ex)
        {
            await SimpleMessageWindow.ShowAsync(this, $"설정 불러오기 실패: {ex.Message}", "오류");
        }
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "설정 저장",
            SuggestedFileName = "config.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON 설정 파일") { Patterns = new[] { "*.json" } } }
        });
        if (file is null)
            return;

        try
        {
            ConfigStore.Save(_manager.ToConfig(), file.Path.LocalPath);
            _configPath = file.Path.LocalPath; // further edits autosave here from now on
            AppendLog($"설정을 저장했습니다: {file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            await SimpleMessageWindow.ShowAsync(this, $"설정 저장 실패: {ex.Message}", "오류");
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        await _manager.StopAllAsync();
        _closeConfirmed = true;
        Close();
    }
}
