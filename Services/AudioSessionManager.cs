using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Mideej.Helpers;
using Mideej.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Mideej.Services;

/// <summary>
/// AudioSessionManager matching DeejNG's architecture for volume/mute operations.
/// Session references are cached ONLY for VU meter readings (performance critical).
/// Volume and mute operations ALWAYS enumerate fresh sessions by process name.
/// This prevents issues with apps like Spotify that dynamically recreate sessions.
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
    private Dictionary<string, (WasapiCapture Capture, MMDevice Device)> _inputDeviceCaptures = new();
    private HashSet<AudioSessionControl> _invalidSessions = new(); // Track sessions that are invalid
    private int _refreshCount = 0; // Track refresh cycles for periodic cleanup
    
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

            // CRITICAL: Release COM objects for AudioSessionControl to prevent leaks
            // This prevents breaking other applications' VU meters
            foreach (var session in _cachedAppSessions.Values)
            {
                try
                {
                    if (session != null)
                        Marshal.FinalReleaseComObject(session);
                }
                catch { /* Ignore release errors */ }
            }
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
            var seenSessionIds = new HashSet<string>(); // Track unique sessions, not just PIDs

            for (int i = 0; i < audioSessions.Count; i++)
            {
                AudioSessionControl? session = null;
                int pid = -1;

                try
                {
                    session = audioSessions[i];
                    if (session == null)
                        continue;

                    // Skip sessions we've already identified as invalid
                    if (_invalidSessions.Contains(session))
                        continue;

                    // Try to get PID - this will throw ArgumentException if session is invalid/disconnected
                    pid = (int)session.GetProcessID;
                }
                catch (ArgumentException)
                {
                    // Session is in invalid/disconnected state, add to blacklist
                    if (session != null)
                        _invalidSessions.Add(session);
                    continue;
                }
                catch (Exception ex)
                {
                    // Log unexpected errors only and blacklist
                    Debug.WriteLine($"[GetActiveSessions] Unexpected error reading session {i}: {ex.GetType().Name} - {ex.Message}");
                    if (session != null)
                        _invalidSessions.Add(session);
                    continue;
                }

                // Continue processing valid session
                try
                {
                    if (_systemProcessIds.Contains(pid) || pid < 100)
                        continue;

                    string processName = ProcessNameCache.GetProcessName(pid);
                    if (string.IsNullOrEmpty(processName))
                        continue;

                    // Create unique sessionId using session instance identifier (like DeejNG)
                    // This handles cases where one process has multiple audio sessions (e.g., Chrome tabs)
                    string instanceId = "";
                    try
                    {
                        instanceId = session.GetSessionInstanceIdentifier;
                    }
                    catch
                    {
                        // Session might be invalid or disconnecting, use index as fallback
                        instanceId = i.ToString();
                    }

                    string sessionId = $"app_{processName}_{pid}_{instanceId}";

                    // Skip if we've already seen this exact session
                    if (seenSessionIds.Contains(sessionId))
                        continue;

                    seenSessionIds.Add(sessionId);

                    // Cache the session reference for VU meter updates and fast volume/mute changes
                    _cachedAppSessions[sessionId] = session;

                    // Extract friendly name, fallback to process name if empty
                    string displayName = "";
                    try
                    {
                        displayName = session.DisplayName;
                    }
                    catch
                    {
                        // Session might be invalid or disconnecting, will use process name fallback
                    }

                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = processName;

                    float volume = 0f, peakLevel = 0f;
                    bool isMuted = false;

                    try
                    {
                        volume = session.SimpleAudioVolume.Volume;
                        isMuted = session.SimpleAudioVolume.Mute;
                        peakLevel = session.AudioMeterInformation.MasterPeakValue;
                    }
                    catch (ArgumentException)
                    {
                        // Session is in invalid state, use defaults
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioProperties] Unexpected error for PID {pid} ({processName}): {ex.GetType().Name} - {ex.Message}");
                    }

                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = sessionId,
                        DisplayName = displayName,
                        ProcessName = processName,
                        ProcessId = pid,
                        SessionType = AudioSessionType.Application,
                        Volume = volume,
                        IsMuted = isMuted,
                        PeakLevel = peakLevel
                    });
                }
                catch (ArgumentException)
                {
                    // Session is in invalid state, skip it silently
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GetActiveSessions] Unexpected error for session {i}: {ex.GetType().Name} - {ex.Message}");
                }
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
                using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return;
            }

            // Handle output device sessions
            if (sessionId.StartsWith("output_"))
            {
                string deviceId = sessionId.Substring(7); // Remove "output_" prefix
                using var device = _deviceEnumerator.GetDevice(deviceId);
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
                using var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                }
                return;
            }

            // For application sessions: ALWAYS enumerate fresh sessions and match by process name
            // This is critical for apps like Spotify that dynamically recreate audio sessions
            // DO NOT use cached session references - they may be stale
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
                using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle output device sessions
            if (sessionId.StartsWith("output_"))
            {
                string deviceId = sessionId.Substring(7);
                using var device = _deviceEnumerator.GetDevice(deviceId);
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
                using var device = _deviceEnumerator.GetDevice(deviceId);
                if (device != null)
                {
                    device.AudioEndpointVolume.Mute = isMuted;
                }
                return;
            }

            // For application sessions: ALWAYS enumerate fresh sessions and match by process name
            // This is critical for apps like Spotify that dynamically recreate audio sessions
            // DO NOT use cached session references - they may be stale
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
            // Handle master output
            if (sessionId == "master_output")
            {
                try
                {
                    using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    return device.AudioMeterInformation.MasterPeakValue;
                }
                catch
                {
                    return 0f;
                }
            }

            // Handle input devices - use active capture session for monitoring
            if (sessionId.StartsWith("input_"))
            {
                try
                {
                    // Ensure we have an active capture session
                    EnsureInputDeviceCapture(sessionId);

                    // Try to get peak from active capture
                    if (_inputDeviceCaptures.TryGetValue(sessionId, out var captureInfo))
                    {
                        try
                        {
                            var peak = captureInfo.Device.AudioMeterInformation.MasterPeakValue;
                            return peak;
                        }
                        catch
                        {
                            // Capture might have failed, remove it
                            try
                            {
                                captureInfo.Capture?.StopRecording();
                                captureInfo.Capture?.Dispose();
                                captureInfo.Device?.Dispose();
                            }
                            catch { }
                            _inputDeviceCaptures.Remove(sessionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GetSessionPeakLevel] Input device error for {sessionId}: {ex.Message}");
                }
                return 0f;
            }

            // Handle output devices - get fresh device reference each time
            if (sessionId.StartsWith("output_"))
            {
                try
                {
                    var deviceId = sessionId["output_".Length..];
                    var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    foreach (var device in deviceCollection)
                    {
                        if (device.ID == deviceId)
                        {
                            var peak = device.AudioMeterInformation.MasterPeakValue;
                            device.Dispose();
                            return peak;
                        }
                        device.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GetSessionPeakLevel] Output device error for {sessionId}: {ex.Message}");
                }
                return 0f;
            }

            // Use cached application session for VU meters
            if (_cachedAppSessions.TryGetValue(sessionId, out var cachedSession))
            {
                if (cachedSession == null)
                {
                    _cachedAppSessions.Remove(sessionId);
                    return 0f;
                }

                try
                {
                    var meterInfo = cachedSession.AudioMeterInformation;
                    if (meterInfo == null)
                    {
                        _cachedAppSessions.Remove(sessionId);
                        return 0f;
                    }

                    return meterInfo.MasterPeakValue;
                }
                catch (ArgumentException)
                {
                    // Session is disconnected/invalid - remove from cache
                    _cachedAppSessions.Remove(sessionId);
                    return 0f;
                }
                catch (InvalidComObjectException)
                {
                    // COM object is invalid - remove from cache
                    _cachedAppSessions.Remove(sessionId);
                    return 0f;
                }
                catch
                {
                    // Any other error - remove from cache
                    _cachedAppSessions.Remove(sessionId);
                    return 0f;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetSessionPeakLevel] Unexpected error for {sessionId}: {ex.GetType().Name} - {ex.Message}");
        }

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

            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
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

            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
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

            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
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

        // Clear invalid session blacklist on start
        _invalidSessions.Clear();

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
                // Periodically clean the invalid session list (every 5 refresh cycles = ~10 seconds)
                // This allows us to retry sessions that might have become valid
                if (_refreshCount++ % 5 == 0)
                {
                    _invalidSessions.Clear();
                }

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

        // Stop all input device captures
        foreach (var (capture, device) in _inputDeviceCaptures.Values)
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();
                device?.Dispose();
            }
            catch { }
        }
        _inputDeviceCaptures.Clear();

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

        // Dispose input device captures
        foreach (var (capture, device) in _inputDeviceCaptures.Values)
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();
                device?.Dispose();
            }
            catch { /* Ignore disposal errors */ }
        }
        _inputDeviceCaptures.Clear();

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

        // CRITICAL: Release COM objects for AudioSessionControl
        foreach (var session in _cachedAppSessions.Values)
        {
            try
            {
                if (session != null)
                    Marshal.FinalReleaseComObject(session);
            }
            catch { /* Ignore release errors */ }
        }
        _cachedAppSessions.Clear();

        _deviceEnumerator?.Dispose();
    }

    /// <summary>
    /// Ensures an input device has an active capture session for monitoring levels
    /// </summary>
    private void EnsureInputDeviceCapture(string sessionId)
    {
        if (!sessionId.StartsWith("input_"))
            return;

        if (_inputDeviceCaptures.ContainsKey(sessionId))
            return; // Already capturing

        try
        {
            var deviceId = sessionId["input_".Length..];
            var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            foreach (var device in deviceCollection)
            {
                if (device.ID == deviceId)
                {
                    var capture = new WasapiCapture(device);
                    capture.DataAvailable += (s, e) => { /* Discard data, we only want meter */ };
                    capture.StartRecording();
                    _inputDeviceCaptures[sessionId] = (capture, device);
                    Debug.WriteLine($"[EnsureInputDeviceCapture] Started capture for {device.FriendlyName}");
                    // Don't dispose device - we need it for meter readings
                    return;
                }
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnsureInputDeviceCapture] Failed to start capture for {sessionId}: {ex.Message}");
        }
    }
}
