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
    private SessionCollection? _sessionCollection;
    private readonly ConcurrentDictionary<string, SessionWrapper> _sessions = new();
    private DispatcherTimer? _vuMeterTimer;
    private bool _isMonitoring;
    private int _vuMeterInterval = 30;

    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;

    private class SessionWrapper
    {
        public AudioSessionControl Session { get; set; } = null!;
        public AudioSessionInfo Info { get; set; } = null!;
        public SimpleAudioVolume? VolumeControl { get; set; }
        public AudioMeterInformation? MeterInfo { get; set; }
    }

    public List<AudioSessionInfo> GetActiveSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        try
        {
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
            if (_sessions.TryGetValue(sessionId, out var wrapper))
            {
                if (wrapper.VolumeControl != null)
                {
                    wrapper.VolumeControl.Volume = Math.Clamp(volume, 0f, 1f);
                    wrapper.Info.Volume = volume;
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
            if (_sessions.TryGetValue(sessionId, out var wrapper))
            {
                if (wrapper.VolumeControl != null)
                {
                    wrapper.VolumeControl.Mute = isMuted;
                    wrapper.Info.IsMuted = isMuted;
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

            var currentSessionIds = new HashSet<string>();

            for (int i = 0; i < _sessionCollection.Count; i++)
            {
                try
                {
                    var session = _sessionCollection[i];
                    if (session.State != AudioSessionState.AudioSessionStateActive)
                        continue;

                    var processId = (int)session.GetProcessID;
                    var sessionId = $"{processId}_{session.GetSessionIdentifier}";

                    currentSessionIds.Add(sessionId);

                    if (!_sessions.ContainsKey(sessionId))
                    {
                        var info = CreateSessionInfo(session, processId);
                        var wrapper = new SessionWrapper
                        {
                            Session = session,
                            Info = info,
                            VolumeControl = session.SimpleAudioVolume,
                            MeterInfo = session.AudioMeterInformation
                        };

                        _sessions.TryAdd(sessionId, wrapper);
                    }
                    else
                    {
                        // Update existing session info
                        var wrapper = _sessions[sessionId];
                        if (wrapper.VolumeControl != null)
                        {
                            wrapper.Info.Volume = wrapper.VolumeControl.Volume;
                            wrapper.Info.IsMuted = wrapper.VolumeControl.Mute;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing session {i}: {ex.Message}");
                }
            }

            // Remove inactive sessions
            var inactiveSessions = _sessions.Keys.Except(currentSessionIds).ToList();
            foreach (var sessionId in inactiveSessions)
            {
                _sessions.TryRemove(sessionId, out _);
            }

            // Notify listeners
            NotifySessionsChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error refreshing sessions: {ex.Message}");
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
            // Periodically refresh session list
            if (_sessions.Count == 0 || DateTime.Now.Second % 5 == 0)
            {
                RefreshSessions();
            }

            // Update peak levels
            var peakLevels = new Dictionary<string, float>();

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
