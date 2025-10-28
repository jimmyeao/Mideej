using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Mideej.Models;

namespace Mideej;

/// <summary>
/// Session assignment dialog with multi-selection and grouping support
/// </summary>
public partial class SessionAssignmentDialog : Window, INotifyPropertyChanged
{
    private bool _isFocusedAppSelected;
    private bool _isUnmappedAppsSelected;

    public List<AudioSessionInfo> SelectedSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> AvailableSessions { get; }
    public ObservableCollection<AudioSessionInfo> MasterAndOutputSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> InputDeviceSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> ApplicationSessions { get; } = new();
    public HashSet<string> AlreadyMappedSessionIds { get; }

    public bool HasInputDevices => InputDeviceSessions.Count > 0;

    public bool IsFocusedAppSelected
    {
        get => _isFocusedAppSelected;
        set
        {
            if (_isFocusedAppSelected != value)
            {
                _isFocusedAppSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsUnmappedAppsSelected
    {
        get => _isUnmappedAppsSelected;
        set
        {
            if (_isUnmappedAppsSelected != value)
            {
                _isUnmappedAppsSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionAssignmentDialog(ObservableCollection<AudioSessionInfo> availableSessions, HashSet<string> alreadyMappedSessionIds)
    {
        InitializeComponent();
        AvailableSessions = availableSessions;
        AlreadyMappedSessionIds = alreadyMappedSessionIds;
        DataContext = this;

        GroupSessions();
    }

    private void GroupSessions()
    {
        MasterAndOutputSessions.Clear();
        InputDeviceSessions.Clear();
        ApplicationSessions.Clear();

        foreach (var session in AvailableSessions)
        {
            switch (session.SessionType)
            {
                case AudioSessionType.Output:
                    MasterAndOutputSessions.Add(session);
                    break;
                case AudioSessionType.Input:
                case AudioSessionType.Microphone:
                    InputDeviceSessions.Add(session);
                    break;
                case AudioSessionType.Application:
                case AudioSessionType.SystemSounds:
                    ApplicationSessions.Add(session);
                    break;
            }
        }

        OnPropertyChanged(nameof(HasInputDevices));
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

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        // Collect all selected sessions
        SelectedSessions.Clear();

        if (IsFocusedAppSelected)
        {
            // Add a special marker for focused app
            SelectedSessions.Add(new AudioSessionInfo
            {
                SessionId = "focused_app",
                DisplayName = "Focused App",
                ProcessName = "Auto-follow",
                SessionType = AudioSessionType.Application
            });
        }

        if (IsUnmappedAppsSelected)
        {
            // Add a special marker for unmapped applications
            SelectedSessions.Add(new AudioSessionInfo
            {
                SessionId = "unmapped_apps",
                DisplayName = "Unmapped Applications",
                ProcessName = "Dynamic",
                SessionType = AudioSessionType.Application
            });
        }

        // Add all checked sessions from all groups
        var allSessions = MasterAndOutputSessions
            .Concat(InputDeviceSessions)
            .Concat(ApplicationSessions);

        foreach (var session in allSessions.Where(s => s.IsSelected))
        {
            SelectedSessions.Add(session);
        }

        DialogResult = SelectedSessions.Count > 0;
        Close();
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
