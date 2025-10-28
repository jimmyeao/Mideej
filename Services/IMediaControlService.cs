using System;

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
}
