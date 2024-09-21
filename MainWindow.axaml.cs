using Avalonia.Controls;
using Avalonia.Interactivity;
using Tmds.DBus.Protocol;

namespace YT2MP3Sharp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    public void DoDownload(object sender, RoutedEventArgs args)
    {
        message.Text = "Button Clicked!";
    }
}