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
}
