using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BandwidthTester.Core;

namespace BandwidthTester.AvaloniaGui;

public partial class SocketEditWindow : Window
{
    private readonly Guid _profileId;
    private readonly ObservableCollection<HeaderFieldRowViewModel> _headerRows = new();

    public SocketEditWindow(SocketProfile source)
    {
        InitializeComponent();

        _profileId = source.Id;

        cmbRole.ItemsSource = Enum.GetValues(typeof(SocketRole));
        cmbProtocol.ItemsSource = Enum.GetValues(typeof(TransportProtocol));
        cmbSendEndian.ItemsSource = Enum.GetValues(typeof(ByteOrder));
        cmbRecvEndian.ItemsSource = Enum.GetValues(typeof(ByteOrder));

        txtName.Text = source.Name;
        cmbRole.SelectedItem = source.Role;
        cmbProtocol.SelectedItem = source.Protocol;
        txtLocalIp.Text = source.LocalIp;
        txtLocalPort.Text = source.LocalPort.ToString(CultureInfo.InvariantCulture);
        txtRemoteIp.Text = source.RemoteIp;
        txtRemotePort.Text = source.RemotePort.ToString(CultureInfo.InvariantCulture);
        chkSendEnabled.IsChecked = source.SendEnabled;
        txtMessageSize.Text = source.MessageSize.ToString(CultureInfo.InvariantCulture);
        txtBandwidth.Text = source.TargetBandwidthBytesPerSec.ToString(CultureInfo.InvariantCulture);
        cmbSendEndian.SelectedItem = source.SendByteOrder;
        cmbRecvEndian.SelectedItem = source.ReceiveByteOrder;

        foreach (var field in source.Header.Fields)
            _headerRows.Add(HeaderFieldRowViewModel.FromDefinition(field));

        itemsHeaderFields.ItemsSource = _headerRows;
        _headerRows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (HeaderFieldRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnHeaderRowChanged;
            RecomputeHeaderTotal();
        };
        foreach (var row in _headerRows)
            row.PropertyChanged += OnHeaderRowChanged;

        RecomputeHeaderTotal();
    }

    // Parameterless constructor required by the Avalonia XAML loader/previewer.
    public SocketEditWindow() : this(new SocketProfile()) { }

    private void OnHeaderRowChanged(object? sender, PropertyChangedEventArgs e) => RecomputeHeaderTotal();

    private void RecomputeHeaderTotal()
    {
        int total = _headerRows.Sum(r => r.Type == HeaderFieldType.Bytes ? r.Size : HeaderFieldDefinition.FixedSizeFor(r.Type));
        txtHeaderTotal.Text = $"헤더 합계: {total} / {HeaderDefinition.TotalSize} bytes" + (total == HeaderDefinition.TotalSize ? "  (OK)" : "  (20바이트가 되어야 합니다)");
        txtHeaderTotal.Foreground = total == HeaderDefinition.TotalSize ? Brushes.Green : Brushes.Red;
    }

    private void BtnAddField_Click(object? sender, RoutedEventArgs e)
    {
        _headerRows.Add(new HeaderFieldRowViewModel { Name = $"field{_headerRows.Count + 1}", Type = HeaderFieldType.UInt8 });
    }

    private void BtnRemoveHeaderRow_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is HeaderFieldRowViewModel row)
            _headerRows.Remove(row);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private async void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var profile = new SocketProfile
            {
                Id = _profileId,
                Name = txtName.Text?.Trim() ?? "",
                Role = (SocketRole)cmbRole.SelectedItem!,
                Protocol = (TransportProtocol)cmbProtocol.SelectedItem!,
                LocalIp = txtLocalIp.Text?.Trim() ?? "",
                LocalPort = ParseInt(txtLocalPort.Text, "Local Port"),
                RemoteIp = txtRemoteIp.Text?.Trim() ?? "",
                RemotePort = ParseInt(txtRemotePort.Text, "Remote Port"),
                SendEnabled = chkSendEnabled.IsChecked == true,
                MessageSize = ParseInt(txtMessageSize.Text, "메시지 크기"),
                TargetBandwidthBytesPerSec = ParseLong(txtBandwidth.Text, "대역폭"),
                SendByteOrder = (ByteOrder)cmbSendEndian.SelectedItem!,
                ReceiveByteOrder = (ByteOrder)cmbRecvEndian.SelectedItem!,
                Header = new HeaderDefinition { Fields = _headerRows.Select(r => r.ToDefinition()).ToList() }
            };

            profile.Validate();
            Close(profile);
        }
        catch (Exception ex)
        {
            await SimpleMessageWindow.ShowAsync(this, ex.Message, "입력 오류");
        }
    }

    private static int ParseInt(string? text, string fieldLabel)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new FormatException($"'{fieldLabel}' 값이 올바른 정수가 아닙니다: {text}");
        return value;
    }

    private static long ParseLong(string? text, string fieldLabel)
    {
        if (!long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            throw new FormatException($"'{fieldLabel}' 값이 올바른 숫자가 아닙니다: {text}");
        return value;
    }
}
