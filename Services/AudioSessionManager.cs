using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Mideej.Helpers;
using Mideej.Models;
using NAudio.CoreAudioApi;

namespace Mideej.Services;

/// <summary>
/// Simplified AudioSessionManager matching DeejNG's architecture exactly.
/// No device caching, no session caching for operations - always get fresh.
/// </summary>
public class AudioSessionManager : IAudioSessionManager, IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly HashSet<int> _systemProcessIds = new HashSet<int> { 0, 4, 8 };
    private DispatcherTimer? _sessionRefreshTimer;
    private DispatcherTimer? _peakLevelTimer;
    private bool _isMonitoring;
    private List<AudioSessionInfo> _cachedSessions = new();
    private Dictionary<string, MMDevice> _cachedDevices = new();
    private Dictionary<string, AudioSessionControl> _cachedAppSessions = new();
    
    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;
    public event EventHandler<MasterMuteChangedEventArgs>? MasterMuteChanged;

    public List<AudioSessionInfo> GetActiveSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        try
        {
            // Dispose old cached devices to prevent COM memory leak
            foreach (var device in _cachedDevices.Values)
            {
                try
                {
                    device?.Dispose();
                }
                catch { /* Ignore disposal errors */ }
            }
            _cachedDevices.Clear();

            // Clear application session cache (these don't need disposal, they're references)
            _cachedAppSessions.Clear();

            // Get default device fresh every time (like DeejNG)
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _cachedDevices["master_output"] = defaultDevice;

            // Master volume
            sessions.Add(new AudioSessionInfo
            {
                SessionId = "master_output",
                DisplayName = "Master Volume",
                ProcessName = "System",
                SessionType = AudioSessionType.Output,
                Volume = defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar,
                IsMuted = defaultDevice.AudioEndpointVolume.Mute,
                PeakLevel = defaultDevice.AudioMeterInformation.MasterPeakValue
            });

            // Add output devices
            var outputDevices = GetOutputDevices();
            sessions.AddRange(outputDevices);

            // Add input devices
            var inputDevices = GetInputDevices();
            sessions.AddRange(inputDevices);

            // Get all sessions
            var audioSessions = defaultDevice.AudioSessionManager.Sessions;
            var seenProcessIds = new HashSet<int>();

            for (int i = 0; i < audioSessions.Count; i++)
            {
                try
                {
                    var session = audioSessions[i];
                    int pid = (int)session.GetProcessID;

                    if (_systemProcessIds.Contains(pid) || pid < 100 || seenProcessIds.Contains(pid))
                        continue;

                    seenProcessIds.Add(pid);

                    string processName = ProcessNameCache.GetProcessName(pid);
                    if (string.IsNullOrEmpty(processName))
                        continue;

                    string sessionId = $"app_{processName}_{pid}";

                    // Cache the session reference for VU meter updates
                    _cachedAppSessions[sessionId] = session;

                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = sessionId,
                        DisplayName = session.DisplayName ?? processName,
                        ProcessName = processName,
                        ProcessId = pid,
                        SessionType = AudioSessionType.Application,
                        Volume = session.SimpleAudioVolume.Volume,
                        IsMuted = session.SimpleAudioVolume.Mute,
                        PeakLevel = session.AudioMeterInformation.MasterPeakValue
                    });
                }
                catch { /* Skip invalid sessions */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetActiveSessions] Error: {ex.Message}");
        }

        return sessions;
    }

    private List<AudioSessionInfo> GetOutputDevices()
    {
        var devices = new List<AudioSessionInfo>();

        try
        {
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in deviceCollection)
            {
                try
                {
                    string sessionId = $"output_{device.ID}";
                    _cachedDevices[sessionId] = device; // Cache device reference

                    devices.Add(new AudioSessionInfo
                    {
                        SessionId = sessionId,
                        DisplayName = device.FriendlyName,
                        ProcessName = "Output Device",
                        SessionType = AudioSessionType.Output,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        PeakLevel = device.AudioMeterInformation.MasterPeakValue
                    });
                }
                catch { /* Skip invalid device */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetOutputDevices] Error: {ex.Message}");
        }

        return devices;
    }

    private List<AudioSessionInfo> GetInputDevices()
    {
        var devices = new List<AudioSessionInfo>();

        try
        {
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            foreach (var device in deviceCollection)
            {
                try
                {
                    string sessionId = $"input_{device.ID}";
                    _cachedDevices[sessionId] = device; // Cache device reference

                    devices.Add(new AudioSessionInfo
                    {
                        SessionId = sessionId,
                        DisplayName = device.FriendlyName,
                        ProcessName = "Input Device",
                        SessionType = AudioSessionType.Input,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        PeakLevel = device.AudioMeterInformation.MasterPeakValue
                    });
                }
                catch { /* Skip invalid device */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetInputDevices] Error: {ex.Message}");
        }

        return devices;
    }

    public void SetSessionVolume(string sessionId, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);

        try
        {
            if (sessionId == "master_output")
            {
                var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return;
            }

            // Handle output device sessions
            if (sessionId.StartsWith("output_"))
            {
                string deviceId = sessionId.Substring(7); // Remove "output_" prefix
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                }
                return;
            }

            // Handle input device sessions
            if (sessionId.StartsWith("input_"))
            {
                string deviceId = sessionId.Substring(6); // Remove "input_" prefix
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                }
                return;
            }

            // Extract process name from sessionId (format: app_processname_pid)
            if (sessionId.StartsWith("app_"))
            {
                var parts = sessionId.Split('_');
                if (parts.Length >= 2)
                {
                    string processName = parts[1];
                    ApplyVolumeToTarget(processName, volume, false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetSessionVolume] Error: {ex.Message}");
        }
    }

    public void SetSessionMute(string sessionId, bool isMuted)
    {
        try
        {
            if (sessionId == "master_output")
            {
                var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle output device sessions
            if (sessionId.StartsWith("output_"))
            {
                string deviceId = sessionId.Substring(7);
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.Mute = isMuted;
                }
                return;
            }

            // Handle input device sessions
            if (sessionId.StartsWith("input_"))
            {
                string deviceId = sessionId.Substring(6);
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.Mute = isMuted;
                }
                return;
            }

            if (sessionId.StartsWith("app_"))
            {
                var parts = sessionId.Split('_');
                if (parts.Length >= 2)
                {
                    string processName = parts[1];
                    ApplyMuteToTarget(processName, isMuted);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetSessionMute] Error: {ex.Message}");
        }
    }

    public float GetSessionPeakLevel(string sessionId)
    {
        try
        {
            // Use cached device for performance (avoid re-enumerating every 50ms!)
            if (_cachedDevices.TryGetValue(sessionId, out var cachedDevice))
            {
                return cachedDevice.AudioMeterInformation.MasterPeakValue;
            }

            // Use cached application session for VU meters
            if (_cachedAppSessions.TryGetValue(sessionId, out var cachedSession))
            {
                return cachedSession.AudioMeterInformation.MasterPeakValue;
            }
        }
        catch { /* Ignore */ }

        return 0f;
    }

    /// <summary>
    /// DeejNG-style: Always get fresh sessions and match by process name
    /// </summary>
    private void ApplyVolumeToTarget(string executable, float level, bool isMuted)
    {
        try
        {
            level = Math.Clamp(level, 0f, 1f);
            string targetName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
            if (string.IsNullOrEmpty(targetName)) return;

            var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                    if (IsProcessNameMatch(cleanedProcName, targetName))
                    {
                        session.SimpleAudioVolume.Mute = isMuted;
                        if (!isMuted)
                            session.SimpleAudioVolume.Volume = level;
                    }
                }
                catch { /* Skip invalid session */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApplyVolumeToTarget] Error: {ex.Message}");
        }
    }

    private void ApplyMuteToTarget(string executable, bool isMuted)
    {
        try
        {
            string targetName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
            if (string.IsNullOrEmpty(targetName)) return;

            var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                    if (IsProcessNameMatch(cleanedProcName, targetName))
                        session.SimpleAudioVolume.Mute = isMuted;
                }
                catch { /* Skip invalid session */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApplyMuteToTarget] Error: {ex.Message}");
        }
    }

    private float GetPeakLevelForTarget(string executable)
    {
        try
        {
            string targetName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
            if (string.IsNullOrEmpty(targetName)) return 0f;

            var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                    if (IsProcessNameMatch(cleanedProcName, targetName))
                        return session.AudioMeterInformation.MasterPeakValue;
                }
                catch { /* Skip invalid session */ }
            }
        }
        catch { /* Ignore */ }

        return 0f;
    }

    private bool IsProcessNameMatch(string processName, string targetName)
    {
        if (string.IsNullOrEmpty(processName) || string.IsNullOrEmpty(targetName))
            return false;

        if (processName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (processName.Contains(targetName, StringComparison.OrdinalIgnoreCase) ||
            targetName.Contains(processName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;

        // Initial session fetch to populate cache
        try
        {
            _cachedSessions = GetActiveSessions();
            SessionsChanged?.Invoke(this, new AudioSessionChangedEventArgs { Sessions = _cachedSessions });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartMonitoring] Initial session fetch error: {ex.Message}");
        }

        // Poll for session changes every 2 seconds
        _sessionRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2000)
        };
        _sessionRefreshTimer.Tick += (s, e) =>
        {
            try
            {
                var sessions = GetActiveSessions();
                _cachedSessions = sessions; // Cache for peak level updates
                SessionsChanged?.Invoke(this, new AudioSessionChangedEventArgs { Sessions = sessions });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionRefresh] Error: {ex.Message}");
            }
        };
        _sessionRefreshTimer.Start();

        // Poll for peak levels every 50ms (use cached sessions for performance)
        _peakLevelTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _peakLevelTimer.Tick += (s, e) =>
        {
            try
            {
                // Update peak levels for cached sessions without re-enumerating devices
                var peaks = new Dictionary<string, float>();
                foreach (var session in _cachedSessions)
                {
                    // Update peak level from actual audio session
                    float peakLevel = GetSessionPeakLevel(session.SessionId);
                    peaks[session.SessionId] = peakLevel;
                }
                PeakLevelsUpdated?.Invoke(this, new PeakLevelEventArgs { PeakLevels = peaks });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PeakLevel] Error: {ex.Message}");
            }
        };
        _peakLevelTimer.Start();

        Debug.WriteLine("[AudioSessionManager] Monitoring started");
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        _sessionRefreshTimer?.Stop();
        _sessionRefreshTimer = null;

        _peakLevelTimer?.Stop();
        _peakLevelTimer = null;

        Debug.WriteLine("[AudioSessionManager] Monitoring stopped");
    }

    public void SetVuMeterUpdateInterval(int intervalMs)
    {
        if (_peakLevelTimer != null)
        {
            _peakLevelTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 10, 100));
        }
    }

    /// <summary>
    /// Gets the session ID of the current default playback device
    /// </summary>
    public string? GetDefaultPlaybackDeviceId()
    {
        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return $"output_{defaultDevice.ID}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetDefaultPlaybackDeviceId] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the session ID of the current default recording device
    /// </summary>
    public string? GetDefaultRecordingDeviceId()
    {
        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return $"input_{defaultDevice.ID}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetDefaultRecordingDeviceId] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sets the default playback device by session ID
    /// </summary>
    public bool SetDefaultPlaybackDevice(string sessionId)
    {
        try
        {
            if (!sessionId.StartsWith("output_"))
            {
                Debug.WriteLine($"[SetDefaultPlaybackDevice] Invalid session ID: {sessionId}");
                return false;
            }

            string deviceId = sessionId.Substring(7);
            var device = _deviceEnumerator.GetDevice(deviceId);

            if (device == null)
            {
                Debug.WriteLine($"[SetDefaultPlaybackDevice] Device not found: {deviceId}");
                return false;
            }

            // Set as default for multimedia
            var policyConfig = new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
            policyConfig.SetDefaultEndpoint(deviceId, Role.Console);

            Debug.WriteLine($"[SetDefaultPlaybackDevice] Set default playback to: {device.FriendlyName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetDefaultPlaybackDevice] Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets the default recording device by session ID
    /// </summary>
    public bool SetDefaultRecordingDevice(string sessionId)
    {
        try
        {
            if (!sessionId.StartsWith("input_"))
            {
                Debug.WriteLine($"[SetDefaultRecordingDevice] Invalid session ID: {sessionId}");
                return false;
            }

            string deviceId = sessionId.Substring(6);
            var device = _deviceEnumerator.GetDevice(deviceId);

            if (device == null)
            {
                Debug.WriteLine($"[SetDefaultRecordingDevice] Device not found: {deviceId}");
                return false;
            }

            // Set as default for communications and multimedia
            var policyConfig = new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, Role.Communications);
            policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);

            Debug.WriteLine($"[SetDefaultRecordingDevice] Set default recording to: {device.FriendlyName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetDefaultRecordingDevice] Error: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        StopMonitoring();

        // Dispose all cached devices
        foreach (var device in _cachedDevices.Values)
        {
            try
            {
                device?.Dispose();
            }
            catch { /* Ignore disposal errors */ }
        }
        _cachedDevices.Clear();

        // Clear application session cache
        _cachedAppSessions.Clear();

        _deviceEnumerator?.Dispose();
    }
}
