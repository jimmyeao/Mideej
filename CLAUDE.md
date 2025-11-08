# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Mideej is a fully functional MIDI-based Windows audio mixer application targeting .NET 9.0. It allows users to control Windows application volumes using MIDI controllers (faders, knobs, and buttons).

**Core Functionality:**
- Control volume for individual applications, system audio, and microphones via MIDI
- Map MIDI controls (faders, knobs, buttons) to audio session parameters
- Mute/Solo controls via MIDI buttons
- Real-time VU meters for visual feedback
- Profile support for different MIDI controller layouts
- Controller presets for popular MIDI devices (M-Vave SMC Mixer, Behringer X-Touch Mini, Novation Launchpad)

## Technology Stack

- **Framework**: .NET 9.0 (net9.0-windows)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Language**: C# with nullable reference types enabled
- **Audio Library**: NAudio (for Windows audio session management and MIDI)
- **Graphics**: SkiaSharp (for VU meter rendering)
- **MVVM**: CommunityToolkit.MVVM

## Build and Run Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Build for release
dotnet build -c Release

# Clean build artifacts
dotnet clean

# Restore dependencies
dotnet restore
```

## Architecture

### High-Level Architecture

1. **MIDI Input Layer**: Receives and processes MIDI messages (CC, Pitch Bend, Note On/Off)
2. **Mapping Layer**: Translates MIDI messages to application actions (volume, mute, solo)
3. **Audio Control Layer**: Interfaces with Windows audio sessions via NAudio
4. **UI Layer**: WPF controls displaying channels, VU meters, and configuration

### Core Components

**Services:**
- **MidiService** (`Services/MidiService.cs`): MIDI device enumeration, connection, and message routing
- **AudioSessionManager** (`Services/AudioSessionManager.cs`): Windows audio session management with volume/mute control
- **ConfigurationService** (`Services/ConfigurationService.cs`): JSON-based persistence for settings and MIDI mappings

**UI Components:**
- **ChannelControl** (`Controls/ChannelControl.xaml`): Custom UserControl with volume slider, VU meter, mute/solo buttons
- **VuMeter** (`Controls/VuMeter.cs`): SkiaSharp-based real-time audio level visualization
- **MainWindow** (`MainWindow.xaml`): Main application window
- **SessionAssignmentWindow** (`Views/SessionAssignmentWindow.xaml`): Audio session assignment dialog

**ViewModels:**
- **MainWindowViewModel** (`ViewModels/MainWindowViewModel.cs`): Main application logic and MIDI mapping
- **ChannelViewModel** (`ViewModels/ChannelViewModel.cs`): Individual channel state and control

### Project Structure

```
Mideej/
├── App.xaml / App.xaml.cs          # Application entry point
├── MainWindow.xaml                  # Main window UI
├── AssemblyInfo.cs                  # Assembly attributes
├── Controls/                        # Custom UI controls
│   ├── ChannelControl.xaml         # Channel strip control
│   └── VuMeter.cs                  # VU meter rendering
├── Services/                        # Business logic services
│   ├── MidiService.cs              # MIDI handling
│   ├── AudioSessionManager.cs      # Audio session management
│   └── ConfigurationService.cs     # Settings persistence
├── ViewModels/                      # MVVM view models
│   ├── MainWindowViewModel.cs
│   └── ChannelViewModel.cs
├── Models/                          # Data models
├── Views/                           # Additional windows/dialogs
│   └── SessionAssignmentWindow.xaml
└── ControllerPresets/              # MIDI controller preset files
```

## MIDI Implementation

### Supported MIDI Message Types

- **Control Change (CC)**: Continuous controls like knobs (0-127 values)
- **Pitch Bend**: Faders in Mackie Control mode (0-16383 values, center at 8192)
- **Note On/Off**: Buttons for mute, solo, transport controls (velocity 0-127)

### Mackie Control Protocol Support

The application supports Mackie Control-compatible devices:
- **Faders**: MIDI Pitch Bend messages on channels 1-8
- **Knobs**: CC messages (typically CC#16-23)
- **Buttons**: Note messages for Record/Solo/Mute/Select
- **LED Feedback**: Sends Note On messages to illuminate button LEDs
- **Motorized Faders**: Sends Pitch Bend messages for fader position updates

### MIDI Mapping Features

- **Learning Mode**: Click gear icon, move control, mapping is created
- **Mapping Types**: Volume, Mute, Solo, Transport (Play/Stop/Next/Previous)
- **Value Scaling**: Convert MIDI values to appropriate ranges
- **Bi-directional**: Send feedback to controllers for motorized faders and LED buttons
- **Persistence**: All mappings saved to JSON configuration

### Audio Session Control

Using NAudio's Windows Audio Session API:
- Enumerate active audio sessions
- Control volume per session
- Control mute state per session
- Monitor audio levels for VU meters (30ms refresh rate)
- Handle dynamic session changes (new apps, closed apps)

## Current Implementation Status

### ✅ Fully Working Features

**MIDI Control:**
- MIDI device detection and connection
- CC, Pitch Bend, and Note message handling
- Learning mode for creating mappings
- Bi-directional MIDI (send feedback to controller)
- Mapping persistence
- Visual MIDI activity indicator

**Audio Control:**
- Windows audio session enumeration
- Volume control per application
- Mute/Solo functionality
- Real-time VU meters with color gradients
- Session assignment UI

**UI/UX:**
- Modern dark theme
- Drag-to-resize borderless window
- 8-channel layout (expandable)
- Settings panel
- MIDI device selection dropdown
- Audio session assignment dialog

**Configuration:**
- JSON-based settings (stored in AppData/Mideej)
- MIDI mapping persistence
- Channel configuration storage
- Controller preset support

### 🚧 Partially Implemented

**Audio Effects:**
- Filter models exist (FilterConfiguration)
- UI toggles present
- **Not implemented**: Actual DSP processing with NAudio filters

### Future Enhancements (see todo.md)

- Remove channels dynamically
- Remember window size and position
- Minimize to system tray
- Run at startup minimized to tray
- Configurable font size
- Additional controller presets
- Prevent soloing the master volume channel

## Configuration Files

### User Configuration
Stored in: `%APPDATA%/Mideej/`
- `settings.json`: Application settings
- `mappings.json`: MIDI mappings

### Controller Presets
Located in: `ControllerPresets/`
- Pre-configured mappings for popular controllers
- JSON format, user-editable
- See `ControllerPresets/README.md` for details

Example preset structure:
```json
{
  "ControllerName": "Your Controller",
  "Channels": [
    {
      "ChannelNumber": 1,
      "FaderCC": 0,
      "MuteCC": 16,
      "SoloCC": 32
    }
  ]
}
```

## Development Notes

### Adding New Features

1. **New MIDI Mappings**: Update `MidiService.cs` and `MainWindowViewModel.cs`
2. **New UI Controls**: Add to `Controls/` folder, follow existing patterns
3. **Audio Processing**: Extend `AudioSessionManager.cs`
4. **Settings**: Update `ConfigurationService.cs` and models

### Testing MIDI Functionality

1. Connect MIDI device
2. Select device from dropdown
3. Click gear icon on channel
4. Move MIDI control to create mapping
5. Verify control affects channel

### Audio Session Management

- Sessions are dynamic (apps start/stop)
- Cache session info but handle stale sessions
- UI updates throttled to avoid excessive redraws
- VU meters update every 30ms

## Reference

This project was inspired by [DeejNG](https://github.com/jimmyeao/DeejNG). Key patterns adapted:
- Session management and caching
- VU meter rendering approach
- Profile and configuration architecture
- Channel control UI patterns
- [GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetActiveSessions] Found Spotify: PID=27012, DisplayName='Spotify', SessionId=app_spotify_27012_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b27012

[GetActiveSessions] Found Spotify: PID=32512, DisplayName='Spotify', SessionId=app_spotify_32512_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b32512

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetActiveSessions] Found Spotify: PID=27012, DisplayName='Spotify', SessionId=app_spotify_27012_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b27012

[GetActiveSessions] Found Spotify: PID=32512, DisplayName='Spotify', SessionId=app_spotify_32512_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b32512

[GetActiveSessions] Found Spotify: PID=27012, DisplayName='Spotify', SessionId=app_spotify_27012_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b27012

[GetActiveSessions] Found Spotify: PID=32512, DisplayName='Spotify', SessionId=app_spotify_32512_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b32512

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetActiveSessions] Found Spotify: PID=27012, DisplayName='Spotify', SessionId=app_spotify_27012_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b27012

[GetActiveSessions] Found Spotify: PID=32512, DisplayName='Spotify', SessionId=app_spotify_32512_{0.0.0.00000000}.{4798d84b-8fa6-4fd8-8927-7c9fe1472d3b}|\Device\HarddiskVolume3\Users\jimmy\AppData\Roaming\Spotify\Spotify.exe%b{00000000-0000-0000-0000-000000000000}|1%b32512

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'ledkeeper2' in 10 sessions

[GetSessionPeakLevel] Searching for 'discord' in 10 sessions

[GetSessionPeakLevel] Searching for 'steam' in 10 sessions

[GetSessionPeakLevel] Searching for 'msedgewebview2' in 10 sessions

[GetSessionPeakLevel] Searching for 'spotify' in 10 sessions

[GetSessionPeakLevel] Searching for 'chrome' in 10 sessions

The program '[45500] Mideej.exe' has exited with code 4294967295 (0xffffffff). this is what ius happening when the spotify vu meter stops working