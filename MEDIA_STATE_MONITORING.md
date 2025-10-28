# Windows Media Playback State Monitoring

## Overview
Mideej now monitors Windows media playback state in real-time and updates your MIDI controller LEDs accordingly.

## Features

### 1. **Initial State Detection**
When Mideej starts, it automatically detects the current playback state (Playing/Paused/Stopped) of any active media app and updates the controller LEDs to match.

### 2. **Real-Time State Changes**
When you click play/pause in any media app (Spotify, YouTube, Windows Media Player, etc.), Mideej captures the state change and immediately updates the controller LEDs:
- **Play LED**: ON when media is playing
- **Pause LED**: ON when media is paused

### 3. **Session Switching**
If you switch between different media apps, Mideej automatically tracks the new active session and updates LEDs accordingly.

## Implementation Details

### API Used
- **`GlobalSystemMediaTransportControlsSessionManager`**: Windows 10 1809+ API for media session monitoring
- **`GlobalSystemMediaTransportControlsSession`**: Represents the active media session

### Playback States
The system recognizes the following states:
- `Playing`: Media is actively playing
- `Paused`: Media is paused
- `Stopped`: Media is stopped
- `Closed`: Media app was closed
- `Changing`: Transitional state

### Event Flow
1. App starts → `MediaControlService.InitializeAsync()` called
2. Current playback state retrieved → LEDs updated via `PlaybackStateChanged` event
3. User clicks play/pause in media app → Windows broadcasts state change
4. `OnPlaybackInfoChanged` fires → `PlaybackStateChanged` event triggers
5. `MainWindowViewModel.OnPlaybackStateChanged()` processes state
6. `UpdatePlayPauseLeds()` sends MIDI messages to controller

## Code Locations

- **Service Interface**: `Services/IMediaControlService.cs`
- **Service Implementation**: `Services/MediaControlService.cs`
- **ViewModel Integration**: `ViewModels/MainWindowViewModel.cs` (lines 135-138, 1292-1309)
- **App Initialization**: `App.xaml.cs` (lines 50-55)

## Requirements

- **Windows Version**: Windows 10 1809 (Build 17763) or later
- **Target Framework**: `net9.0-windows10.0.17763.0`
- **.NET API**: `Windows.Media.Control` namespace

## Usage

No user action required! The feature works automatically:
1. Map transport controls (Play/Pause) to your MIDI controller buttons
2. Start Mideej - LEDs will reflect current media state
3. Play/pause media in any app - LEDs update instantly

## Troubleshooting

If LEDs don't update:
1. Ensure your media app supports Windows Media Transport Controls (most modern apps do)
2. Check that transport buttons are properly mapped in Mideej
3. Verify Windows version is 1809 or later
4. Check console output for "Playback state changed" messages
