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
    
    public event EventHandler<AudioSessionChangedEventArgs>? SessionsChanged;
    public event EventHandler<SessionVolumeChangedEventArgs>? SessionVolumeChanged;
    public event EventHandler<PeakLevelEventArgs>? PeakLevelsUpdated;
    public event EventHandler<MasterMuteChangedEventArgs>? MasterMuteChanged;

    public List<AudioSessionInfo> GetActiveSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        try
        {
            // Get default device fresh every time (like DeejNG)
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            
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

                    sessions.Add(new AudioSessionInfo
                    {
                        SessionId = $"app_{processName}_{pid}",
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
            if (sessionId == "master_output")
            {
                var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return device.AudioMeterInformation.MasterPeakValue;
            }

            if (sessionId.StartsWith("app_"))
            {
                var parts = sessionId.Split('_');
                if (parts.Length >= 2)
                {
                    string processName = parts[1];
                    return GetPeakLevelForTarget(processName);
                }
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
                SessionsChanged?.Invoke(this, new AudioSessionChangedEventArgs { Sessions = sessions });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionRefresh] Error: {ex.Message}");
            }
        };
        _sessionRefreshTimer.Start();

        // Poll for peak levels every 50ms
        _peakLevelTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _peakLevelTimer.Tick += (s, e) =>
        {
            try
            {
                var sessions = GetActiveSessions();
                var peaks = new Dictionary<string, float>();
                foreach (var session in sessions)
                {
                    peaks[session.SessionId] = session.PeakLevel;
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

    public void Dispose()
    {
        StopMonitoring();
        _deviceEnumerator?.Dispose();
    }
}
