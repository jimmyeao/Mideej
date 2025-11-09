using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Mideej.Models;

/// <summary>
/// Represents information about an audio session
/// </summary>
public class AudioSessionInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>
    /// Unique identifier for the session
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the application
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Process name
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Process ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Icon path for the application
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Current volume (0.0 to 1.0)
    /// </summary>
    public float Volume { get; set; } = 1f;

    /// <summary>
    /// Whether the session is muted
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Current peak audio level (0.0 to 1.0)
    /// </summary>
    public float PeakLevel { get; set; }

    /// <summary>
    /// Type of audio session
    /// </summary>
    public AudioSessionType SessionType { get; set; }

    /// <summary>
    /// Cached cleaned process name for fast VU meter lookups (avoids repeated string parsing)
    /// Format: lowercase, no extension (e.g., "chrome", "spotify")
    /// </summary>
    public string? CleanedProcessName { get; set; }

    /// <summary>
    /// Whether this session is selected in the UI
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Types of audio sessions
/// </summary>
public enum AudioSessionType
{
    Application,
    SystemSounds,
    Microphone,
    Output,
    Input
}
