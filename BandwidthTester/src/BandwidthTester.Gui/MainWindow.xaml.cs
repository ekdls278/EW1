using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public MainWindow()
    {
        InitializeComponent();
        gridSockets.ItemsSource = _rows;
        _manager.LogEmitted += OnLogEmitted;
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

        // Rebuild the worker so a fresh BandwidthLimiter/state picks up the new settings.
        await _manager.RemoveAsync(row.Profile.Id).ConfigureAwait(true);
        _rows.Remove(row);
        var worker = _manager.Add(edited);
        var newRow = new SocketRowViewModel(worker);
        _rows.Add(newRow);
        if (wasRunning)
            worker.Start();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in gridSockets.SelectedItems.Cast<SocketRowViewModel>().ToList())
        {
            await _manager.RemoveAsync(row.Profile.Id).ConfigureAwait(true);
            _rows.Remove(row);
        }
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
