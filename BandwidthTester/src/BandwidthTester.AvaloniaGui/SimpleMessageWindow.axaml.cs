using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BandwidthTester.AvaloniaGui;

public partial class SimpleMessageWindow : Window
{
    public SimpleMessageWindow()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner, string message, string title = "알림")
    {
        var window = new SimpleMessageWindow { Title = title };
        window.FindControl<TextBlock>("MessageText")!.Text = message;
        return window.ShowDialog(owner);
    }
}
