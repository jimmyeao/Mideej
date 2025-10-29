using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Mideej.Helpers;
using Mideej.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Mideej.Services;

public class AudioSessionManager : IAudioSessionManager, IDisposable
{
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _defaultDevice;
    private MMDevice? _defaultInputDevice;
    private SessionCollection? _sessionCollection; // kept for compatibility, not used in multi-device loop
    private NAudio.CoreAudioApi.AudioSessionManager? _sessionManager; // default device session manager
    private IMMNotificationClient? _deviceNotificationClient;

    private readonly ConcurrentDictionary<string, SessionWrapper> _sessions = new();
    private readonly ConcurrentDictionary<(int ProcessId, string DeviceId), string> _processIndex = new(); // (processId, deviceId) -> sessionKey
    private readonly ConcurrentDictionary<string, MMDevice> _inputDevices = new();
    private readonly ConcurrentDictionary<string, MMDevice> _outputDevices = new();

    // VU metering moved to dedicated STA thread
    private Thread? _meterThread;
    private Dispatcher? _meterDispatcher;
    private DispatcherTimer? _bgVuTimer;

    private System.Timers.Timer? _sessionRefreshTimer;
    private volatile bool _scanInProgress;
    private bool _isMonitoring;
    private int _vuMeterInterval = 20; // default faster for responsiveness
    private DateTime _lastDeviceRefresh = DateTime.MinValue;
    private const int SessionRefreshIntervalMs = 1500; // separate background refresh
    private const int SessionGraceRemovalMs = 15000; // Keep missing sessions for 15s
    private const int DeviceRefreshIntervalMs = 3000; // refresh device list every 3s at most
    private readonly ConcurrentDictionary<string, float> _peakLevelsBuffer = new();

    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;
    public event EventHandler<MasterMuteChangedEventArgs>? MasterMuteChanged;

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

            // Use cached input devices (avoid re-enumeration here)
            foreach (var kvp in _inputDevices)
            {
                var device = kvp.Value;
                try
                {
                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = kvp.Key,
                        DisplayName = device.FriendlyName,
                        ProcessName = "Microphone",
                        SessionType = AudioSessionType.Input,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        PeakLevel = device.AudioMeterInformation.MasterPeakValue
                    });
                }
                catch { }
            }

            // Use cached output devices
            foreach (var kvp in _outputDevices)
            {
                var device = kvp.Value;
                try
                {
                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = kvp.Key,
                        DisplayName = device.FriendlyName,
                        ProcessName = "Audio Output",
                        SessionType = AudioSessionType.Output,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        PeakLevel = device.AudioMeterInformation.MasterPeakValue
                    });
                }
                catch { }
            }

            // De-duplicate application sessions by ProcessId; prefer default device and better names
            var bestByPid = new Dictionary<int, AudioSessionInfo>();
            foreach (var kvp in _sessions)
            {
                var info = kvp.Value.Info;
                if (info == null || info.SessionType != AudioSessionType.Application)
                    continue;

                info.DisplayName = NormalizeDisplayName(info);

                if (!bestByPid.TryGetValue(info.ProcessId, out var existing))
                {
                    bestByPid[info.ProcessId] = info;
                }
                else
                {
                    var existingOnDefault = IsOnDefaultDevice(existing.SessionId);
                    var candidateOnDefault = IsOnDefaultDevice(info.SessionId);
                    if (!existingOnDefault && candidateOnDefault)
                    {
                        bestByPid[info.ProcessId] = info;
                    }
                    else if (existingOnDefault == candidateOnDefault)
                    {
                        if (ScoreName(info) > ScoreName(existing))
                            bestByPid[info.ProcessId] = info;
                    }
                }
            }

            sessions.AddRange(bestByPid.Values);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting active sessions: {ex.Message}");
        }

        return sessions;
    }

    private bool IsOnDefaultDevice(string sessionId)
    {
        var sep = sessionId.IndexOf('|');
        if (sep <= 0 || _defaultDevice == null) return false;
        var deviceId = sessionId.Substring(0, sep);
        return string.Equals(deviceId, _defaultDevice.ID, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreName(AudioSessionInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.DisplayName)) return 0;
        int score = info.DisplayName.Length;
        if (!string.IsNullOrWhiteSpace(info.ProcessName) && !string.Equals(info.DisplayName, info.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        return score;
    }

    private static string NormalizeDisplayName(AudioSessionInfo info)
    {
        var name = info.DisplayName;
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("@") && !name.Contains("%SystemRoot%", StringComparison.OrdinalIgnoreCase))
            return name!;

        try
        {
            if (info.ProcessId > 0)
            {
                var proc = Process.GetProcessById(info.ProcessId);
                var product = proc.MainModule?.FileVersionInfo?.ProductName;
                if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    return proc.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(product))
                    return product!;
                if (!string.IsNullOrWhiteSpace(proc.ProcessName))
                    return proc.ProcessName;
            }
        }
        catch { }

        return info.ProcessName ?? name ?? "";
    }

    public void StartMonitoring()
    {
        if (_isMonitoring)
            return;

        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _sessionManager = _defaultDevice.AudioSessionManager;

            // Subscribe to endpoint notifications (device add/remove/default change)
            _deviceNotificationClient = new DeviceNotificationClient(this);
            _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceNotificationClient);

            // Subscribe to master volume change notifications
            if (_defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
            }

            UpdateDeviceCaches(force: true);
            RefreshSessions();

            // Start background session refresh timer to avoid UI lag; run on UI dispatcher
            _sessionRefreshTimer = new System.Timers.Timer(SessionRefreshIntervalMs) { AutoReset = true, Enabled = true };
            _sessionRefreshTimer.Elapsed += (_, __) =>
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    if (_scanInProgress) return;
                    _scanInProgress = true;
                    try { RefreshSessions(); } finally { _scanInProgress = false; }
                });
            };

            // Start meter thread
            StartMeterThread();

            _isMonitoring = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio session monitoring: {ex.Message}");
        }
    }

    private void StartMeterThread()
    {
        // Stop if running
        StopMeterThread();

        _meterThread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                _meterDispatcher = Dispatcher.CurrentDispatcher;
                _bgVuTimer = new DispatcherTimer(DispatcherPriority.Background, _meterDispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(_vuMeterInterval)
                };
                _bgVuTimer.Tick += MeterTimer_Tick;
                _bgVuTimer.Start();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Meter thread error: {ex.Message}");
            }
        })
        {
            IsBackground = true
        };
        _meterThread.SetApartmentState(ApartmentState.STA);
        _meterThread.Start();
    }

    private void StopMeterThread()
    {
        try
        {
            if (_meterDispatcher != null)
            {
                _meterDispatcher.Invoke(() =>
                {
                    try { _bgVuTimer?.Stop(); } catch { }
                    _bgVuTimer = null;
                });
                _meterDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }
        catch { }
        finally
        {
            _meterDispatcher = null;
            _meterThread = null;
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;

        try
        {
            StopMeterThread();

            if (_sessionRefreshTimer != null)
            {
                try { _sessionRefreshTimer.Stop(); } catch { }
                try { _sessionRefreshTimer.Dispose(); } catch { }
                _sessionRefreshTimer = null;
            }

            // Unregister endpoint notifications
            try
            {
                if (_deviceEnumerator != null && _deviceNotificationClient != null)
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceNotificationClient);
                }
            }
            catch { }
            _deviceNotificationClient = null;

            // Unsubscribe from master volume notifications
            if (_defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
            }

            CleanupSessions();

            _sessionCollection = null;
            _sessionManager = null;

            _defaultDevice?.Dispose();
            _defaultDevice = null;

            // Dispose cached devices
            foreach (var d in _inputDevices.Values) { try { d.Dispose(); } catch { } }
            _inputDevices.Clear();
            foreach (var d in _outputDevices.Values) { try { d.Dispose(); } catch { } }
            _outputDevices.Clear();

            _deviceEnumerator?.Dispose();
            _deviceEnumerator = null;

            _isMonitoring = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping audio session monitoring: {ex.Message}");
        }
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
                    wrapper.LastSeen = DateTime.UtcNow;
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
                    wrapper.LastSeen = DateTime.UtcNow;
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

    public void SetVuMeterUpdateInterval(int intervalMs)
    {
        _vuMeterInterval = Math.Clamp(intervalMs, 10, 100);
        if (_meterDispatcher != null)
        {
            _meterDispatcher.Invoke(() =>
            {
                if (_bgVuTimer != null)
                {
                    _bgVuTimer.Interval = TimeSpan.FromMilliseconds(_vuMeterInterval);
                }
            });
        }
    }

    private void RefreshSessions()
    {
        try
        {
            if (_deviceEnumerator == null)
                return;

            // refresh device caches at most every few seconds
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastDeviceRefresh).TotalMilliseconds >= DeviceRefreshIntervalMs)
            {
                UpdateDeviceCaches();
            }

            var currentSessionIds = new HashSet<string>();
            var newSessions = new List<string>();
            var removedSessions = new List<string>();

            // Enumerate sessions on all active render devices
            foreach (var devKvp in _outputDevices)
            {
                var device = devKvp.Value;
                var deviceId = device.ID;

                SessionCollection sessionsOnDevice;
                try { sessionsOnDevice = device.AudioSessionManager.Sessions; }
                catch { continue; }

                for (int i = 0; i < sessionsOnDevice.Count; i++)
                {
                    try
                    {
                        var session = sessionsOnDevice[i];
                        int processId;
                        try { processId = (int)session.GetProcessID; } catch { processId = -1; }
                        if (processId <= 0) continue;

                        var state = session.State;
                        if (state == AudioSessionState.AudioSessionStateExpired) continue;

                        string identifier = SafeGetIdentifier(session, i);
                        var sessionKey = string.Concat(deviceId, "|", processId.ToString(), "_", identifier);
                        currentSessionIds.Add(sessionKey);

                        if (_sessions.ContainsKey(sessionKey))
                        {
                            var wrapper = _sessions[sessionKey];
                            wrapper.Session = session;
                            wrapper.VolumeControl = session.SimpleAudioVolume;
                            wrapper.MeterInfo = session.AudioMeterInformation;
                            if (wrapper.VolumeControl != null)
                            {
                                wrapper.Info.Volume = wrapper.VolumeControl.Volume;
                                wrapper.Info.IsMuted = wrapper.VolumeControl.Mute;
                            }
                            wrapper.LastSeen = nowUtc;
                            _processIndex[(wrapper.Info.ProcessId, deviceId)] = sessionKey;
                        }
                        else if (_processIndex.TryGetValue((processId, deviceId), out var oldKey) && _sessions.TryGetValue(oldKey, out var existing))
                        {
                            var wrapper = existing;
                            wrapper.Session = session;
                            wrapper.VolumeControl = session.SimpleAudioVolume;
                            wrapper.MeterInfo = session.AudioMeterInformation;
                            wrapper.LastSeen = nowUtc;
                            wrapper.Info.ProcessId = processId;
                            _sessions.TryRemove(oldKey, out _);
                            _sessions[sessionKey] = wrapper;
                            _processIndex[(processId, deviceId)] = sessionKey;
                            newSessions.Add(wrapper.Info.DisplayName);
                        }
                        else
                        {
                            var info = CreateSessionInfo(session, processId);
                            info.SessionId = sessionKey;

                            info.DisplayName = NormalizeDisplayName(info);

                            var wrapper = new SessionWrapper
                            {
                                Session = session,
                                Info = info,
                                VolumeControl = session.SimpleAudioVolume,
                                MeterInfo = session.AudioMeterInformation,
                                LastSeen = nowUtc
                            };
                            try { session.RegisterEventClient(new SessionEventsHandler(this, sessionKey)); } catch { }
                            _sessions.TryAdd(sessionKey, wrapper);
                            _processIndex[(processId, deviceId)] = sessionKey;
                            newSessions.Add(info.DisplayName);
                        }
                    }
                    catch { }
                }
            }

            // Remove sessions missing beyond grace period
            var now = nowUtc;
            foreach (var kvp in _sessions.ToArray())
            {
                if (!currentSessionIds.Contains(kvp.Key))
                {
                    var ageMs = (now - kvp.Value.LastSeen).TotalMilliseconds;
                    if (ageMs > SessionGraceRemovalMs)
                    {
                        if (_sessions.TryRemove(kvp.Key, out var wrapper))
                        {
                            var sep = kvp.Key.IndexOf('|');
                            var devId = sep > 0 ? kvp.Key.Substring(0, sep) : string.Empty;
                            _processIndex.TryRemove((wrapper.Info.ProcessId, devId), out _);
                        }
                    }
                }
            }

            if (newSessions.Count > 0)
            {
                NotifySessionsChanged();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefreshSessions] ERROR: {ex.Message}");
        }
    }

    private static string SafeGetIdentifier(AudioSessionControl session, int index)
    {
        try
        {
            var id = session.GetSessionIdentifier;
            if (!string.IsNullOrEmpty(id)) return id;
        }
        catch { }
        try
        {
            var inst = session.GetSessionInstanceIdentifier;
            if (!string.IsNullOrEmpty(inst)) return inst;
        }
        catch { }
        return $"idx{index}";
    }

    private AudioSessionInfo CreateSessionInfo(AudioSessionControl session, int processId)
    {
        var info = new AudioSessionInfo
        {
            SessionId = $"{processId}_{SafeGetIdentifier(session, 0)}",
            ProcessId = processId,
            SessionType = AudioSessionType.Application
        };

        try
        {
            var process = Process.GetProcessById(processId);
            info.ProcessName = process.ProcessName;
            try { info.DisplayName = session.DisplayName; } catch { info.DisplayName = process.MainWindowTitle; }
            info.DisplayName = NormalizeDisplayName(info);
            try { info.IconPath = process.MainModule?.FileName; } catch { }
            if (session.SimpleAudioVolume != null)
            {
                info.Volume = session.SimpleAudioVolume.Volume;
                info.IsMuted = session.SimpleAudioVolume.Mute;
            }
        }
        catch
        {
            info.DisplayName = $"Process {processId}";
            info.ProcessName = $"Process {processId}";
        }

        return info;
    }

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Collect peaks off the UI thread
            _peakLevelsBuffer.Clear();

            if (_defaultDevice != null)
            {
                try { _peakLevelsBuffer["master_output"] = _defaultDevice.AudioMeterInformation.MasterPeakValue; } catch { }
            }

            foreach (var kvp in _inputDevices)
            {
                try { _peakLevelsBuffer[kvp.Key] = kvp.Value.AudioMeterInformation.MasterPeakValue; } catch { }
            }
            foreach (var kvp in _outputDevices)
            {
                try { _peakLevelsBuffer[kvp.Key] = kvp.Value.AudioMeterInformation.MasterPeakValue; } catch { }
            }
            foreach (var kvp in _sessions)
            {
                try
                {
                    if (kvp.Value.MeterInfo != null)
                    {
                        var peak = kvp.Value.MeterInfo.MasterPeakValue;
                        kvp.Value.Info.PeakLevel = peak; // keep cached
                        _peakLevelsBuffer[kvp.Key] = peak;
                    }
                }
                catch { }
            }

            if (_peakLevelsBuffer.Count > 0)
            {
                var payload = new Dictionary<string, float>(_peakLevelsBuffer);
                DispatcherHelper.RunOnUIThread(() =>
                {
                    PeakLevelsUpdated?.Invoke(this, new PeakLevelEventArgs { PeakLevels = payload });
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in meter timer: {ex.Message}");
        }
    }

    private void OnMasterVolumeChanged(AudioVolumeNotificationData data)
    {
        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                MasterMuteChanged?.Invoke(this, new MasterMuteChangedEventArgs
                {
                    IsMuted = data.Muted,
                    Volume = data.MasterVolume
                });
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling master volume change: {ex.Message}");
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

    private void UpdateDeviceCaches(bool force = false)
    {
        if (_deviceEnumerator == null) return;
        var nowUtc = DateTime.UtcNow;
        if (!force && (nowUtc - _lastDeviceRefresh).TotalMilliseconds < DeviceRefreshIntervalMs)
            return;

        // Capture new input devices
        try
        {
            var inputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var seen = new HashSet<string>();
            foreach (var device in inputDevices)
            {
                var id = "input_" + device.ID;
                seen.Add(id);
                _inputDevices.AddOrUpdate(id, device, (_, old) => { try { old.Dispose(); } catch { } return device; });
            }
            // Remove missing
            foreach (var kvp in _inputDevices)
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (_inputDevices.TryRemove(kvp.Key, out var d)) { try { d.Dispose(); } catch { } }
                }
            }
        }
        catch { }

        // Capture new output devices
        try
        {
            var outputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var seen = new HashSet<string>();
            foreach (var device in outputDevices)
            {
                var id = "output_" + device.ID;
                seen.Add(id);
                _outputDevices.AddOrUpdate(id, device, (_, old) => { try { old.Dispose(); } catch { } return device; });
            }
            foreach (var kvp in _outputDevices)
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (_outputDevices.TryRemove(kvp.Key, out var d)) { try { d.Dispose(); } catch { } }
                }
            }
        }
        catch { }

        _lastDeviceRefresh = nowUtc;
    }

    private void CleanupSessions()
    {
        foreach (var wrapper in _sessions.Values)
        {
            try { wrapper.Session?.Dispose(); } catch { }
        }
        _sessions.Clear();
        _processIndex.Clear();
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly AudioSessionManager _owner;
        public DeviceNotificationClient(AudioSessionManager owner) => _owner = owner;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            _owner.UpdateDeviceCaches(force: true);
            _owner.NotifySessionsChanged();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            _owner.UpdateDeviceCaches(force: true);
            _owner.NotifySessionsChanged();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            _owner.UpdateDeviceCaches(force: true);
            _owner.NotifySessionsChanged();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            try
            {
                if (flow == DataFlow.Render && role == Role.Multimedia && _owner._deviceEnumerator != null)
                {
                    if (_owner._defaultDevice != null)
                    {
                        _owner._defaultDevice.AudioEndpointVolume.OnVolumeNotification -= _owner.OnMasterVolumeChanged;
                        _owner._defaultDevice.Dispose();
                    }
                    _owner._defaultDevice = _owner._deviceEnumerator.GetDevice(defaultDeviceId);
                    _owner._sessionManager = _owner._defaultDevice.AudioSessionManager;
                    _owner._defaultDevice.AudioEndpointVolume.OnVolumeNotification += _owner.OnMasterVolumeChanged;
                    _owner.RefreshSessions();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling default device change: {ex.Message}");
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // not used
        }
    }

    private sealed class SessionEventsHandler : IAudioSessionEventsHandler
    {
        private readonly AudioSessionManager _owner;
        private readonly string _sessionKey;
        public SessionEventsHandler(AudioSessionManager owner, string sessionKey)
        {
            _owner = owner;
            _sessionKey = sessionKey;
        }

        public void OnDisplayNameChanged(string displayName)
        {
            if (_owner._sessions.TryGetValue(_sessionKey, out var w))
            {
                w.Info.DisplayName = displayName;
                _owner.NotifySessionsChanged();
            }
        }

        public void OnIconPathChanged(string iconPath)
        {
            if (_owner._sessions.TryGetValue(_sessionKey, out var w))
            {
                w.Info.IconPath = iconPath;
                _owner.NotifySessionsChanged();
            }
        }

        public void OnVolumeChanged(float volume, bool isMuted)
        {
            if (_owner._sessions.TryGetValue(_sessionKey, out var w))
            {
                w.Info.Volume = volume;
                w.Info.IsMuted = isMuted;
                w.LastSeen = DateTime.UtcNow;
                _owner.SessionVolumeChanged?.Invoke(_owner, new SessionVolumeChangedEventArgs { SessionId = _sessionKey, Volume = volume, IsMuted = isMuted });
            }
        }

        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
        {
            // not used
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
            // not used
        }

        public void OnStateChanged(AudioSessionState state)
        {
            if (state == AudioSessionState.AudioSessionStateExpired)
            {
                if (_owner._sessions.TryGetValue(_sessionKey, out var w))
                {
                    w.LastSeen = DateTime.UtcNow.AddMilliseconds(-(SessionGraceRemovalMs + 100));
                    _owner.RefreshSessions();
                }
            }
        }

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            if (_owner._sessions.TryGetValue(_sessionKey, out var w))
            {
                w.LastSeen = DateTime.UtcNow.AddMilliseconds(-(SessionGraceRemovalMs + 100));
                _owner.RefreshSessions();
            }
        }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
