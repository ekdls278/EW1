using System.Collections.ObjectModel;
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

    public MainWindow()
    {
        InitializeComponent();
        gridSockets.ItemsSource = _rows;
        _manager.LogEmitted += OnLogEmitted;
        Closing += MainWindow_Closing;
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
    }

    private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var row in gridSockets.SelectedItems.Cast<SocketRowViewModel>().ToList())
        {
            await _manager.RemoveAsync(row.Profile.Id);
            _rows.Remove(row);
        }
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
            return;

        e.Cancel = true;
        await _manager.StopAllAsync();
        _closeConfirmed = true;
        Close();
    }
}
