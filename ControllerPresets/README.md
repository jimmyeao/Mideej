# MIDI Controller Presets

This folder contains premade MIDI controller mappings for popular MIDI controllers.

## Available Presets

### Behringer X-Touch Mini
- **File**: `Behringer-X-Touch-Mini.json`
- **Mappings**:
  - Rotary encoders 1-8: Volume control for channels 1-8
  - Buttons 8-11 (Layer A): Mute buttons for channels 1-4
  - Transport buttons: Play, Pause, Previous, Next

### Novation Launchpad
- **File**: `Novation-Launchpad.json`
- **Mappings**:
  - Top row pads (0-7): Mute buttons for channels 1-8
  - Second row pads (16-19): Solo buttons for channels 1-4
  - Top-right buttons (104-105): Play and Pause

## How to Use

1. Click the **Import** button (📥) in the Mideej toolbar
2. Select the preset JSON file for your controller
3. Choose whether to replace existing mappings or merge them
4. Choose whether to import channel configurations
5. Click "Yes" to confirm

## Creating Your Own Presets

You can create custom presets by:

1. Setting up your MIDI mappings in Mideej using the mapping mode
2. Clicking the **Export** button (📤) in the toolbar
3. Saving the configuration file
4. Sharing it with others or using it as a template

### Preset File Format

```json
{
  "ControllerName": "Your Controller Name",
  "Description": "Brief description of the preset",
  "MidiMappings": [
    {
      "Channel": 0,
      "ControlNumber": 1,
      "TargetChannelIndex": 0,
      "ControlType": "Volume",
      "MinValue": 0.0,
      "MaxValue": 1.0,
      "IsInverted": false
    }
  ],
  "Channels": []
}
```

### Control Types
- `Volume` - Fader/knob for volume control
- `Mute` - Toggle button for mute
- `Solo` - Toggle button for solo
- `Record` - Toggle button for recording indicator
- `Select` - Toggle button for channel selection
- `TransportPlay` - Global play button
- `TransportPause` - Global pause button
- `TransportNext` - Global next track button
- `TransportPrevious` - Global previous track button

### Special Values
- `TargetChannelIndex`: Use `-1` for global transport controls
- `Channel`: MIDI channel (0-15, where 0 is channel 1)
- `ControlNumber`: 
  - For CC: Use the CC number (0-127)
  - For Note: Use the note number (0-127)
  - For Pitch Bend: Use `-1`

## Contributing Presets

If you'd like to share a preset for a popular controller, please:

1. Test it thoroughly with your controller
2. Add a descriptive name and documentation
3. Create a pull request or share it with the community

## Tips

- Use the MIDI Monitor in Mideej to identify your controller's CC/Note numbers
- Start with small mappings and test before building complex configurations
- Controllers in Mackie Control mode may use Pitch Bend for faders (use ControlNumber: -1)
- Some controllers may need to be set to a specific mode (e.g., "User Mode") to send standard MIDI messages
