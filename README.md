# Mideej

<p align="center">
  <img src="appicon.png" alt="Mideej Logo" width="200"/>
</p>

A powerful MIDI to Windows audio mixer controller for .NET 9. Mideej lets you control Windows application volumes and audio routing using MIDI controllers like faders, knobs, and buttons.

## Features

- 🎛️ **Multi-Channel Control**: Up to 8 simultaneous audio channels with individual volume control
- 🎹 **MIDI Controller Support**: Works with any MIDI device (tested with M-Vave SMC Mixer, Behringer X-Touch Mini, Novation Launchpad)
- 🎚️ **Real-time Fader Control**: Smooth, responsive volume control with visual feedback
- 🔇 **Mute & Solo**: Quick mute and solo buttons for each channel
- 📱 **Audio Application Mapping**: Assign any Windows audio application to any channel
- 🎨 **Modern UI**: Clean, dark-themed interface built with WPF
- 🔌 **Hot-plugging**: Connect and disconnect MIDI devices on the fly
- 💾 **Controller Presets**: Pre-configured mappings for popular MIDI controllers
- ⚡ **High Performance**: Built on .NET 9 for optimal performance and low latency

## Screenshots

### Main Interface
<img width="1212" height="720" alt="Main application window showing 8 channel faders" src="https://github.com/user-attachments/assets/933daa73-6c69-496e-9dcf-55f2a9b2fabb" />

### MIDI Controller Connection
<img width="900" height="700" alt="MIDI device selection and connection" src="https://github.com/user-attachments/assets/c567e3b7-5130-486e-83b2-3fb89e0bb57d" />

### Application Mapping
<img width="700" height="889" alt="Assigning audio applications to channels" src="https://github.com/user-attachments/assets/6f87bf85-d166-489a-b9be-a64ae18a5884" />

### Controller Configuration
<img width="700" height="500" alt="MIDI controller preset configuration" src="https://github.com/user-attachments/assets/353961e0-c229-4a7f-94d0-d533275b3ece" />

## Requirements

- Windows 10 version 1809 (build 17763) or later
- .NET 9.0 Runtime (Desktop)
- A MIDI controller (physical or virtual)

## Installation

### From Release

1. Download the latest release from the [Releases](https://github.com/jimmyeao/Mideej/releases) page
2. Extract the archive to a folder of your choice
3. Run `Mideej.exe`

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/jimmyeao/Mideej.git
   cd Mideej
   ```

2. Build the project:
   ```bash
   dotnet build -c Release
   ```

3. Run the application:
   ```bash
   dotnet run
   ```

## Getting Started

### 1. Connect Your MIDI Controller

1. Plug in your MIDI controller via USB
2. Launch Mideej
3. Click the **MIDI Device** dropdown at the top
4. Select your controller from the list
5. The status indicator will show "Connected" when successful

### 2. Map Audio Applications to Channels

1. Click the **gear icon** (⚙️) on any channel
2. A dropdown will appear showing all active audio applications
3. Select the application you want to control (e.g., Spotify, Discord, Chrome)
4. The channel name will update to show the mapped application

### 3. Configure MIDI Mappings (First Time)

If you're using a supported controller (M-Vave SMC Mixer, Behringer X-Touch Mini, or Novation Launchpad), presets are automatically loaded.

For custom controllers:
1. Move a fader/knob on your controller
2. Click the **"MAPPING..."** button that appears on the channel
3. The MIDI CC number will be learned automatically
4. Repeat for mute (M) and solo (S) buttons

### 4. Control Your Audio

- **Faders/Knobs**: Adjust application volume (0-100%)
- **M Button**: Mute/unmute the channel
- **S Button**: Solo the channel (mutes all others)
- **+ Channel Button**: Add additional channels (up to 8)

## Supported Controllers

Mideej includes presets for:
- **M-Vave SMC Mixer**: 8-channel motorized fader controller
- **Behringer X-Touch Mini**: Compact USB controller with encoders and buttons
- **Novation Launchpad**: Grid-based MIDI controller

Custom controller mappings are saved automatically and persist between sessions.

## Configuration Files

Controller presets are stored in `ControllerPresets/` as JSON files. You can create custom presets by copying and modifying existing ones.

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

## Technology Stack

- **.NET 9.0**: Modern C# with latest language features
- **WPF**: Windows Presentation Foundation for UI
- **NAudio**: MIDI and Windows audio session management
- **SkiaSharp**: High-performance 2D graphics rendering
- **CommunityToolkit.MVVM**: Modern MVVM patterns

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

See [LICENSE.txt](LICENSE.txt) for details.

## Acknowledgments

- Built with [NAudio](https://github.com/naudio/NAudio) for MIDI and audio control
- Uses [SkiaSharp](https://github.com/mono/SkiaSharp) for beautiful rendering
- Inspired by physical mixer consoles and the need for better Windows audio control

## Support

If you encounter any issues or have feature requests, please open an issue on the [GitHub Issues](https://github.com/jimmyeao/Mideej/issues) page.
