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
    /// Audio sessions assigned to this channel
    /// </summary>
    public ObservableCollection<AudioSessionInfo> AssignedSessions { get; } = new();

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
    private void RemoveSession(AudioSessionInfo session)
    {
        AssignedSessions.Remove(session);
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
    /// Creates a configuration object from this ViewModel
    /// </summary>
    public ChannelConfiguration ToConfiguration()
    {
        return new ChannelConfiguration
        {
            Index = Index,
            Name = Name,
            Volume = Volume,
            IsMuted = IsMuted,
            IsSoloed = IsSoloed,
            Color = Color,
            Filter = Filter,
            SessionType = SessionType,
            AssignedSessionIds = AssignedSessions.Select(s => s.SessionId).ToList()
        };
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
        // Note: AssignedSessionIds are loaded separately via RelinkSessions method
    }

    /// <summary>
    /// Relinks saved session IDs to actual AudioSessionInfo objects
    /// </summary>
    public void RelinkSessions(List<string> sessionIds, IEnumerable<AudioSessionInfo> availableSessions)
    {
        AssignedSessions.Clear();

        foreach (var sessionId in sessionIds)
        {
            var session = availableSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session != null)
            {
                AssignedSessions.Add(session);
            }
        }
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
