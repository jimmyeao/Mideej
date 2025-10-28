using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using Windows.Media.Control;

namespace Mideej.Services;

/// <summary>
/// Sends system media key commands to control the OS/global media transport
/// and monitors playback state changes.
/// </summary>
public class MediaControlService : IMediaControlService
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    
    public event Action<GlobalSystemMediaTransportControlsSessionPlaybackStatus>? PlaybackStateChanged;
    
    public async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _currentSession = _sessionManager.GetCurrentSession();
            
            if (_currentSession != null)
            {
                // Get initial state
                var status = _currentSession.GetPlaybackInfo().PlaybackStatus;
                PlaybackStateChanged?.Invoke(status);
                
                // Subscribe to changes
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
            
            // Handle session changes (e.g., switching between apps)
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
        }
        catch (Exception)
        {
            // Silently fail if media sessions aren't available
        }
    }
    
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        var status = session.GetPlaybackInfo().PlaybackStatus;
        PlaybackStateChanged?.Invoke(status);
    }
    
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (_currentSession != null)
        {
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
            
        _currentSession = sender.GetCurrentSession();
        if (_currentSession != null)
        {
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            
            // Notify of new session's state
            var status = _currentSession.GetPlaybackInfo().PlaybackStatus;
            PlaybackStateChanged?.Invoke(status);
        }
    }
    
    public void PlayPause() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_MEDIA_PLAY_PAUSE);
    public void Play() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_MEDIA_PLAY_PAUSE); // Best-effort
    public void Pause() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_MEDIA_PLAY_PAUSE); // Best-effort
    public void NextTrack() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_MEDIA_NEXT_TRACK);
    public void PreviousTrack() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_MEDIA_PREV_TRACK);
    public void SeekForward() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_BROWSER_FORWARD); // Fallback
    public void SeekBackward() => SendMediaKey(KEYEVENTF_EXTENDEDKEY, VK_BROWSER_BACK); // Fallback

    private static void SendMediaKey(uint flags, ushort key)
    {
        // Use SendInput for reliability across apps
        INPUT[] inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    wScan = 0,
                    dwFlags = flags | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    // P/Invoke definitions
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    private const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    private const ushort VK_MEDIA_STOP = 0xB2;
    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    // Browser Forward/Back used as generic seek fallback (not all players support true seek hotkeys)
    private const ushort VK_BROWSER_BACK = 0xA6;
    private const ushort VK_BROWSER_FORWARD = 0xA7;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
