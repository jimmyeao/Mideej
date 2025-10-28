using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Mideej.Services;

public interface IMediaControlService
{
    void Play();
    void Pause();
    void NextTrack();
    void PreviousTrack();
    void SeekForward();
    void SeekBackward();
    // Optional: toggle support if needed elsewhere
    void PlayPause();
    
    // Playback state monitoring
    Task InitializeAsync();
    event Action<GlobalSystemMediaTransportControlsSessionPlaybackStatus>? PlaybackStateChanged;
}
