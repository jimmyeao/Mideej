# Button Mapping Guide

## What's New

I've added full support for mapping all 4 button types on your M-Vave SMC8 controller:
- **Mute** buttons (Notes 16-23)
- **Solo** buttons (Notes 8-15)
- **Record** buttons (Notes 0-7)
- **Select** buttons (Notes 24-31)

## How Button Auto-Detection Works

When you press a button during mapping mode, the app **automatically detects** which type of button it is based on the MIDI note number:

| Button Type | Note Range | Function |
|-------------|------------|----------|
| **Record** | 0-7 | Placeholder (shows message) |
| **Solo** | 8-15 | Toggles channel solo state |
| **Mute** | 16-23 | Toggles channel mute state |
| **Select** | 24-31 | Placeholder (shows message) |

## How to Map Buttons

### Example: Map Mute Button to Channel 1

1. **Click the settings cog (⚙)** on Channel 1
2. **Press the Mute button** for Channel 1 on your controller (Note 16)
3. Console shows:
   ```
   MIDI Note On: Ch0 Note#16 Vel127
   Auto-detected button type: Mute
   Mapped MIDI CH0 Note16 to Channel 1 Mute
   ```
4. **Mapping complete!** The LED on the button should light up

### Example: Map Solo Button to Channel 2

1. **Click the settings cog (⚙)** on Channel 2
2. **Press the Solo button** for Channel 2 on your controller (Note 9)
3. Console shows:
   ```
   MIDI Note On: Ch0 Note#9 Vel127
   Auto-detected button type: Solo
   Mapped MIDI CH0 Note9 to Channel 2 Solo
   ```
4. **Mapping complete!**

## Button Functionality

### ✅ Fully Implemented

**Mute Button:**
- Toggles channel mute state
- Updates all assigned audio sessions
- Sends LED feedback to controller (LED on when muted)
- Console output: `Mute toggled for Channel 1: True`

**Solo Button:**
- Toggles channel solo state
- Applies solo logic (mutes all non-soloed channels)
- Sends LED feedback to controller (LED on when soloed)
- Console output: `Solo toggled for Channel 1: True`

### 📋 Placeholder (To Be Implemented)

**Record Button:**
- Currently shows status message: "Channel 1 - Record button pressed (not yet implemented)"
- Lights up the LED
- Console output: `Record button pressed for Channel 1`
- **Future**: Could be used to arm channels for recording

**Select Button:**
- Currently shows status message: "Channel 1 - Select button pressed (not yet implemented)"
- Lights up the LED
- Console output: `Select button pressed for Channel 1`
- **Future**: Could be used to select/highlight channels for editing

## LED Feedback

The app sends MIDI messages back to your controller to update button LEDs:

- **Mute LED**: ON when channel is muted, OFF when unmuted
- **Solo LED**: ON when channel is soloed, OFF when unsoloed
- **Record LED**: Blinks when pressed (placeholder)
- **Select LED**: Blinks when pressed (placeholder)

## Testing All Button Types

### Test Mute and Solo
1. Map Mute button to a channel → Press it → Channel should mute/unmute
2. Map Solo button to a channel → Press it → Channel should solo (other channels mute)
3. Verify LEDs light up when active

### Test Record and Select (Placeholders)
1. Map Record button → Press it → Status message shows "Record button pressed"
2. Map Select button → Press it → Status message shows "Select button pressed"
3. LEDs should blink briefly

## Complete Mapping Example

Map all controls for Channel 1 (assuming Mackie mode with 1-based indexing):

1. **Fader** (Pitch Bend on MIDI Ch1): Controls volume
2. **Knob** (CC#16): Ready to map to any parameter
3. **Record** (Note 0): Placeholder
4. **Solo** (Note 8): Toggles solo
5. **Mute** (Note 16): Toggles mute
6. **Select** (Note 24): Placeholder

## What Changed

### Files Modified

1. **Models/MidiMapping.cs**
   - Added `Record` and `Select` to `MidiControlType` enum

2. **ViewModels/MainWindowViewModel.cs**
   - Auto-detect button type from note number in `OnMidiNoteOn`
   - Handle `Record` and `Select` in `ApplyMidiNote` switch statement
   - Added debug logging for all button presses

3. **MainWindow.xaml.cs**
   - Added `Closing` event handler to save configuration on exit
   - **Fixes channels disappearing on restart!**

## Configuration Persistence

All button mappings are now automatically saved when you close the app:
- Saved to: `%AppData%\Mideej\appsettings.json`
- Includes fader mappings, button mappings, and channel configurations
- Automatically loaded on startup

## Next Steps

When you're ready to implement Record and Select functionality, you can:
- **Record**: Add channel recording/arming state to `ChannelViewModel`
- **Select**: Add channel selection/highlighting in the UI

For now, the placeholders confirm that the buttons are working and properly mapped!
