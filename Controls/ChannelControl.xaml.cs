using System.Windows.Controls;
using System.Windows;

namespace Mideej.Controls;

/// <summary>
/// Interaction logic for ChannelControl.xaml
/// </summary>
public partial class ChannelControl : UserControl
{
    public ChannelControl()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }

    private void MapButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
}
