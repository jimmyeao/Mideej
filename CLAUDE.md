# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Mideej is a MIDI-based Windows audio mixer application targeting .NET 9.0. It's inspired by [DeejNG](https://github.com/jimmyeao/DeejNG) but uses MIDI devices instead of serial communication to control Windows audio sessions.

**Key Differences from DeejNG:**
- Uses MIDI input (MIDI controllers, keyboards, control surfaces) instead of serial/Arduino devices
- Maps MIDI CC (Control Change) messages to audio controls
- Adds solo and filter effect capabilities
- MIDI provides standardized protocol (no custom serial parsing needed)

**Core Functionality:**
- Control volume for individual applications, system audio, and microphones via MIDI
- Map MIDI controls (faders, knobs, buttons) to audio session parameters
- Mute/Solo controls via MIDI buttons or toggles
- Audio filtering effects controllable via MIDI
- Real-time VU meters for visual feedback
- Profile support for different MIDI controller layouts

## Technology Stack

- **Framework**: .NET 9.0 (net9.0-windows)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Language**: C# with nullable reference types enabled
- **Implicit usings**: Enabled
- **Audio Library**: NAudio (for Windows audio session management and audio effects)
- **MIDI Library**: NAudio.Midi or Windows.Devices.Midi (for MIDI device communication)
- **Graphics**: SkiaSharp (for VU meter rendering)

## Build and Run Commands

### Build the project
```bash
dotnet build
```

### Run the application
```bash
dotnet run
```

### Clean build artifacts
```bash
dotnet clean
```

### Restore dependencies
```bash
dotnet restore
```

## Architecture

### High-Level Architecture

The application follows a layered architecture:

1. **MIDI Input Layer**: Receives and processes MIDI messages from connected devices
2. **Mapping Layer**: Translates MIDI CC messages to application actions (volume, mute, solo, effects)
3. **Audio Control Layer**: Interfaces with Windows audio sessions via NAudio
4. **Effects Processing Layer**: Applies audio filters and effects
5. **UI Layer**: WPF controls displaying channels, VU meters, and configuration

### Core Components (To Be Implemented)

- **ChannelControl**: UI component representing a single audio channel with volume, mute, solo controls
- **MidiDeviceManager**: Handles MIDI device enumeration, connection, and message routing
- **MidiMapper**: Maps MIDI CC numbers to audio session controls and functions
- **SessionManager**: Manages Windows audio sessions (applications, devices, system audio)
- **AudioEffectProcessor**: Applies filter effects to audio streams
- **VUMeterRenderer**: Real-time audio level visualization using SkiaSharp
- **ConfigurationManager**: Persists MIDI mappings, profiles, and user preferences

### Project Structure

- **App.xaml / App.xaml.cs**: Application entry point and global resources
- **MainWindow.xaml / MainWindow.xaml.cs**: Main window UI and code-behind
- **AssemblyInfo.cs**: Assembly-level attributes, including WPF theme information

### WPF Pattern

This project follows the standard WPF code-behind pattern with MVVM considerations:
- XAML files define UI layout and structure
- Code-behind (.cs) files contain event handlers and window logic
- View models may be introduced for complex state management
- The `App.xaml` defines the startup window via `StartupUri="MainWindow.xaml"`

### Configuration

- **Nullable reference types**: Enabled - all reference types are non-nullable by default
- **Implicit usings**: Enabled - common namespaces are automatically imported
- **Output type**: WinExe - Windows GUI application (no console window)

## MIDI Implementation Details

### MIDI Message Types

- **Control Change (CC)**: Used for faders, knobs, and continuous controls (0-127 values)
- **Note On/Off**: Can be used for buttons (mute, solo toggles)
- **Program Change**: Could be used for profile switching

### MIDI Mapping Strategy

1. **Learning Mode**: Allow users to "learn" MIDI mappings by moving controls
2. **CC to Control Mapping**: Store mappings as `Dictionary<(int channel, int ccNumber), ControlAction>`
3. **Value Scaling**: Convert MIDI values (0-127) to appropriate ranges (volume 0-100%, filter frequency, etc.)

### Audio Session Control

Using NAudio's `AudioSessionControl2`:
- Enumerate active audio sessions via `AudioSessionManager`
- Control volume per session using `SimpleAudioVolume`
- Monitor audio levels for VU meters via `AudioMeterInformation`
- Handle session notifications (new sessions, disconnected sessions)

### Solo Implementation

Solo is exclusive - when one channel is soloed:
1. Mute all other channels except soloed ones
2. Track solo state separately from user-initiated mutes
3. Restore previous mute states when solo is deactivated

### Filter Effects

Implement audio effects using NAudio's `ISampleProvider` chain:
- **Low-pass filter**: Attenuate frequencies above cutoff
- **High-pass filter**: Attenuate frequencies below cutoff
- **Band-pass filter**: Combine low-pass and high-pass
- Apply effects per-channel or globally

## Development Notes

### XAML Editing

When modifying UI:
1. Edit XAML files for layout and visual structure
2. Add event handlers and logic in corresponding .cs code-behind files
3. WPF designer files are auto-generated in obj/Debug folder

### MIDI Device Testing

When working with MIDI features:
1. Use MIDI-OX or similar tools to monitor MIDI messages during development
2. Handle device hot-plugging (connect/disconnect events)
3. Test with multiple MIDI device types (control surfaces, keyboards, etc.)
4. Validate CC number ranges and channel filtering

### Audio Session Management

- Audio sessions are dynamic - applications can start/stop at any time
- Cache session information but handle stale sessions gracefully
- UI updates should be throttled to avoid excessive redraws
- VU meter updates typically use 25-40ms refresh intervals

### Configuration Persistence

Store user configuration including:
- MIDI device selections and mappings
- Audio session to channel assignments
- Profile definitions
- Window position and theme preferences

Use JSON serialization for configuration files stored in AppData.

### Theme Configuration

Theme resource dictionaries are configured in AssemblyInfo.cs using the `ThemeInfo` attribute. The current configuration:
- Theme-specific resources: None (ResourceDictionaryLocation.None)
- Generic resource dictionary: SourceAssembly

## Reference Implementation

This project is based on [DeejNG](https://github.com/jimmyeao/DeejNG). Key concepts and patterns from DeejNG that apply here:
- Session management and caching strategies
- VU meter rendering approach
- Profile and configuration architecture
- Channel control UI patterns
