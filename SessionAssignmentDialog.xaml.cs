using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Mideej.Models;

namespace Mideej;

/// <summary>
/// Session assignment dialog
/// </summary>
public partial class SessionAssignmentDialog : Window
{
    public AudioSessionInfo? SelectedSession { get; private set; }

    public ObservableCollection<AudioSessionInfo> AvailableSessions { get; }

    public SessionAssignmentDialog(ObservableCollection<AudioSessionInfo> availableSessions)
    {
        InitializeComponent();
        AvailableSessions = availableSessions;
        DataContext = this;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SessionItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is AudioSessionInfo session)
        {
            SelectedSession = session;
            DialogResult = true;
            Close();
        }
    }
}
