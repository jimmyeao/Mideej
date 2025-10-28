using System.IO;
using System.Text.Json;
using Mideej.Models;

namespace Mideej.Services;

/// <summary>
/// Service for managing application configuration and persistence
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly string _settingsPath;
    private AppSettings _currentSettings;

    public AppSettings CurrentSettings => _currentSettings;

    public ConfigurationService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var mideejPath = Path.Combine(appDataPath, "Mideej");
        Directory.CreateDirectory(mideejPath);

        _settingsPath = Path.Combine(mideejPath, "settings.json");
        _currentSettings = new AppSettings();
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    _currentSettings = settings;
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error (could add logging service later)
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }

        // Return default settings if load fails
        _currentSettings = new AppSettings();
        return _currentSettings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            _currentSettings = settings;
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"Error saving settings: {ex.Message}");
            throw;
        }
    }

    public async Task SaveCurrentSettingsAsync()
    {
        await SaveSettingsAsync(_currentSettings);
    }

    public void SaveProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("Profile name cannot be empty", nameof(profileName));

        var profile = new Profile
        {
            Name = profileName,
            Channels = new List<ChannelConfiguration>(_currentSettings.Channels),
            MidiMappings = new List<MidiMapping>(_currentSettings.MidiMappings)
        };

        _currentSettings.Profiles[profileName] = profile;
        _currentSettings.ActiveProfile = profileName;
    }

    public void LoadProfile(string profileName)
    {
        if (!_currentSettings.Profiles.ContainsKey(profileName))
            throw new ArgumentException($"Profile '{profileName}' not found", nameof(profileName));

        var profile = _currentSettings.Profiles[profileName];
        _currentSettings.Channels = new List<ChannelConfiguration>(profile.Channels);
        _currentSettings.MidiMappings = new List<MidiMapping>(profile.MidiMappings);
        _currentSettings.ActiveProfile = profileName;
    }

    public void DeleteProfile(string profileName)
    {
        if (_currentSettings.Profiles.ContainsKey(profileName))
        {
            _currentSettings.Profiles.Remove(profileName);

            if (_currentSettings.ActiveProfile == profileName)
            {
                _currentSettings.ActiveProfile = null;
            }
        }
    }

    public List<string> GetProfileNames()
    {
        return _currentSettings.Profiles.Keys.ToList();
    }

    public async Task ExportSettingsAsync(string filePath)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_currentSettings, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting settings: {ex.Message}");
            throw;
        }
    }

    public async Task<AppSettings> ImportSettingsAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Settings file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings == null)
                throw new InvalidOperationException("Failed to deserialize settings");

            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing settings: {ex.Message}");
            throw;
        }
    }

    public async Task ExportControllerConfigAsync(string filePath, string controllerName, bool includeChannels = false)
    {
        try
        {
            var config = new ControllerConfig
            {
                ControllerName = controllerName,
                MidiMappings = new List<MidiMapping>(_currentSettings.MidiMappings),
                Channels = includeChannels ? new List<ChannelConfiguration>(_currentSettings.Channels) : null,
                ModifiedAt = DateTime.UtcNow
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting controller config: {ex.Message}");
            throw;
        }
    }

    public async Task<ControllerConfig> ImportControllerConfigAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Controller config file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<ControllerConfig>(json);

            if (config == null)
                throw new InvalidOperationException("Failed to deserialize controller config");

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing controller config: {ex.Message}");
            throw;
        }
    }

    public void ApplyControllerConfig(ControllerConfig config, bool replaceExisting = true, bool applyChannels = false)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // Handle MIDI mappings
        if (replaceExisting)
        {
            _currentSettings.MidiMappings = new List<MidiMapping>(config.MidiMappings);
        }
        else
        {
            // Merge: only add mappings that don't conflict with existing ones
            foreach (var mapping in config.MidiMappings)
            {
                var existingMapping = _currentSettings.MidiMappings.FirstOrDefault(m =>
                    m.Channel == mapping.Channel && m.ControlNumber == mapping.ControlNumber);

                if (existingMapping != null)
                {
                    // Update existing mapping
                    var index = _currentSettings.MidiMappings.IndexOf(existingMapping);
                    _currentSettings.MidiMappings[index] = mapping;
                }
                else
                {
                    // Add new mapping
                    _currentSettings.MidiMappings.Add(mapping);
                }
            }
        }

        // Handle channel configurations if requested
        if (applyChannels && config.Channels != null)
        {
            if (replaceExisting)
            {
                _currentSettings.Channels = new List<ChannelConfiguration>(config.Channels);
            }
            else
            {
                // Merge: update channels by index
                foreach (var channel in config.Channels)
                {
                    var existingChannel = _currentSettings.Channels.FirstOrDefault(c => c.Index == channel.Index);
                    if (existingChannel != null)
                    {
                        var index = _currentSettings.Channels.IndexOf(existingChannel);
                        _currentSettings.Channels[index] = channel;
                    }
                    else
                    {
                        _currentSettings.Channels.Add(channel);
                    }
                }
            }
        }
    }
}
