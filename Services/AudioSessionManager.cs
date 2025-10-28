using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using Mideej.Helpers;
using Mideej.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Mideej.Services;

/// <summary>
/// Service for managing Windows audio sessions
/// </summary>
public class AudioSessionManager : IAudioSessionManager, IDisposable
{
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _defaultDevice;
    private MMDevice? _defaultInputDevice;
    private SessionCollection? _sessionCollection;
    private readonly ConcurrentDictionary<string, SessionWrapper> _sessions = new();
    private readonly Dictionary<string, MMDevice> _inputDevices = new();
    private readonly Dictionary<string, MMDevice> _outputDevices = new();
    private DispatcherTimer? _vuMeterTimer;
    private bool _isMonitoring;
    private int _vuMeterInterval = 30;
    private DateTime _lastSessionRefresh = DateTime.MinValue;
    private const int SessionRefreshIntervalMs = 2000; // Refresh every 2 seconds
    private const int SessionGraceRemovalMs = 15000; // Keep missing sessions for 15s to avoid UI churn

    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;

    private class SessionWrapper
    {
        public AudioSessionControl? Session { get; set; }
        public AudioSessionInfo Info { get; set; } = null!;
        public SimpleAudioVolume? VolumeControl { get; set; }
        public AudioMeterInformation? MeterInfo { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public List<AudioSessionInfo> GetActiveSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        try
        {
            // Add master output session
            if (_defaultDevice != null)
            {
                sessions.Add(new AudioSessionInfo
                {
                    SessionId = "master_output",
                    DisplayName = "Master Volume",
                    ProcessName = "System",
                    SessionType = AudioSessionType.Output,
                    Volume = _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar,
                    IsMuted = _defaultDevice.AudioEndpointVolume.Mute,
                    PeakLevel = _defaultDevice.AudioMeterInformation.MasterPeakValue
                });
            }

            // Refresh and add input devices (microphones)
            if (_deviceEnumerator != null)
            {
                try
                {
                    // Clear old cache and rebuild
                    _inputDevices.Clear();
                    var inputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                    foreach (var device in inputDevices)
                    {
                        var sessionId = $"input_{device.ID}";
                        sessions.Add(new AudioSessionInfo
                        {
                            SessionId = sessionId,
                            DisplayName = device.FriendlyName,
                            ProcessName = "Microphone",
                            SessionType = AudioSessionType.Input,
                            Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                            IsMuted = device.AudioEndpointVolume.Mute,
                            PeakLevel = device.AudioMeterInformation.MasterPeakValue
                        });

                        // Cache the device for later use
                        _inputDevices[sessionId] = device;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error enumerating input devices: {ex.Message}");
                }
            }

            // Refresh and add output devices (speakers/headphones)
            if (_deviceEnumerator != null)
            {
                try
                {
                    // Clear old cache and rebuild
                    _outputDevices.Clear();
                    var outputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    foreach (var device in outputDevices)
                    {
                        var sessionId = $"output_{device.ID}";
                        sessions.Add(new AudioSessionInfo
                        {
                            SessionId = sessionId,
                            DisplayName = device.FriendlyName,
                            ProcessName = "Audio Output",
                            SessionType = AudioSessionType.Output,
                            Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                            IsMuted = device.AudioEndpointVolume.Mute,
                            PeakLevel = device.AudioMeterInformation.MasterPeakValue
                        });

                        // Cache the device for later use
                        _outputDevices[sessionId] = device;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error enumerating output devices: {ex.Message}");
                }
            }

            // Add application sessions (kept across inactive periods)
            foreach (var wrapper in _sessions.Values)
            {
                if (wrapper.Info != null)
                {
                    sessions.Add(wrapper.Info);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting active sessions: {ex.Message}");
        }

        return sessions;
    }

    public void SetSessionVolume(string sessionId, float volume)
    {
        try
        {
            volume = Math.Clamp(volume, 0f, 1f);

            // Handle master output
            if (sessionId == "master_output" && _defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return;
            }

            // Handle input devices
            if (sessionId.StartsWith("input_") && _inputDevices.TryGetValue(sessionId, out var inputDevice))
            {
                inputDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return;
            }

            // Handle output devices
            if (sessionId.StartsWith("output_") && _outputDevices.TryGetValue(sessionId, out var outputDevice))
            {
                outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return;
            }

            // Handle application sessions
            if (_sessions.TryGetValue(sessionId, out var wrapper))
            {
                if (wrapper.VolumeControl != null)
                {
                    wrapper.VolumeControl.Volume = volume;
                    wrapper.Info.Volume = volume;
                    wrapper.LastSeen = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting session volume: {ex.Message}");
        }
    }

    public void SetSessionMute(string sessionId, bool isMuted)
    {
        try
        {
            // Handle master output
            if (sessionId == "master_output" && _defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle input devices
            if (sessionId.StartsWith("input_") && _inputDevices.TryGetValue(sessionId, out var inputDevice))
            {
                inputDevice.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle output devices
            if (sessionId.StartsWith("output_") && _outputDevices.TryGetValue(sessionId, out var outputDevice))
            {
                outputDevice.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle application sessions
            if (_sessions.TryGetValue(sessionId, out var wrapper))
            {
                if (wrapper.VolumeControl != null)
                {
                    wrapper.VolumeControl.Mute = isMuted;
                    wrapper.Info.IsMuted = isMuted;
                    wrapper.LastSeen = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting session mute: {ex.Message}");
        }
    }

    public float GetSessionPeakLevel(string sessionId)
    {
        try
        {
            // Handle master output
            if (sessionId == "master_output" && _defaultDevice != null)
            {
                return _defaultDevice.AudioMeterInformation.MasterPeakValue;
            }

            // Handle input devices
            if (sessionId.StartsWith("input_") && _inputDevices.TryGetValue(sessionId, out var inputDevice))
            {
                return inputDevice.AudioMeterInformation.MasterPeakValue;
            }

            // Handle output devices
            if (sessionId.StartsWith("output_") && _outputDevices.TryGetValue(sessionId, out var outputDevice))
            {
                return outputDevice.AudioMeterInformation.MasterPeakValue;
            }

            // Handle application sessions
            if (_sessions.TryGetValue(sessionId, out var wrapper))
            {
                if (wrapper.MeterInfo != null)
                {
                    return wrapper.MeterInfo.MasterPeakValue;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting peak level: {ex.Message}");
        }

        return 0f;
    }

    public void StartMonitoring()
    {
        if (_isMonitoring)
            return;

        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            RefreshSessions();

            // Start VU meter timer
            _vuMeterTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_vuMeterInterval)
            };
            _vuMeterTimer.Tick += VuMeterTimer_Tick;
            _vuMeterTimer.Start();

            _isMonitoring = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio session monitoring: {ex.Message}");
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;

        try
        {
            _vuMeterTimer?.Stop();
            _vuMeterTimer = null;

            CleanupSessions();

            _sessionCollection = null;

            _defaultDevice?.Dispose();
            _defaultDevice = null;

            _deviceEnumerator?.Dispose();
            _deviceEnumerator = null;

            _isMonitoring = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping audio session monitoring: {ex.Message}");
        }
    }

    public void SetVuMeterUpdateInterval(int intervalMs)
    {
        _vuMeterInterval = Math.Max(10, Math.Min(intervalMs, 100));

        if (_vuMeterTimer != null)
        {
            _vuMeterTimer.Interval = TimeSpan.FromMilliseconds(_vuMeterInterval);
        }
    }

    private void RefreshSessions()
    {
        try
        {
            if (_defaultDevice == null)
                return;

            // Get audio session manager
            var sessionManager = _defaultDevice.AudioSessionManager;
            _sessionCollection = sessionManager.Sessions;

            Console.WriteLine($"[RefreshSessions] Scanning for audio sessions... Found {_sessionCollection.Count} session(s) in collection");

            var currentSessionIds = new HashSet<string>();
            var newSessions = new List<string>();
            var removedSessions = new List<string>();

            for (int i = 0; i < _sessionCollection.Count; i++)
            {
                try
                {
                    var session = _sessionCollection[i];
                    var processId = (int)session.GetProcessID;
                    var sessionState = session.State;

                    Console.WriteLine($"[RefreshSessions] Session {i}: PID={processId}, State={sessionState}");

                    if (sessionState == AudioSessionState.AudioSessionStateExpired)
                    {
                        Console.WriteLine($"[RefreshSessions]   -> Skipped (expired)");
                        continue;
                    }

                    var sessionId = $"{processId}_{session.GetSessionIdentifier}";
                    currentSessionIds.Add(sessionId);

                    if (_sessions.ContainsKey(sessionId))
                    {
                        // Update existing session wrapper by id
                        var wrapper = _sessions[sessionId];
                        wrapper.Session = session;
                        wrapper.VolumeControl = session.SimpleAudioVolume;
                        wrapper.MeterInfo = session.AudioMeterInformation;
                        if (wrapper.VolumeControl != null)
                        {
                            wrapper.Info.Volume = wrapper.VolumeControl.Volume;
                            wrapper.Info.IsMuted = wrapper.VolumeControl.Mute;
                        }
                        wrapper.LastSeen = DateTime.Now;
                        Console.WriteLine($"[RefreshSessions]   -> Existing session: {wrapper.Info.DisplayName}");
                    }
                    else
                    {
                        // Try to migrate an existing wrapper for the same process (session id changed)
                        var migrate = _sessions.FirstOrDefault(kvp => kvp.Value.Info.ProcessId == processId);
                        if (!string.IsNullOrEmpty(migrate.Key))
                        {
                            var wrapper = migrate.Value;
                            // Update wrapper to point to new session, keep Info instance
                            wrapper.Session = session;
                            wrapper.VolumeControl = session.SimpleAudioVolume;
                            wrapper.MeterInfo = session.AudioMeterInformation;
                            wrapper.LastSeen = DateTime.Now;
                            wrapper.Info.ProcessId = processId;
                            try
                            {
                                // Update display name if empty
                                if (string.IsNullOrWhiteSpace(wrapper.Info.DisplayName))
                                {
                                    var p = Process.GetProcessById(processId);
                                    wrapper.Info.DisplayName = session.DisplayName;
                                    if (string.IsNullOrWhiteSpace(wrapper.Info.DisplayName))
                                        wrapper.Info.DisplayName = p.ProcessName;
                                }
                            }
                            catch { }

                            // Move dictionary key
                            _sessions.TryRemove(migrate.Key, out _);
                            _sessions[sessionId] = wrapper;
                            newSessions.Add(wrapper.Info.DisplayName);
                            Console.WriteLine($"[RefreshSessions]   -> MIGRATED: {wrapper.Info.DisplayName} oldKey={migrate.Key} newKey={sessionId}");
                        }
                        else
                        {
                            // Create new wrapper
                            var info = CreateSessionInfo(session, processId);
                            var wrapper = new SessionWrapper
                            {
                                Session = session,
                                Info = info,
                                VolumeControl = session.SimpleAudioVolume,
                                MeterInfo = session.AudioMeterInformation,
                                LastSeen = DateTime.Now
                            };

                            _sessions.TryAdd(sessionId, wrapper);
                            newSessions.Add(info.DisplayName);
                            Console.WriteLine($"[RefreshSessions]   -> NEW SESSION: {info.DisplayName} (PID: {processId})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RefreshSessions] Error processing session {i}: {ex.Message}");
                }
            }

            // Remove sessions that have not been seen for a grace period
            var now = DateTime.Now;
            foreach (var kvp in _sessions.ToArray())
            {
                if (!currentSessionIds.Contains(kvp.Key))
                {
                    var ageMs = (now - kvp.Value.LastSeen).TotalMilliseconds;
                    if (ageMs > SessionGraceRemovalMs)
                    {
                        if (_sessions.TryRemove(kvp.Key, out var wrapper))
                        {
                            removedSessions.Add(wrapper.Info.DisplayName);
                            Console.WriteLine($"[RefreshSessions]   -> REMOVED (grace expired): {wrapper.Info.DisplayName}");
                        }
                    }
                }
            }

            Console.WriteLine($"[RefreshSessions] Scan complete. Total tracked sessions: {_sessions.Count}, New: {newSessions.Count}, Removed: {removedSessions.Count}");

            // Only notify if there were changes or periodically to update meters
            if (newSessions.Count > 0 || removedSessions.Count > 0)
            {
                Console.WriteLine($"[RefreshSessions] Notifying UI of session changes...");
                NotifySessionsChanged();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefreshSessions] ERROR: {ex.Message}");
        }
    }

    private AudioSessionInfo CreateSessionInfo(AudioSessionControl session, int processId)
    {
        var info = new AudioSessionInfo
        {
            SessionId = $"{processId}_{session.GetSessionIdentifier}",
            ProcessId = processId,
            SessionType = AudioSessionType.Application
        };

        try
        {
            // Get process information
            var process = Process.GetProcessById(processId);
            info.ProcessName = process.ProcessName;
            info.DisplayName = session.DisplayName;

            // Use session display name if available, otherwise use process name
            if (string.IsNullOrWhiteSpace(info.DisplayName))
            {
                info.DisplayName = info.ProcessName;
            }

            // Try to get icon path
            try
            {
                info.IconPath = process.MainModule?.FileName;
            }
            catch
            {
                // Some processes don't allow access to MainModule
            }

            // Get volume info
            if (session.SimpleAudioVolume != null)
            {
                info.Volume = session.SimpleAudioVolume.Volume;
                info.IsMuted = session.SimpleAudioVolume.Mute;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating session info for process {processId}: {ex.Message}");
            info.DisplayName = $"Process {processId}";
            info.ProcessName = $"Process {processId}";
        }

        return info;
    }

    private void VuMeterTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Periodically refresh session list (every 2 seconds)
            var now = DateTime.Now;
            var timeSinceLastRefresh = (now - _lastSessionRefresh).TotalMilliseconds;

            if (timeSinceLastRefresh >= SessionRefreshIntervalMs)
            {
                Console.WriteLine($"[VuMeterTimer] Triggering session refresh (time since last: {timeSinceLastRefresh:F0}ms)");
                RefreshSessions();
                _lastSessionRefresh = now;
            }

            // Update peak levels
            var peakLevels = new Dictionary<string, float>();

            // Add master output peak level
            if (_defaultDevice != null)
            {
                try
                {
                    var peakLevel = _defaultDevice.AudioMeterInformation.MasterPeakValue;
                    peakLevels["master_output"] = peakLevel;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating master output peak level: {ex.Message}");
                }
            }

            // Add input device peak levels
            foreach (var kvp in _inputDevices)
            {
                try
                {
                    var peakLevel = kvp.Value.AudioMeterInformation.MasterPeakValue;
                    peakLevels[kvp.Key] = peakLevel;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating input device peak level for {kvp.Key}: {ex.Message}");
                }
            }

            // Add output device peak levels
            foreach (var kvp in _outputDevices)
            {
                try
                {
                    var peakLevel = kvp.Value.AudioMeterInformation.MasterPeakValue;
                    peakLevels[kvp.Key] = peakLevel;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating output device peak level for {kvp.Key}: {ex.Message}");
                }
            }

            // Add application session peak levels
            foreach (var kvp in _sessions)
            {
                try
                {
                    if (kvp.Value.MeterInfo != null)
                    {
                        var peakLevel = kvp.Value.MeterInfo.MasterPeakValue;
                        kvp.Value.Info.PeakLevel = peakLevel;
                        peakLevels[kvp.Key] = peakLevel;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating peak level for session {kvp.Key}: {ex.Message}");
                }
            }

            if (peakLevels.Count > 0)
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    PeakLevelsUpdated?.Invoke(this, new PeakLevelEventArgs { PeakLevels = peakLevels });
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in VU meter timer: {ex.Message}");
        }
    }

    private void NotifySessionsChanged()
    {
        var sessions = GetActiveSessions();
        DispatcherHelper.RunOnUIThread(() =>
        {
            SessionsChanged?.Invoke(this, new AudioSessionChangedEventArgs { Sessions = sessions });
        });
    }

    private void CleanupSessions()
    {
        foreach (var wrapper in _sessions.Values)
        {
            try
            {
                wrapper.Session?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing session: {ex.Message}");
            }
        }
        _sessions.Clear();
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
