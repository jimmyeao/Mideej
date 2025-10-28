# Configuration Backup & Protection System

## Overview
Mideej now includes automatic backup and validation to protect your configuration from accidental loss.

## Features

### 🛡️ **Automatic Backups**
Every time settings are saved, Mideej automatically:
1. Creates a timestamped backup (e.g., `settings.20251028-213000.json`)
2. Maintains a "latest" backup (`settings.backup.json`)
3. Keeps the 5 most recent backups, auto-deleting older ones

**Backup Location:** `%AppData%\Mideej\backups\`

### ✅ **Save Validation**
Before saving, Mideej validates your configuration:
- **Prevents saving null/corrupted configs**
- **Prevents accidental data loss** - Won't save an empty config if you previously had channels/mappings
- **Allows intentional clearing** - You can still manually delete all channels/mappings if needed

If validation fails, the save is blocked and your existing config is preserved.

### 🔄 **Automatic Restore**
If Mideej detects a corrupted or empty config on startup:
1. **Automatically attempts to restore** from the most recent backup
2. **Notifies you** if restoration succeeds or fails
3. **Falls back to defaults** if no backup is available

### 📋 **Console Logging**
All backup operations are logged to the console:
- `Settings saved successfully (8 channels, 24 mappings)`
- `Warning: Refusing to save invalid/empty settings. Keeping existing config.`
- `Successfully restored from backup (8 channels, 24 mappings)`

## How It Works

### Save Process
```
User saves config
    ↓
Validate settings
    ↓ (valid)
Create backup (timestamped + latest)
    ↓
Save new config
    ↓
Cleanup old backups (keep 5)
```

### Load Process
```
Load config from disk
    ↓
Config empty/corrupted?
    ↓ (yes)
Restore from backup
    ↓
Load restored config or use defaults
```

## Configuration

### Maximum Backups
By default, Mideej keeps the 5 most recent backups. This is defined in `ConfigurationService.cs`:
```csharp
private const int MaxBackups = 5;
```

### Backup Files
- **Timestamped:** `settings.YYYYMMDD-HHMMSS.json` (e.g., `settings.20251028-213445.json`)
- **Latest:** `settings.backup.json` (always points to the most recent backup)

## Manual Restore

To manually restore from a backup:
1. Navigate to `%AppData%\Mideej\backups\`
2. Find the backup you want to restore (check timestamps)
3. Copy it to `%AppData%\Mideej\settings.json`
4. Restart Mideej

## Troubleshooting

### "Refusing to save invalid/empty settings"
This means Mideej detected you're about to lose data. Check:
- Are your channels still visible in the UI?
- Did something clear your mappings unexpectedly?
- Check console for more details

**Solution:** Close and restart Mideej. It should auto-restore from backup.

### "No backup available or backup restore failed"
This happens on first run or if all backups are corrupted.
**Solution:** You'll need to manually reconfigure or restore from an exported controller config.

### Backups Taking Up Space
Mideej automatically manages backups:
- Only keeps 5 most recent backups
- Each backup is typically < 10KB
- Old backups are auto-deleted

## Best Practices

1. **Export important configs** - Use File > Export Controller Config for major milestones
2. **Check console output** - Watch for backup/restore messages
3. **Don't panic on empty config** - Mideej will auto-restore on next launch
4. **Test your backups** - Occasionally check `%AppData%\Mideej\backups\` to ensure they're being created

## Technical Details

### Validation Rules
A config is considered valid if:
- It's not null
- Either:
  - It has channels OR mappings, OR
  - The previous config was also empty (intentional clear)

### Backup Timing
Backups are created:
- Before every save operation
- Only if a settings file exists
- Even if the new save might fail

This ensures you always have a recovery point.

## Code Locations

- **Backup Logic:** `Services/ConfigurationService.cs` (lines 290-392)
- **Validation:** `ValidateSettings()` method
- **Auto-Restore:** `ViewModels/MainWindowViewModel.cs` LoadConfiguration method (lines 1225-1240)
