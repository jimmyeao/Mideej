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

### Core Components

**✅ Implemented:**
- **ChannelControl**: Custom UserControl with volume slider, VU meter, mute/solo buttons (C:\Users\jimmy\source\repos\Mideej\Controls\ChannelControl.xaml)
- **MidiService**: Complete MIDI device enumeration, connection, and message routing (C:\Users\jimmy\source\repos\Mideej\Services\MidiService.cs)
- **MainWindowViewModel**: Full MIDI mapping logic with CC and Note support (C:\Users\jimmy\source\repos\Mideej\ViewModels\MainWindowViewModel.cs)
- **AudioSessionManager**: Windows audio session management with volume/mute control (C:\Users\jimmy\source\repos\Mideej\Services\AudioSessionManager.cs)
- **VuMeter**: SkiaSharp-based real-time audio level visualization (C:\Users\jimmy\source\repos\Mideej\Controls\VuMeter.cs)
- **ConfigurationService**: JSON-based persistence for settings and MIDI mappings (C:\Users\jimmy\source\repos\Mideej\Services\ConfigurationService.cs)

**🚧 Partially Implemented:**
- **AudioEffectProcessor**: Filter models exist but DSP processing not yet implemented

**❌ Not Yet Implemented:**
- **Audio Session Assignment UI**: No dialog/interface to assign audio applications to channels
- **Profile Switcher UI**: Profile management exists in backend but no UI
- **Filter DSP Processing**: Need to implement actual audio filtering with NAudio

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

- **Control Change (CC)**: Used for knobs and rotary encoders (0-127 values)
- **Pitch Bend**: Used for faders in Mackie Control mode (0-16383 values, center at 8192)
- **Note On/Off**: Used for buttons (mute, solo, record, select toggles)
- **Program Change**: Could be used for profile switching

### Mackie Control Protocol Support

The application supports Mackie Control-compatible devices like the M-Vave SMC8:
- **Faders**: Use MIDI Pitch Bend messages on channels 1-8
- **Knobs**: Use CC messages 0x10-0x17 (CC#16-23)
- **Buttons**: Use Note messages 0x00-0x1F for Record/Solo/Mute/Select
- **LED Feedback**: Sends Note On messages back to illuminate button LEDs
- **Motorized Faders**: Sends Pitch Bend messages for motorized fader position updates

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

## Implementation Status (Current)

### ✅ Completed Features

**MIDI Control System:**
- MIDI device enumeration and connection
- MIDI CC message handling for continuous controls (knobs, rotary encoders)
- MIDI Pitch Bend message handling for faders (Mackie Control mode)
- MIDI Note On/Off handling for buttons (mute/solo)
- "Learn" mode for mapping MIDI controls to channels
- Mapping persistence (save/load from JSON configuration)
- Multi-channel support (8 channels by default, expandable)
- Value scaling and inversion support for mappings
- Bi-directional MIDI (send feedback to motorized faders and LED buttons)
- Visual MIDI activity indicator with real-time message display

**Audio Session Management:**
- Windows audio session enumeration via NAudio
- Per-session volume control
- Per-session mute control
- Real-time peak level monitoring (30ms refresh rate)
- VU meter visualization with color gradient (green → yellow → red)

**UI:**
- Modern dark theme with rounded borders
- Custom borderless window with drag/resize
- Channel strips with VU meters and volume sliders
- Mute/Solo buttons with visual feedback
- MIDI device selection dropdown
- Status bar with connection indicator
- Mapping mode visual feedback

**Configuration:**
- JSON-based settings persistence (stored in AppData/Mideej)
- MIDI mapping storage
- Channel configuration storage
- Profile support (backend complete)
- Auto-save on exit

### 🚧 Partially Complete

**Solo Logic:**
- Solo button implemented in UI
- Backend logic for exclusive solo complete
- **Missing**: Currently only works when audio sessions are assigned to channels

**Audio Effects:**
- Filter models defined (FilterConfiguration)
- UI toggle for filters exists
- **Missing**: Actual DSP processing not implemented

### ❌ Outstanding Tasks

**Critical (Required for Basic Functionality):**

1. **Audio Session Assignment UI** (PRIORITY 1)
   - Need dialog to show available audio applications
   - Allow user to assign applications to channels
   - Currently channels work but have no audio sessions assigned
   - Without this, MIDI controls don't affect any audio

2. **Apply Volume Changes to Audio Sessions** (PRIORITY 1)
   - UI sliders change ChannelViewModel.Volume
   - MIDI controls change ChannelViewModel.Volume
   - **Current**: This triggers ApplyVolumeToSessions()
   - **Status**: Working but requires sessions to be assigned first

3. **Test with Physical MIDI Device** (PRIORITY 2)
   - App is untested with real MIDI hardware
   - Need to verify CC message handling
   - Verify mapping mode works correctly

**Nice-to-Have Enhancements:**

4. **Profile Switcher UI**
   - Add combobox or menu for profile selection
   - "Save Current as Profile" button
   - Delete profile functionality

5. **Filter Effects DSP**
   - Implement BiQuadFilter or similar with NAudio
   - Wire up FilterConfiguration to actual audio processing
   - Add filter type selector (LowPass/HighPass/BandPass)

6. **Session Icons**
   - Extract and display application icons in channel strips
   - Show which apps are assigned to each channel

7. **Visual Feedback Improvements**
   - Animate channel when MIDI message received
   - Flash border or highlight on MIDI input
   - Better mapping mode indicator (maybe overlay)

8. **Multi-select Control Types in Mapping**
   - Currently maps to Volume by default
   - Add UI to choose: Volume, Pan, Filter Cutoff, Filter Resonance
   - Or automatically detect (fader=volume, knob=filter)

## How to Test MIDI Functionality

1. **Connect MIDI Device**:
   ```
   - Launch app: dotnet run
   - Select MIDI device from dropdown
   - Click "Connect"
   - Status should show "MIDI Connected"
   ```

2. **Map a Fader to Channel Volume**:
   ```
   - Click gear icon (⚙) on any channel
   - Channel enters mapping mode (green indicator)
   - Move a fader on your MIDI controller
   - Mapping is created and saved
   - Move the fader again - volume slider should move
   ```

3. **Map a Button to Mute**:
   ```
   - Click gear icon on a channel
   - Press a button on your MIDI controller
   - Mapping created for mute toggle
   - Press button again - mute button should toggle
   ```

4. **Assign Audio Application** (CURRENTLY MANUAL):
   ```
   - Open code: ViewModels/MainWindowViewModel.cs
   - In OnAudioSessionsChanged, manually add session to channel:
     Channels[0].AssignedSessions.Add(session)
   - This is temporary until Assignment UI is built
   ```

## Next Development Session

**Start Here:**
Implement Audio Session Assignment UI:
- Create new window/dialog (Views/SessionAssignmentDialog.xaml)
- Show list of AvailableSessions from MainWindowViewModel
- Allow clicking/dragging to assign to channels
- Add "Assign Session" button to channel strips
- This will make the app fully functional for volume control
