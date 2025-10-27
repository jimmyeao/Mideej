# MIDI Troubleshooting Guide

## Why Am I Not Receiving MIDI Messages?

### **MOST COMMON ISSUE: MIDI Device Already in Use**

**Windows only allows ONE application to access a MIDI device at a time.**

If your MIDI controller is open in your DAW (Digital Audio Workstation), Mideej cannot receive messages from it.

## How to Fix

### Option 1: Close Your DAW (Recommended for Testing)
1. Close your DAW completely
2. In Mideej, click the 🔄 (Refresh) button
3. Select your MIDI device from the dropdown
4. Click "Connect"
5. Look for the visual MIDI activity indicator in the bottom-left status bar
6. Move a fader/knob on your MIDI controller
7. You should see:
   - A green "MIDI Activity" indicator flash
   - The status message show the MIDI data received

### Option 2: Use Virtual MIDI Ports (For DAW Integration)
1. Install a virtual MIDI port driver like [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html)
2. Create a virtual MIDI port
3. Route your DAW's MIDI output to the virtual port
4. Connect Mideej to the virtual port
5. This allows both applications to work simultaneously

## Which Device Should I Choose?

If you see multiple devices like:
- "SMC Mixer"
- "MIDIIN2 (SMC MIXER)"

**Choose the one with "MIDIIN" in the name** - this is the input device.

## How to Tell If It's Working

When Mideej successfully receives MIDI messages, you will see:

1. **Visual Indicator**: A green dot and "MIDI Activity" text in the bottom-left status bar
2. **Status Message**: The center status bar will briefly show the MIDI data (e.g., "🎵 MIDI CC: Ch0 CC#1 = 64")
3. **Console Output** (if running from command line): MIDI data printed to console

## Testing Steps

1. **Close all other MIDI applications** (DAWs, virtual instruments, etc.)
2. **Refresh MIDI devices** in Mideej
3. **Connect** to the MIDIIN device
4. **Click the settings cog** on Channel 1 to enter mapping mode
5. **Move a fader or knob** on your MIDI controller
6. You should see:
   - Green MIDI activity flash
   - The mapping complete automatically
   - Channel 1 no longer shows "MAPPING..."

## Cancel Mapping Mode

If you enter mapping mode and want to exit without mapping:
- Click the **"Cancel"** button in the bottom status bar (center)
- The red cancel button appears when mapping mode is active

## Still Not Working?

1. Check Device Manager (Windows) - is your MIDI device recognized?
2. Try disconnecting and reconnecting your MIDI controller physically
3. Restart Mideej
4. Check if Windows audio/MIDI services are running
