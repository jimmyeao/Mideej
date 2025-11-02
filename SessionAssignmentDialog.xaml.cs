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
    private string _manualApplicationName = string.Empty;

    public List<AudioSessionInfo> SelectedSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> AvailableSessions { get; }
    public ObservableCollection<AudioSessionInfo> MasterAndOutputSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> InputDeviceSessions { get; } = new();
    public ObservableCollection<AudioSessionInfo> ApplicationSessions { get; } = new();
    public HashSet<string> AlreadyMappedSessionIds { get; }

    private readonly HashSet<string> _preselectedSessionIds;

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

    public string ManualApplicationName
    {
        get => _manualApplicationName;
        set
        {
            if (_manualApplicationName != value)
            {
                _manualApplicationName = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionAssignmentDialog(ObservableCollection<AudioSessionInfo> availableSessions, HashSet<string> alreadyMappedSessionIds, IEnumerable<string> preselectedSessionIds)
    {
        InitializeComponent();
        AvailableSessions = availableSessions;
        AlreadyMappedSessionIds = alreadyMappedSessionIds;
        _preselectedSessionIds = new HashSet<string>(preselectedSessionIds);
        DataContext = this;

        GroupSessions();
        ResetSelections();
        ApplyPreselection();
    }

    private void GroupSessions()
    {
        MasterAndOutputSessions.Clear();
        InputDeviceSessions.Clear();
        ApplicationSessions.Clear();

        // DeejNG-style: Deduplicate by process name (show only one entry per app)
        var seenProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in AvailableSessions.OrderBy(s => s.DisplayName))
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
                    // DeejNG deduplication: Only show first occurrence of each process name
                    string processName = System.IO.Path.GetFileNameWithoutExtension(session.ProcessName).ToLowerInvariant();

                    if (!seenProcesses.Contains(processName))
                    {
                        seenProcesses.Add(processName);
                        ApplicationSessions.Add(session);
                    }
                    // Silently skip duplicate process names (like DeejNG does)
                    break;
            }
        }

        OnPropertyChanged(nameof(HasInputDevices));
    }

    private void ResetSelections()
    {
        foreach (var s in AvailableSessions)
        {
            s.IsSelected = false;
        }
    }

    private void ApplyPreselection()
    {
        foreach (var s in AvailableSessions)
        {
            // Preselect only sessions that belong to the channel being edited
            if (_preselectedSessionIds.Contains(s.SessionId))
            {
                s.IsSelected = true;
            }
        }

        // Ensure sessions already mapped to other channels are not preselected inadvertently
        foreach (var s in AvailableSessions)
        {
            if (AlreadyMappedSessionIds.Contains(s.SessionId) && !_preselectedSessionIds.Contains(s.SessionId))
            {
                s.IsSelected = false;
            }
        }
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

        // Handle manual application name entry
        if (!string.IsNullOrWhiteSpace(ManualApplicationName))
        {
            var cleanName = ManualApplicationName.Trim();
            SelectedSessions.Add(new AudioSessionInfo
            {
                SessionId = $"manual_{cleanName}",
                DisplayName = cleanName,
                ProcessName = cleanName,
                SessionType = AudioSessionType.Application
            });
        }

        // Add all checked sessions
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
