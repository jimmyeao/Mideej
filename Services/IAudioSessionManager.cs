using Mideej.Models;

namespace Mideej.Services;

/// <summary>
/// Service for managing Windows audio sessions
/// </summary>
public interface IAudioSessionManager
{
    /// <summary>
    /// Event fired when audio sessions change (new session, session ended)
    /// </summary>
    event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;

    /// <summary>
    /// Event fired when a session's volume changes
    /// </summary>
    event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;

    /// <summary>
    /// Event fired when peak levels are updated (for VU meters)
    /// </summary>
    event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;

    /// <summary>
    /// Event fired when master volume mute state changes
    /// </summary>
    event EventHandler<MasterMuteChangedEventArgs>? MasterMuteChanged;

    /// <summary>
    /// Gets all active audio sessions
    /// </summary>
    List<AudioSessionInfo> GetActiveSessions();

    /// <summary>
    /// Sets the volume for a specific session
    /// </summary>
    void SetSessionVolume(string sessionId, float volume);

    /// <summary>
    /// Sets the mute state for a specific session
    /// </summary>
    void SetSessionMute(string sessionId, bool isMuted);

    /// <summary>
    /// Gets the current peak level for a session (for VU meter)
    /// </summary>
    float GetSessionPeakLevel(string sessionId);

    /// <summary>
    /// Starts monitoring audio sessions
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops monitoring audio sessions
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Sets the VU meter update interval in milliseconds
    /// </summary>
    void SetVuMeterUpdateInterval(int intervalMs);
}

/// <summary>
/// Event args for audio session changes
/// </summary>
public class AudioSessionChangedEventArgs : EventArgs
{
    public List<AudioSessionInfo> Sessions { get; set; } = new();
}

/// <summary>
/// Event args for session volume changes
/// </summary>
public class SessionVolumeChangedEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public float Volume { get; set; }
    public bool IsMuted { get; set; }
}

/// <summary>
/// Event args for peak level updates
/// </summary>
public class PeakLevelEventArgs : EventArgs
{
    public Dictionary<string, float> PeakLevels { get; set; } = new();
}

/// <summary>
/// Event args for master volume mute state changes
/// </summary>
public class MasterMuteChangedEventArgs : EventArgs
{
    public bool IsMuted { get; set; }
    public float Volume { get; set; }
}
