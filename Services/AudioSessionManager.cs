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
/// Tracks invalid session state with retry logic and auto-expiration
/// </summary>
internal class InvalidSessionInfo
{
    public int FailureCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime FirstFailureTime { get; set; }

    public InvalidSessionInfo()
    {
        FailureCount = 1;
        FirstFailureTime = DateTime.Now;
        LastFailureTime = DateTime.Now;
    }

    /// <summary>
    /// Check if this session should be blacklisted (3+ consecutive failures)
    /// </summary>
    public bool ShouldBlacklist => FailureCount >= 3;

    /// <summary>
    /// Check if this tracking info has expired (5 seconds since first failure)
    /// Sessions are given a fresh chance after 5 seconds
    /// </summary>
    public bool IsExpired => (DateTime.Now - FirstFailureTime).TotalSeconds >= 5;

    public void IncrementFailure()
    {
        FailureCount++;
        LastFailureTime = DateTime.Now;
    }
}

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
    private MMDevice? _cachedDefaultDevice = null; // Cache the default device like DeejNG
    private SessionCollection? _cachedSessionCollection = null; // Cache the SessionCollection like DeejNG (refreshed every 50ms)
    private DateTime _lastSessionCollectionRefresh = DateTime.MinValue;
    private Dictionary<string, (WasapiCapture Capture, MMDevice Device)> _inputDeviceCaptures = new();
    private Dictionary<string, InvalidSessionInfo> _invalidSessions = new(); // Track invalid sessions by instance ID with retry logic and auto-expiration
    private int _sessionCollectionFailureCount = 0; // Track consecutive failures for resilient error handling
    
    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;
    public event EventHandler<MasterMuteChangedEventArgs>? MasterMuteChanged;

    public List<AudioSessionInfo> GetActiveSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        try
        {
            // Clear old cached devices - let .NET handle disposal naturally
            // Do NOT call Dispose() on devices that are still active in the system
            _cachedDevices.Clear();

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
                string? instanceId = null;

                try
                {
                    session = audioSessions[i];
                    if (session == null)
                        continue;

                    // Try to get instance identifier first (for tracking purposes)
                    try
                    {
                        instanceId = session.GetSessionInstanceIdentifier;
                    }
                    catch
                    {
                        // If we can't get instance ID, use index as fallback
                        instanceId = $"session_index_{i}";
                    }

                    // Check if this session is in the invalid list
                    if (_invalidSessions.TryGetValue(instanceId, out var invalidInfo))
                    {
                        // Check if the entry has expired (auto-retry after 5 seconds)
                        if (invalidInfo.IsExpired)
                        {
                            // Give it another chance
                            _invalidSessions.Remove(instanceId);
                            Debug.WriteLine($"[GetActiveSessions] Retry expired blacklist entry: {instanceId}");
                        }
                        else if (invalidInfo.ShouldBlacklist)
                        {
                            // Skip this session - it's had 3+ failures and hasn't expired yet
                            // Log if this is a Spotify session being skipped
                            if (instanceId.ToLowerInvariant().Contains("spotify"))
                            {
                                Debug.WriteLine($"[GetActiveSessions] BLACKLISTED Spotify session: {instanceId}");
                            }
                            continue;
                        }
                        // Otherwise, let it through (1-2 failures, not blacklisted yet)
                    }

                    // Try to get PID - this will throw ArgumentException if session is invalid/disconnected
                    pid = (int)session.GetProcessID;
                }
                catch (ArgumentException)
                {
                    // Session is in invalid/disconnected state, track the failure
                    if (instanceId != null)
                    {
                        TrackSessionFailure(instanceId, "ArgumentException");
                    }
                    continue;
                }
                catch (Exception ex)
                {
                    // Log unexpected errors and track failure
                    Debug.WriteLine($"[GetActiveSessions] Unexpected error reading session {i}: {ex.GetType().Name} - {ex.Message}");
                    if (instanceId != null)
                    {
                        TrackSessionFailure(instanceId, ex.GetType().Name);
                    }
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

                    // instanceId was already retrieved above, reuse it
                    string sessionId = $"app_{processName}_{pid}_{instanceId}";

                    // Skip if we've already seen this exact session
                    if (seenSessionIds.Contains(sessionId))
                        continue;

                    seenSessionIds.Add(sessionId);

                    // Session is valid - clear it from invalid tracking if it was there
                    if (instanceId != null && _invalidSessions.ContainsKey(instanceId))
                    {
                        _invalidSessions.Remove(instanceId);
                        Debug.WriteLine($"[GetActiveSessions] Session recovered: {instanceId} ({processName})");
                    }

                    // Don't cache individual AudioSessionControl objects - they become stale
                    // DeejNG approach: cache the SessionCollection and re-find sessions each time

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

                    // Log Spotify sessions specifically
                    if (processName.ToLowerInvariant().Contains("spotify"))
                    {
                        Debug.WriteLine($"[GetActiveSessions] : PID={pid}, DisplayName='{displayName}', SessionId={sessionId}");
                    }

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

                    // Cache cleaned process name for fast VU meter lookups
                    string cleanedProcessName = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();

                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = sessionId,
                        DisplayName = displayName,
                        ProcessName = processName,
                        ProcessId = pid,
                        SessionType = AudioSessionType.Application,
                        Volume = volume,
                        IsMuted = isMuted,
                        PeakLevel = peakLevel,
                        CleanedProcessName = cleanedProcessName
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
                finally
                {
                    // Dispose COM object to prevent memory leak
                    session?.Dispose();
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

    public float GetSessionPeakLevel(string sessionId, string? cleanedProcessName = null)
    {
        try
        {
            // Handle master output - use cached device to avoid repeated enumeration
            if (sessionId == "master_output")
            {
                try
                {
                    // Cache the default device to avoid creating/disposing it every 30ms
                    if (_cachedDefaultDevice == null)
                    {
                        _cachedDefaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    return _cachedDefaultDevice.AudioMeterInformation.MasterPeakValue;
                }
                catch
                {
                    // Device might be stale, clear cache and try once more
                    _cachedDefaultDevice?.Dispose();
                    _cachedDefaultDevice = null;
                    try
                    {
                        _cachedDefaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        return _cachedDefaultDevice.AudioMeterInformation.MasterPeakValue;
                    }
                    catch
                    {
                        return 0f;
                    }
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

            // CRITICAL FIX: Search ALL devices, not just default device
            // Apps can route audio to different outputs (headphones, virtual devices, etc.)
            // This was the root cause of intermittent VU meter failures
            try
            {
                // Extract process name from sessionId (format: app_{processName}_{pid}_{instanceId})
                if (!sessionId.StartsWith("app_"))
                    return 0f;

                // OPTIMIZATION: Use cached cleanedProcessName if provided to avoid string parsing (30ms interval)
                string cleanedTargetName;
                if (!string.IsNullOrEmpty(cleanedProcessName))
                {
                    cleanedTargetName = cleanedProcessName;
                }
                else
                {
                    // Fallback: parse from sessionId if cleanedProcessName not provided
                    var parts = sessionId.Split('_', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        return 0f;

                    string processName = parts[1];
                    cleanedTargetName = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
                }

                // Enumerate ALL output devices ONCE and cache the collection
                var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                // Search through all devices and their sessions
                for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
                {
                    MMDevice? device = null;
                    try
                    {
                        device = devices[deviceIndex];
                        if (device == null) continue;

                        var sessions = device.AudioSessionManager.Sessions;
                        if (sessions == null) continue;

                        // Search through all sessions on this device
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            AudioSessionControl? session = null;
                            try
                            {
                                session = sessions[i];
                                if (session == null) continue;

                                int pid = (int)session.GetProcessID;

                                if (pid < 100)
                                {
                                    continue;
                                }

                                string procName = ProcessNameCache.GetProcessName(pid);
                                if (string.IsNullOrEmpty(procName))
                                {
                                    continue;
                                }

                                string sessionProcessName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                                // Fuzzy matching like DeejNG
                                bool isMatch = sessionProcessName.Equals(cleanedTargetName, StringComparison.OrdinalIgnoreCase) ||
                                               sessionProcessName.Contains(cleanedTargetName, StringComparison.OrdinalIgnoreCase) ||
                                               cleanedTargetName.Contains(sessionProcessName, StringComparison.OrdinalIgnoreCase);

                                if (isMatch)
                                {
                                    return session.AudioMeterInformation.MasterPeakValue;
                                }
                            }
                            catch (ArgumentException) { continue; }
                            catch { continue; }
                            finally
                            {
                                // Dispose COM object to prevent memory leak
                                session?.Dispose();
                            }
                        }
                    }
                    catch { continue; }
                    finally
                    {
                        // Don't dispose - the collection owns these references
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetSessionPeakLevel] Error searching devices: {ex.Message}");
            }

            return 0f;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetSessionPeakLevel] Unexpected error for {sessionId}: {ex.GetType().Name} - {ex.Message}");
        }

        return 0f;
    }

    /// <summary>
    /// Fallback method to get peak level by re-enumerating sessions.
    /// Used when cached session becomes invalid (e.g., after long pause).
    /// Does NOT re-cache the session - that will happen on the next GetActiveSessions() call (every 2 seconds).
    /// </summary>
    private float GetPeakLevelByEnumeration(string sessionId)
    {
        try
        {
            if (!sessionId.StartsWith("app_"))
                return 0f;

            var parts = sessionId.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) // Need at least: app, processname, pid
                return 0f;

            string processName = parts[1];
            if (string.IsNullOrWhiteSpace(processName))
                return 0f;

            // Process name from cache is already lowercase without extension
            string targetName = processName.ToLowerInvariant();

            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl? session = null;
                try
                {
                    session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    if (string.IsNullOrWhiteSpace(procName))
                        continue;

                    // ProcessNameCache already returns lowercase without extension
                    if (IsProcessNameMatch(procName, targetName))
                    {
                        // Found the session - return peak level
                        // Don't re-cache here as the device is being disposed
                        // The session will be properly re-cached on the next GetActiveSessions() call
                        var peak = session.AudioMeterInformation.MasterPeakValue;
                        Debug.WriteLine($"[GetPeakLevelByEnumeration] Found session {procName}, peak: {peak}");
                        return peak;
                    }
                }
                catch (Exception innerEx)
                {
                    Debug.WriteLine($"[GetPeakLevelByEnumeration] Inner error: {innerEx.Message}");
                }
                finally
                {
                    // Dispose COM object to prevent memory leak
                    session?.Dispose();
                }
            }

            Debug.WriteLine($"[GetPeakLevelByEnumeration] No matching session found for {targetName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetPeakLevelByEnumeration] Error for {sessionId}: {ex.Message}");
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
                AudioSessionControl? session = null;
                try
                {
                    session = sessions[i];
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
                finally
                {
                    // Dispose COM object to prevent memory leak
                    session?.Dispose();
                }
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
                AudioSessionControl? session = null;
                try
                {
                    session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                    if (IsProcessNameMatch(cleanedProcName, targetName))
                    {
                        session.SimpleAudioVolume.Mute = isMuted;
                    }
                }
                catch { /* Skip invalid session */ }
                finally
                {
                    // Dispose COM object to prevent memory leak
                    session?.Dispose();
                }
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
                AudioSessionControl? session = null;
                try
                {
                    session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid < 100) continue;

                    string procName = ProcessNameCache.GetProcessName(pid);
                    string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();

                    if (IsProcessNameMatch(cleanedProcName, targetName))
                        return session.AudioMeterInformation.MasterPeakValue;
                }
                catch { /* Skip invalid session */ }
                finally
                {
                    // Dispose COM object to prevent memory leak
                    session?.Dispose();
                }
            }
        }
        catch { /* Ignore */ }

        return 0f;
    }

    private bool IsProcessNameMatch(string processName, string targetName)
    {
        if (string.IsNullOrEmpty(processName) || string.IsNullOrEmpty(targetName))
            return false;

        // Exact match only - different executables are separate sessions
        // msedge != msedgewebview2, chrome != chrome_helper, etc.
        return processName.Equals(targetName, StringComparison.OrdinalIgnoreCase);
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;

        // Clear invalid session blacklist on start
        _invalidSessions.Clear();

        // Reset session collection failure counter
        _sessionCollectionFailureCount = 0;

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
                // Clean up expired invalid session entries (auto-expire after 5 seconds)
                // This gives sessions with temporary failures a fresh chance
                CleanupExpiredInvalidSessions();

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
                // DeejNG approach: Enumerate sessions ONCE per tick, not once per cached session
                var peaks = new Dictionary<string, float>();

                // Get default device - same as volume control uses
                using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;

                // Build a process name -> peak level lookup table
                var processPeaks = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sessions.Count; i++)
                {
                    AudioSessionControl? session = null;
                    try
                    {
                        session = sessions[i];
                        int pid = (int)session.GetProcessID;
                        if (pid < 100) continue;

                        string procName = ProcessNameCache.GetProcessName(pid);
                        if (string.IsNullOrEmpty(procName)) continue;

                        string cleanedProcName = Path.GetFileNameWithoutExtension(procName).ToLowerInvariant();
                        float peak = session.AudioMeterInformation.MasterPeakValue;

                        // Keep the highest peak for this process name
                        if (!processPeaks.ContainsKey(cleanedProcName) || peak > processPeaks[cleanedProcName])
                        {
                            processPeaks[cleanedProcName] = peak;
                        }
                    }
                    catch (ArgumentException) { }
                    catch { }
                    finally
                    {
                        // Dispose COM object to prevent memory leak
                        session?.Dispose();
                    }
                }

                // Map cached sessions to their peak levels using fuzzy matching
                foreach (var cachedSession in _cachedSessions)
                {
                    float peak = 0f;

                    if (cachedSession.SessionId == "master_output")
                    {
                        // Master output - get from device
                        peak = device.AudioMeterInformation.MasterPeakValue;
                    }
                    else if (cachedSession.SessionId.StartsWith("input_"))
                    {
                        // Input devices handled separately
                        peak = GetSessionPeakLevel(cachedSession.SessionId, cachedSession.CleanedProcessName);
                    }
                    else if (cachedSession.SessionId.StartsWith("output_"))
                    {
                        // Output devices handled separately
                        peak = GetSessionPeakLevel(cachedSession.SessionId, cachedSession.CleanedProcessName);
                    }
                    else if (cachedSession.CleanedProcessName != null)
                    {
                        // Application session - use fuzzy matching against processPeaks
                        string targetName = cachedSession.CleanedProcessName;

                        // Try exact match first
                        if (processPeaks.TryGetValue(targetName, out float exactPeak))
                        {
                            peak = exactPeak;
                        }
                        // No else - if no exact match, peak stays at 0
                        // Different executables (msedge vs msedgewebview2) are separate sessions
                    }

                    peaks[cachedSession.SessionId] = peak;
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

    /// <summary>
    /// Track a session failure for the blacklist system with retry logic
    /// </summary>
    private void TrackSessionFailure(string instanceId, string errorType)
    {
        // Log prominently if this is Spotify
        bool isSpotify = instanceId.ToLowerInvariant().Contains("spotify");

        if (_invalidSessions.TryGetValue(instanceId, out var info))
        {
            // Existing entry - increment failure count
            info.IncrementFailure();
            if (isSpotify)
            {
                Debug.WriteLine($"[TrackSessionFailure] *** SPOTIFY *** failure #{info.FailureCount} ({errorType}) - Instance: {instanceId}");
            }
            else
            {
                Debug.WriteLine($"[TrackSessionFailure] {instanceId} failure #{info.FailureCount} ({errorType})");
            }
        }
        else
        {
            // New entry
            _invalidSessions[instanceId] = new InvalidSessionInfo();
            if (isSpotify)
            {
                Debug.WriteLine($"[TrackSessionFailure] *** SPOTIFY *** first failure ({errorType}) - Instance: {instanceId}");
            }
            else
            {
                Debug.WriteLine($"[TrackSessionFailure] {instanceId} first failure ({errorType})");
            }
        }
    }

    /// <summary>
    /// Clean up expired entries from the invalid session tracking
    /// Expired entries are automatically removed so sessions get a fresh chance
    /// </summary>
    private void CleanupExpiredInvalidSessions()
    {
        var expiredKeys = _invalidSessions
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _invalidSessions.Remove(key);
        }

        if (expiredKeys.Count > 0)
        {
            Debug.WriteLine($"[CleanupExpiredInvalidSessions] Removed {expiredKeys.Count} expired entries");
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

        // Clear cached devices - let .NET GC handle cleanup
        _cachedDevices.Clear();

        // Clear cached SessionCollection and device - DO NOT use FinalReleaseComObject!
        // Let the .NET garbage collector handle COM cleanup naturally.
        // FinalReleaseComObject can break the Windows Audio system.
        _cachedSessionCollection = null;
        _cachedDefaultDevice = null;

        _deviceEnumerator?.Dispose();

        // Force garbage collection to clean up COM objects
        GC.Collect();
        GC.WaitForPendingFinalizers();
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
