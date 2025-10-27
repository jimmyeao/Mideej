# Bug Fixes Summary

## Issues Fixed

### 1. ✅ Cancel Button Not Visible
**Problem**: The bottom status bar with the "Cancel" button was being rendered out of view due to incorrect XAML grid positioning.

**Root Cause**: `MainWindow.xaml` line 163 had the status bar in `Grid.Row="2"` with a broken margin `Margin="0,532,0,-27" Grid.RowSpan="2"`.

**Fix**: Changed to `Grid.Row="3"` and removed the incorrect margin.

**File**: `MainWindow.xaml:163`

---

### 2. ✅ Fader Mapping Not Working
**Problem**: Clicking the settings cog (⚙) entered a local mapping mode on the channel, but didn't actually enable MIDI capture in the MainWindowViewModel.

**Root Cause**: The ChannelViewModel's `EnterMappingMode` command only set a local flag `IsInMappingMode = true`, but didn't notify the MainWindowViewModel to start listening for MIDI input.

**Fix**:
1. Added `MappingModeRequested` event to `ChannelViewModel`
2. `EnterMappingMode` now fires this event
3. `MainWindowViewModel.SubscribeToChannelEvents` subscribes to this event
4. New handler `OnChannelMappingModeRequested` calls `StartMappingMode(channel)`

**Files Modified**:
- `ViewModels/ChannelViewModel.cs:63` - Added event
- `ViewModels/ChannelViewModel.cs:93` - Fire event in EnterMappingMode
- `ViewModels/MainWindowViewModel.cs:251` - Subscribe to event
- `ViewModels/MainWindowViewModel.cs:254` - New event handler

---

### 3. ✅ Enhanced Debug Logging for Fader Values
**Problem**: Fader values appeared inconsistent (showing 127/127 sometimes, 62/127 other times).

**Fix**: Added comprehensive debug logging to show:
- Raw pitch bend value (0-16383)
- Display value (0-127)
- Volume percentage (0-100%)
- Mapping mode state
- Whether mapping is being created

**File**: `ViewModels/MainWindowViewModel.cs:344-372`

**Debug Output Example**:
```
MIDI Pitch Bend: Ch0 RawValue=16383 Display=127/127 Volume=100.0%
  IsMappingModeActive=True, ChannelAwaitingMapping=Channel 1
  Creating fader mapping for channel Channel 1
```

This will help identify if the issue is:
- MIDI device sending different max values
- Pitch bend range not 0-16383
- Value conversion issue

---

## How to Test the Fixes

### Test 1: Cancel Button Visibility
1. Run the app
2. Click the settings cog (⚙) on any channel
3. **Expected**: Bottom status bar shows "⚙ MAPPING MODE ACTIVE" with a red "Cancel" button in the center
4. Click "Cancel"
5. **Expected**: Mapping mode exits, channel no longer shows "MAPPING..."

### Test 2: Fader Mapping
1. Ensure your DAW is **closed** (only one app can use MIDI at a time)
2. Connect to "MIDIIN2 (SMC MIXER)"
3. Click settings cog on Channel 1
4. **Expected**:
   - Channel 1 shows "MAPPING..." in green
   - Bottom status bar shows "⚙ MAPPING MODE ACTIVE" with Cancel button
   - Status message: "MAPPING MODE: Move a MIDI control to map to Channel 1"
5. Move Fader 1 on your controller
6. **Expected**:
   - Console shows detailed debug info
   - Green "MIDI Activity" indicator flashes
   - Status message: "Mapped Fader on MIDI CH1 to Channel 1"
   - Channel 1 exits mapping mode automatically
   - Cancel button disappears

### Test 3: Verify Fader Control
1. After mapping Fader 1 to Channel 1
2. Move Fader 1 up and down
3. **Expected**:
   - Channel 1 slider moves in sync
   - Volume percentage updates
   - Green MIDI activity flashes
   - Debug console shows pitch bend values

---

## Console Debug Output to Look For

When you click the settings cog:
```
=== Mapping mode requested for Channel 1 ===
=== MAPPING MODE STARTED for Channel 1 ===
```

When you move a fader during mapping mode:
```
MIDI Pitch Bend: Ch0 RawValue=8192 Display=64/127 Volume=50.0%
  IsMappingModeActive=True, ChannelAwaitingMapping=Channel 1
  Creating fader mapping for channel Channel 1
Created fader mapping: MIDI CH0 -> Channel 1
=== MAPPING MODE CANCELLED ===
```

When you move a mapped fader:
```
MIDI Pitch Bend: Ch0 RawValue=16383 Display=127/127 Volume=100.0%
  IsMappingModeActive=False, ChannelAwaitingMapping=null
```

---

## If Fader Values Still Inconsistent

The debug output will now show exactly what values are being received. If you see:
- **Raw value varies** (e.g., max is sometimes 16383, sometimes 8000) → MIDI device issue
- **Raw value consistent but display value wrong** → Conversion formula issue
- **Values jump around randomly** → Possible MIDI noise or resolution issue

Please share the console output if the issue persists!
