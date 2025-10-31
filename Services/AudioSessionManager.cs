using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
    private int _meterErrorCount = 0;
    private DateTime _lastMeterError = DateTime.MinValue;
    private const int MaxConsecutiveErrors = 10;
    private static readonly string _logFilePath = Path.Combine(Path.GetTempPath(), "Mideej_AudioErrors.log");

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
                try
                {
                    var masterSession = new AudioSessionInfo
                    {
                        SessionId = "master_output",
                        DisplayName = "Master Volume",
                        ProcessName = "System",
                        SessionType = AudioSessionType.Output,
                        Volume = _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = _defaultDevice.AudioEndpointVolume.Mute,
                        PeakLevel = _defaultDevice.AudioMeterInformation.MasterPeakValue
                    };
                    sessions.Add(masterSession);
                    Debug.WriteLine($"[GetActiveSessions] Added master_output - Muted={masterSession.IsMuted}");
                }
                catch (InvalidCastException) { /* COM object released */ }
                catch (COMException) { /* Device disconnected */ }
                catch { }
            }
            else
            {
                Debug.WriteLine($"[GetActiveSessions] _defaultDevice is NULL!");
            }

            // Use cached input devices (avoid re-enumeration here)
            foreach (var kvp in _inputDevices.ToArray())
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
                catch (InvalidCastException) { /* COM object released */ }
                catch (COMException) { /* Device disconnected */ }
                catch { }
            }

            // Use cached output devices
            Debug.WriteLine($"[GetActiveSessions] Output devices count: {_outputDevices.Count}");
            foreach (var kvp in _outputDevices.ToArray())
            {
                var device = kvp.Value;
                try
                {
                    var outputSession = new AudioSessionInfo
                    {
                        SessionId = kvp.Key,
                        DisplayName = device.FriendlyName,
                        ProcessName = "Audio Output",
                        SessionType = AudioSessionType.Output,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        PeakLevel = device.AudioMeterInformation.MasterPeakValue
                    };
                    sessions.Add(outputSession);
                    Debug.WriteLine($"[GetActiveSessions] Added output device {kvp.Key} - {device.FriendlyName} - Muted={outputSession.IsMuted}");
                }
                catch (InvalidCastException ex) 
                { 
                    Debug.WriteLine($"[GetActiveSessions] InvalidCastException for output device {kvp.Key}: {ex.Message}");
                }
                catch (COMException ex) 
                { 
                    Debug.WriteLine($"[GetActiveSessions] COMException for output device {kvp.Key}: {ex.Message}");
                }
                catch (Exception ex) 
                { 
                    Debug.WriteLine($"[GetActiveSessions] Exception for output device {kvp.Key}: {ex.Message}");
                }
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

        // Add global exception handler for unhandled exceptions in background threads
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

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

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is InvalidCastException ex)
        {
            Console.WriteLine($"[AppDomain] Unhandled InvalidCastException: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

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
        Debug.WriteLine($"[SetSessionMute] Called for {sessionId}, isMuted={isMuted}");
        try
        {
            // Handle master output
            if (sessionId == "master_output" && _defaultDevice != null)
            {
                Debug.WriteLine($"[SetSessionMute] Setting master_output mute={isMuted}");
                _defaultDevice.AudioEndpointVolume.Mute = isMuted;
                return;
            }

            // Handle input devices
            if (sessionId.StartsWith("input_") && _inputDevices.TryGetValue(sessionId, out var inputDevice))
            {
                Debug.WriteLine($"[SetSessionMute] Setting input device {sessionId} mute={isMuted}");
                inputDevice.AudioEndpointVolume.Mute = isMuted;
                Debug.WriteLine($"[SetSessionMute] Successfully set input device mute");
                return;
            }

            // Handle output devices
            if (sessionId.StartsWith("output_") && _outputDevices.TryGetValue(sessionId, out var outputDevice))
            {
                Debug.WriteLine($"[SetSessionMute] Found output device in dictionary");
                outputDevice.AudioEndpointVolume.Mute = isMuted;
                Debug.WriteLine($"[SetSessionMute] Successfully set output device mute");
                return;
            }
            else if (sessionId.StartsWith("output_"))
            {
                Debug.WriteLine($"[SetSessionMute] Output device {sessionId} NOT FOUND in _outputDevices!");
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
        catch (InvalidCastException)
        {
            // COM object released - ignore
        }
        catch (COMException)
        {
            // Device/session disconnected - ignore
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
            if (_deviceEnumerator == null || !_isMonitoring)
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
                try 
                { 
                    sessionsOnDevice = device.AudioSessionManager.Sessions; 
                }
                catch (InvalidCastException ex)
                {
                    var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] InvalidCastException getting Sessions for device {deviceId}: {ex.Message}\nStackTrace: {ex.StackTrace}\n";
                    Console.WriteLine(logMsg);
                    try { File.AppendAllText(_logFilePath, logMsg); } catch { }
                    continue;
                }
                catch (Exception ex)
                {
                    var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] Exception getting Sessions for device {deviceId}: {ex.GetType().Name} - {ex.Message}\n";
                    try { File.AppendAllText(_logFilePath, logMsg); } catch { }
                    continue;
                }

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
                            try
                            {
                                wrapper.VolumeControl = session.SimpleAudioVolume;
                                wrapper.MeterInfo = session.AudioMeterInformation;
                                if (wrapper.VolumeControl != null)
                                {
                                    wrapper.Info.Volume = wrapper.VolumeControl.Volume;
                                    wrapper.Info.IsMuted = wrapper.VolumeControl.Mute;
                                }
                            }
                            catch (InvalidCastException ex)
                            {
                                Console.WriteLine($"[RefreshSessions] InvalidCastException accessing session properties for {sessionKey}: {ex.Message}");
                                continue;
                            }
                            catch (COMException)
                            {
                                continue;
                            }
                            wrapper.LastSeen = nowUtc;
                            _processIndex[(wrapper.Info.ProcessId, deviceId)] = sessionKey;
                        }
                        else if (_processIndex.TryGetValue((processId, deviceId), out var oldKey) && _sessions.TryGetValue(oldKey, out var existing))
                        {
                            var wrapper = existing;
                            wrapper.Session = session;
                            try
                            {
                                wrapper.VolumeControl = session.SimpleAudioVolume;
                                wrapper.MeterInfo = session.AudioMeterInformation;
                            }
                            catch (InvalidCastException ex)
                            {
                                Console.WriteLine($"[RefreshSessions] InvalidCastException accessing session properties for {sessionKey}: {ex.Message}");
                                continue;
                            }
                            catch (COMException)
                            {
                                continue;
                            }
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

                            SimpleAudioVolume? volumeControl = null;
                            AudioMeterInformation? meterInfo = null;
                            try
                            {
                                volumeControl = session.SimpleAudioVolume;
                                meterInfo = session.AudioMeterInformation;
                            }
                            catch (InvalidCastException ex)
                            {
                                Console.WriteLine($"[RefreshSessions] InvalidCastException accessing new session properties for {sessionKey}: {ex.Message}");
                                continue;
                            }
                            catch (COMException)
                            {
                                continue;
                            }

                            var wrapper = new SessionWrapper
                            {
                                Session = session,
                                Info = info,
                                VolumeControl = volumeControl,
                                MeterInfo = meterInfo,
                                LastSeen = nowUtc
                            };
                            try { session.RegisterEventClient(new SessionEventsHandler(this, sessionKey)); } catch { }
                            _sessions.TryAdd(sessionKey, wrapper);
                            _processIndex[(processId, deviceId)] = sessionKey;
                            newSessions.Add(info.DisplayName);
                        }
                    }
                    catch (InvalidCastException ex)
                    {
                        Console.WriteLine($"[RefreshSessions] InvalidCastException in session loop iteration {i}: {ex.Message}");
                    }
                    catch (COMException ex)
                    {
                        Console.WriteLine($"[RefreshSessions] COMException in session loop iteration {i}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RefreshSessions] {ex.GetType().Name} in session loop iteration {i}: {ex.Message}");
                    }
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
        catch (InvalidCastException ex)
        {
            Console.WriteLine($"[RefreshSessions] InvalidCastException: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
        catch (COMException ex)
        {
            Console.WriteLine($"[RefreshSessions] COMException: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefreshSessions] {ex.GetType().Name}: {ex.Message}");
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
        if (!_isMonitoring)
            return;

        // If we've had too many consecutive errors, slow down the meter rate
        if (_meterErrorCount > MaxConsecutiveErrors)
        {
            if ((DateTime.UtcNow - _lastMeterError).TotalSeconds < 5)
            {
                return; // Skip this tick
            }
            _meterErrorCount = 0; // Reset after cooldown
        }

        try
        {
            MeterTimer_Tick_Internal();
            _meterErrorCount = 0; // Reset on success
        }
        catch (InvalidCastException ex)
        {
            _meterErrorCount++;
            _lastMeterError = DateTime.UtcNow;
            if (_meterErrorCount <= 3) // Only log first few
            {
                var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] [MeterTimer TOP LEVEL] InvalidCastException: {ex.Message}\nStackTrace: {ex.StackTrace}\n";
                Console.WriteLine(logMsg);
                try { File.AppendAllText(_logFilePath, logMsg); } catch { }
            }
        }
        catch (Exception ex)
        {
            _meterErrorCount++;
            _lastMeterError = DateTime.UtcNow;
            Console.WriteLine($"[MeterTimer TOP LEVEL] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void MeterTimer_Tick_Internal()
    {
        try
        {
            // Collect peaks off the UI thread
            _peakLevelsBuffer.Clear();

            if (_defaultDevice != null)
            {
                try 
                { 
                    var meterInfo = _defaultDevice.AudioMeterInformation;
                    if (meterInfo != null)
                    {
                        _peakLevelsBuffer["master_output"] = meterInfo.MasterPeakValue;
                    }
                } 
                catch (InvalidCastException ex) 
                { 
                    Debug.WriteLine($"[MeterTimer] InvalidCastException on master_output: {ex.Message}");
                    // Don't null out the device, just skip this tick
                }
                catch (COMException ex) 
                { 
                    Debug.WriteLine($"[MeterTimer] COMException on master_output: {ex.Message}");
                    // Don't null out the device, just skip this tick
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[MeterTimer] Exception on master_output: {ex.GetType().Name} - {ex.Message}");
                }
            }

            // Copy to array to avoid collection modified exceptions
            var inputDevicesCopy = _inputDevices.ToArray();
            foreach (var kvp in inputDevicesCopy)
            {
                try 
                { 
                    var meterInfo = kvp.Value?.AudioMeterInformation;
                    if (meterInfo != null)
                    {
                        _peakLevelsBuffer[kvp.Key] = meterInfo.MasterPeakValue;
                    }
                } 
                catch (InvalidCastException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] InvalidCastException on input device {kvp.Key}: {ex.Message}");
                    _inputDevices.TryRemove(kvp.Key, out _);
                }
                catch (COMException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] COMException on input device {kvp.Key}: {ex.Message}");
                    _inputDevices.TryRemove(kvp.Key, out _);
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[MeterTimer] Exception on input device {kvp.Key}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            var outputDevicesCopy = _outputDevices.ToArray();
            foreach (var kvp in outputDevicesCopy)
            {
                try 
                { 
                    var meterInfo = kvp.Value?.AudioMeterInformation;
                    if (meterInfo != null)
                    {
                        _peakLevelsBuffer[kvp.Key] = meterInfo.MasterPeakValue;
                    }
                } 
                catch (InvalidCastException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] InvalidCastException on output device {kvp.Key}: {ex.Message}");
                    _outputDevices.TryRemove(kvp.Key, out _);
                }
                catch (COMException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] COMException on output device {kvp.Key}: {ex.Message}");
                    _outputDevices.TryRemove(kvp.Key, out _);
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[MeterTimer] Exception on output device {kvp.Key}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            var sessionsCopy = _sessions.ToArray();
            foreach (var kvp in sessionsCopy)
            {
                try
                {
                    var meterInfo = kvp.Value?.MeterInfo;
                    if (meterInfo != null)
                    {
                        var peak = meterInfo.MasterPeakValue;
                        kvp.Value.Info.PeakLevel = peak; // keep cached
                        _peakLevelsBuffer[kvp.Key] = peak;
                    }
                }
                catch (InvalidCastException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] InvalidCastException on session {kvp.Key}: {ex.Message}");
                    // Mark for removal
                    kvp.Value.LastSeen = DateTime.UtcNow.AddMilliseconds(-(SessionGraceRemovalMs + 100));
                }
                catch (COMException ex) 
                { 
                    Console.WriteLine($"[MeterTimer] COMException on session {kvp.Key}: {ex.Message}");
                    kvp.Value.LastSeen = DateTime.UtcNow.AddMilliseconds(-(SessionGraceRemovalMs + 100));
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[MeterTimer] Exception on session {kvp.Key}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            if (_peakLevelsBuffer.Count > 0)
            {
                var payload = new Dictionary<string, float>(_peakLevelsBuffer);
                DispatcherHelper.RunOnUIThread(() =>
                {
                    try
                    {
                        PeakLevelsUpdated?.Invoke(this, new PeakLevelEventArgs { PeakLevels = payload });
                    }
                    catch { /* Ignore errors in event handlers */ }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MeterTimer_Tick_Internal] Error: {ex.GetType().Name} - {ex.Message}");
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
        if (_deviceEnumerator == null || !_isMonitoring) return;
        var nowUtc = DateTime.UtcNow;
        if (!force && (nowUtc - _lastDeviceRefresh).TotalMilliseconds < DeviceRefreshIntervalMs)
            return;

        var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] UpdateDeviceCaches called\n";
        try { File.AppendAllText(_logFilePath, logMsg); } catch { }

        // Capture new input devices
        try
        {
            var logMsg2 = $"[{DateTime.Now:HH:mm:ss.fff}] About to enumerate input devices\n";
            try { File.AppendAllText(_logFilePath, logMsg2); } catch { }
            
            var inputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var seen = new HashSet<string>();
            foreach (var device in inputDevices)
            {
                try
                {
                    var id = "input_" + device.ID;
                    seen.Add(id);
                    _inputDevices.AddOrUpdate(id, device, (_, old) => { try { old.Dispose(); } catch { } return device; });
                }
                catch (InvalidCastException) { /* COM error */ }
                catch (COMException) { /* Device error */ }
                catch { }
            }
            // Remove missing
            foreach (var kvp in _inputDevices.ToArray())
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (_inputDevices.TryRemove(kvp.Key, out var d)) { try { d.Dispose(); } catch { } }
                }
            }
        }
        catch (InvalidCastException ex) 
        { 
            var logMsg3 = $"[{DateTime.Now:HH:mm:ss.fff}] InvalidCastException in input device enumeration: {ex.Message}\nStackTrace: {ex.StackTrace}\n";
            Console.WriteLine(logMsg3);
            try { File.AppendAllText(_logFilePath, logMsg3); } catch { }
        }
        catch (COMException ex) 
        { 
            var logMsg4 = $"[{DateTime.Now:HH:mm:ss.fff}] COMException in input device enumeration: {ex.Message}\n";
            try { File.AppendAllText(_logFilePath, logMsg4); } catch { }
        }
        catch (Exception ex) 
        { 
            var logMsg5 = $"[{DateTime.Now:HH:mm:ss.fff}] Exception in input device enumeration: {ex.GetType().Name} - {ex.Message}\n";
            try { File.AppendAllText(_logFilePath, logMsg5); } catch { }
        }

        // Capture new output devices
        try
        {
            var logMsg6 = $"[{DateTime.Now:HH:mm:ss.fff}] About to enumerate output devices\n";
            try { File.AppendAllText(_logFilePath, logMsg6); } catch { }
            
            var outputDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var seen = new HashSet<string>();
            foreach (var device in outputDevices)
            {
                try
                {
                    var id = "output_" + device.ID;
                    seen.Add(id);
                    _outputDevices.AddOrUpdate(id, device, (_, old) => { try { old.Dispose(); } catch { } return device; });
                }
                catch (InvalidCastException) { /* COM error */ }
                catch (COMException) { /* Device error */ }
                catch { }
            }
            foreach (var kvp in _outputDevices.ToArray())
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (_outputDevices.TryRemove(kvp.Key, out var d)) { try { d.Dispose(); } catch { } }
                }
            }
        }
        catch (InvalidCastException ex) 
        { 
            var logMsg7 = $"[{DateTime.Now:HH:mm:ss.fff}] InvalidCastException in output device enumeration: {ex.Message}\nStackTrace: {ex.StackTrace}\n";
            Console.WriteLine(logMsg7);
            try { File.AppendAllText(_logFilePath, logMsg7); } catch { }
        }
        catch (COMException ex) 
        { 
            var logMsg8 = $"[{DateTime.Now:HH:mm:ss.fff}] COMException in output device enumeration: {ex.Message}\n";
            try { File.AppendAllText(_logFilePath, logMsg8); } catch { }
        }
        catch (Exception ex) 
        { 
            var logMsg9 = $"[{DateTime.Now:HH:mm:ss.fff}] Exception in output device enumeration: {ex.GetType().Name} - {ex.Message}\n";
            try { File.AppendAllText(_logFilePath, logMsg9); } catch { }
        }

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
