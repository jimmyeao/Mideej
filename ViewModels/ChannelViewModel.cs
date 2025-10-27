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
    private float _peakLevel;

    [ObservableProperty]
    private string _color = "#3B82F6";

    [ObservableProperty]
    private bool _isInMappingMode;

    [ObservableProperty]
    private FilterConfiguration? _filter;

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
    private void EnterMappingMode()
    {
        IsInMappingMode = true;
    }

    [RelayCommand]
    private void AssignSession()
    {
        // Will be called when user wants to assign an audio session to this channel
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
    }
}
