using System.Windows;

namespace RevitCortex.Plugin.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        ContentFrame.Navigate(new GeneralSettingsPage());
    }

    private void General_Click(object sender, RoutedEventArgs e)
        => ContentFrame.Navigate(new GeneralSettingsPage());

    private void Tools_Click(object sender, RoutedEventArgs e)
        => ContentFrame.Navigate(new ToolsSettingsPage());
}
