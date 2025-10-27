using Mideej.Models;

namespace Mideej.Services;

/// <summary>
/// Service for managing application configuration and persistence
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads application settings from disk
    /// </summary>
    Task<AppSettings> LoadSettingsAsync();

    /// <summary>
    /// Saves application settings to disk
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>
    /// Saves the current settings
    /// </summary>
    Task SaveCurrentSettingsAsync();

    /// <summary>
    /// Gets the current application settings
    /// </summary>
    AppSettings CurrentSettings { get; }

    /// <summary>
    /// Creates a new profile from current settings
    /// </summary>
    void SaveProfile(string profileName);

    /// <summary>
    /// Loads a profile
    /// </summary>
    void LoadProfile(string profileName);

    /// <summary>
    /// Deletes a profile
    /// </summary>
    void DeleteProfile(string profileName);

    /// <summary>
    /// Gets all profile names
    /// </summary>
    List<string> GetProfileNames();

    /// <summary>
    /// Exports settings to a file
    /// </summary>
    Task ExportSettingsAsync(string filePath);

    /// <summary>
    /// Imports settings from a file
    /// </summary>
    Task<AppSettings> ImportSettingsAsync(string filePath);
}
