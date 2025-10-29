using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;

namespace Mideej.ViewModels;

/// <summary>
/// ViewModel for a single audio channel
/// </summary>
public partial class ChannelViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name = "Channel";

    [ObservableProperty]
    private float _volume = 1.0f;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSoloed;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private float _peakLevel;

    [ObservableProperty]
    private string _color = "#3B82F6";

    [ObservableProperty]
    private bool _isInMappingMode;

    [ObservableProperty]
    private FilterConfiguration? _filter;

    [ObservableProperty]
    private AudioSessionType? _sessionType;

    /// <summary>
    /// Audio sessions currently linked (live objects)
    /// </summary>
    public ObservableCollection<AudioSessionInfo> AssignedSessions { get; } = new();

    /// <summary>
    /// Stable intended assignments that should persist across restarts and session churn
    /// </summary>
    public List<SessionReference> IntendedAssignments { get; } = new();

    /// <summary>
    /// Event fired when volume changes (for applying to audio sessions)
    /// </summary>
    public event EventHandler? VolumeChanged;

    /// <summary>
    /// Event fired when mute state changes
    /// </summary>
    public event EventHandler? MuteChanged;

    /// <summary>
    /// Event fired when solo state changes
    /// </summary>
    public event EventHandler? SoloChanged;

    /// <summary>
    /// Event fired when user wants to enter mapping mode for this channel
    /// </summary>
    public event EventHandler<MappingTypeRequestedEventArgs>? MappingModeRequested;

    /// <summary>
    /// Event fired when record state changes
    /// </summary>
    public event EventHandler? RecordChanged;

    /// <summary>
    /// Event fired when select state changes
    /// </summary>
    public event EventHandler? SelectChanged;

    /// <summary>
    /// Event fired when user wants to assign a session to this channel
    /// </summary>
    public event EventHandler? SessionAssignmentRequested;

    /// <summary>
    /// Event fired when user clears all sessions from this channel
    /// </summary>
    public event EventHandler? SessionCleared;

    partial void OnVolumeChanged(float value)
    {
        // Clamp volume between 0 and 1
        if (value < 0) Volume = 0;
        if (value > 1) Volume = 1;

        // Notify listeners
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        MuteChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ToggleSolo()
    {
        IsSoloed = !IsSoloed;
        SoloChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EnterMappingMode(string controlType)
    {
        IsInMappingMode = true;
        var args = new MappingTypeRequestedEventArgs(controlType);
        MappingModeRequested?.Invoke(this, args);
    }

    [RelayCommand]
    public void ToggleRecord()
    {
        IsRecording = !IsRecording;
        RecordChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ToggleSelect()
    {
        IsSelected = !IsSelected;
        SelectChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AssignSession()
    {
        // Fire event so MainWindowViewModel can handle showing the dialog
        SessionAssignmentRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearSession()
    {
        // Clear all assigned sessions (live)
        AssignedSessions.Clear();
        IntendedAssignments.Clear();
        Name = $"Channel {Index + 1}";
        SessionType = null;

        // Fire event to notify MainWindowViewModel
        SessionCleared?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveSession(AudioSessionInfo session)
    {
        AssignedSessions.Remove(session);
        // Also remove from intended assignments to keep in sync
        IntendedAssignments.RemoveAll(r => r.SessionId == session.SessionId || string.Equals(r.DisplayName, session.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void ToggleFilter()
    {
        if (Filter == null)
        {
            Filter = new FilterConfiguration();
        }
        Filter.IsEnabled = !Filter.IsEnabled;
    }

    /// <summary>
    /// Event fired when user wants to cycle to the next session
    /// </summary>
    public event EventHandler? CycleSessionRequested;

    /// <summary>
    /// Trigger a cycle to the next available session
    /// </summary>
    public void CycleSession()
    {
        CycleSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a configuration object from this ViewModel
    /// </summary>
    public ChannelConfiguration ToConfiguration()
    {
        var config = new ChannelConfiguration
        {
            Index = Index,
            Name = Name,
            Volume = Volume,
            IsMuted = IsMuted,
            IsSoloed = IsSoloed,
            Color = Color,
            Filter = Filter,
            SessionType = SessionType,
        };

        // Persist intended assignments for stability
        config.AssignedSessions = IntendedAssignments.Select(r => new SessionReference
        {
            SessionId = r.SessionId,
            ProcessId = r.ProcessId,
            ProcessName = r.ProcessName,
            DisplayName = r.DisplayName,
            SessionType = r.SessionType,
            DeviceEndpointId = r.DeviceEndpointId
        }).ToList();

        // Legacy ids (best-effort)
        config.AssignedSessionIds = IntendedAssignments.Select(r => r.SessionId).Where(id => !string.IsNullOrEmpty(id))!.Select(id => id!).ToList();

        return config;
    }

    /// <summary>
    /// Loads configuration into this ViewModel
    /// </summary>
    public void LoadConfiguration(ChannelConfiguration config)
    {
        Index = config.Index;
        Name = config.Name;
        Volume = config.Volume;
        IsMuted = config.IsMuted;
        IsSoloed = config.IsSoloed;
        Color = config.Color;
        Filter = config.Filter;
        SessionType = config.SessionType;

        IntendedAssignments.Clear();
        foreach (var r in config.AssignedSessions)
        {
            IntendedAssignments.Add(new SessionReference
            {
                SessionId = r.SessionId,
                ProcessId = r.ProcessId,
                ProcessName = r.ProcessName,
                DisplayName = r.DisplayName,
                SessionType = r.SessionType,
                DeviceEndpointId = r.DeviceEndpointId
            });
        }

        // Fallback legacy ids
        if (IntendedAssignments.Count == 0)
        {
            foreach (var id in config.AssignedSessionIds)
            {
                IntendedAssignments.Add(new SessionReference { SessionId = id, SessionType = config.SessionType });
            }
        }
        // AssignedSessions are linked separately
    }

    /// <summary>
    /// Set intended assignments from selected sessions, keeping stable references
    /// </summary>
    public void SetIntendedAssignments(IEnumerable<AudioSessionInfo> sessions)
    {
        IntendedAssignments.Clear();
        foreach (var s in sessions)
        {
            var reference = new SessionReference
            {
                SessionId = s.SessionId,
                ProcessId = s.ProcessId,
                ProcessName = s.ProcessName,
                DisplayName = s.DisplayName,
                SessionType = s.SessionType
            };
            if (s.SessionId.StartsWith("input_") || s.SessionId.StartsWith("output_"))
            {
                var id = s.SessionId.Contains('_') ? s.SessionId[(s.SessionId.IndexOf('_') + 1)..] : s.SessionId;
                reference.DeviceEndpointId = id;
            }
            IntendedAssignments.Add(reference);
        }
    }

    /// <summary>
    /// Relinks runtime AssignedSessions from current IntendedAssignments against available sessions.
    /// Does not mutate IntendedAssignments when no match is found.
    /// </summary>
    public void RelinkFromIntended(IEnumerable<AudioSessionInfo> availableSessions)
    {
        AssignedSessions.Clear();

        foreach (var r in IntendedAssignments)
        {
            AudioSessionInfo? match = null;

            // 1. Exact id
            if (!string.IsNullOrEmpty(r.SessionId))
            {
                match = availableSessions.FirstOrDefault(s => s.SessionId == r.SessionId);
            }
            // 2. Endpoint id
            if (match == null && !string.IsNullOrEmpty(r.DeviceEndpointId))
            {
                var expectedInput = $"input_{r.DeviceEndpointId}";
                var expectedOutput = $"output_{r.DeviceEndpointId}";
                match = availableSessions.FirstOrDefault(s => s.SessionId == expectedInput || s.SessionId == expectedOutput);
            }
            // 3. ProcessId + type
            if (match == null && r.ProcessId.HasValue)
            {
                match = availableSessions.FirstOrDefault(s => s.ProcessId == r.ProcessId && (!r.SessionType.HasValue || s.SessionType == r.SessionType.Value));
            }
            // 4. ProcessName + type
            if (match == null && !string.IsNullOrWhiteSpace(r.ProcessName))
            {
                match = availableSessions.FirstOrDefault(s => string.Equals(s.ProcessName, r.ProcessName, StringComparison.OrdinalIgnoreCase) && (!r.SessionType.HasValue || s.SessionType == r.SessionType.Value));
            }
            // 5. DisplayName + type
            if (match == null && !string.IsNullOrWhiteSpace(r.DisplayName))
            {
                match = availableSessions.FirstOrDefault(s => string.Equals(s.DisplayName, r.DisplayName, StringComparison.OrdinalIgnoreCase) && (!r.SessionType.HasValue || s.SessionType == r.SessionType.Value));
            }

            if (match != null)
            {
                AssignedSessions.Add(match);
            }
        }
    }

    /// <summary>
    /// Legacy method: Relinks saved session IDs to actual AudioSessionInfo objects
    /// </summary>
    public void RelinkSessions(List<string> sessionIds, IEnumerable<AudioSessionInfo> availableSessions)
    {
        IntendedAssignments.Clear();
        foreach (var id in sessionIds)
        {
            IntendedAssignments.Add(new SessionReference { SessionId = id });
        }
        RelinkFromIntended(availableSessions);
    }

    /// <summary>
    /// Relinks using richer references from a configuration.
    /// </summary>
    public void RelinkSessions(ChannelConfiguration config, IEnumerable<AudioSessionInfo> availableSessions)
    {
        LoadConfiguration(config);
        RelinkFromIntended(availableSessions);
    }
}

/// <summary>
/// Event args for mapping mode requests with control type
/// </summary>
public class MappingTypeRequestedEventArgs : EventArgs
{
    public string ControlType { get; }

    public MappingTypeRequestedEventArgs(string controlType)
    {
        ControlType = controlType;
    }
}
