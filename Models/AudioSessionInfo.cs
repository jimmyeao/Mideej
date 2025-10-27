namespace Mideej.Models;

/// <summary>
/// Represents information about an audio session
/// </summary>
public class AudioSessionInfo
{
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
