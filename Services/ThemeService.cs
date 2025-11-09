using Mideej.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Mideej.Services
{
    /// <summary>
    /// Service for managing theme loading, saving, importing, and exporting
    /// </summary>
    public class ThemeService
    {
        private readonly string _themesDirectory;
        private const string ThemeNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        public ThemeService()
        {
            // Use AppData for user-created themes
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _themesDirectory = Path.Combine(appDataPath, "Mideej", "Themes");

            // Ensure themes directory exists
            if (!Directory.Exists(_themesDirectory))
            {
                Directory.CreateDirectory(_themesDirectory);
            }
        }

        /// <summary>
        /// Load a theme from an XAML file (physical or embedded resource)
        /// </summary>
        public Theme? LoadThemeFromFile(string themeName)
        {
            try
            {
                XDocument? doc = null;

                // First, try to load from physical file (for custom themes)
                var filePath = Path.Combine(_themesDirectory, $"{themeName}.xaml");
                System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: Attempting to load {themeName}");

                if (File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: Loading from physical file: {filePath}");
                    doc = XDocument.Load(filePath);
                }
                else
                {
                    // Try to load from embedded application resources (for built-in themes)
                    try
                    {
                        var uri = new Uri($"pack://application:,,,/Themes/{themeName}.xaml", UriKind.Absolute);
                        System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: Trying embedded resource: {uri}");
                        var resourceInfo = Application.GetResourceStream(uri);
                        if (resourceInfo != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: Successfully loaded embedded resource for {themeName}");
                            using (var reader = new System.IO.StreamReader(resourceInfo.Stream))
                            {
                                var xamlContent = reader.ReadToEnd();
                                doc = XDocument.Parse(xamlContent);
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: GetResourceStream returned null for {themeName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Resource not found, return null
                        System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: Exception loading embedded resource: {ex.Message}");
                        return null;
                    }
                }

                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadThemeFromFile: doc is null for {themeName}");
                    return null;
                }

                var theme = new Theme { Name = themeName };

                // Parse colors from XML
                var ns = XNamespace.Get(ThemeNamespace);
                var xns = XNamespace.Get(XamlNamespace);

                foreach (var element in doc.Root?.Elements(ns + "Color") ?? Enumerable.Empty<XElement>())
                {
                    var key = element.Attribute(xns + "Key")?.Value;
                    var colorValue = element.Value;

                    if (key != null && !string.IsNullOrEmpty(colorValue))
                    {
                        var color = (Color)ColorConverter.ConvertFromString(colorValue);
                        SetThemeColor(theme, key, color);
                    }
                }

                // Set display name with emoji for built-in themes
                theme.DisplayName = GetDisplayNameForTheme(themeName);

                return theme;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading theme {themeName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save a theme to an XAML file
        /// </summary>
        public bool SaveTheme(Theme theme)
        {
            try
            {
                var filePath = Path.Combine(_themesDirectory, $"{theme.Name}.xaml");
                var xaml = GenerateThemeXaml(theme);
                File.WriteAllText(filePath, xaml);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving theme {theme.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export a theme to a user-specified location
        /// </summary>
        public bool ExportTheme(Theme theme, string destinationPath)
        {
            try
            {
                var xaml = GenerateThemeXaml(theme);
                File.WriteAllText(destinationPath, xaml);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting theme: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Import a theme from a user-specified file
        /// </summary>
        public Theme? ImportTheme(string sourcePath)
        {
            try
            {
                // Load the theme from the source path
                var doc = XDocument.Load(sourcePath);
                var fileName = Path.GetFileNameWithoutExtension(sourcePath);
                var theme = new Theme { Name = fileName };

                // Parse colors from XML
                var ns = XNamespace.Get(ThemeNamespace);
                var xns = XNamespace.Get(XamlNamespace);

                foreach (var element in doc.Root?.Elements(ns + "Color") ?? Enumerable.Empty<XElement>())
                {
                    var key = element.Attribute(xns + "Key")?.Value;
                    var colorValue = element.Value;

                    if (key != null && !string.IsNullOrEmpty(colorValue))
                    {
                        var color = (Color)ColorConverter.ConvertFromString(colorValue);
                        SetThemeColor(theme, key, color);
                    }
                }

                theme.DisplayName = fileName.Replace("Theme", "");
                if (string.IsNullOrEmpty(theme.DisplayName))
                    theme.DisplayName = fileName;

                // Copy to themes directory
                var destPath = Path.Combine(_themesDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destPath, overwrite: true);

                return theme;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing theme: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all available theme files (both physical and embedded)
        /// </summary>
        public List<string> GetAvailableThemes()
        {
            var themes = new HashSet<string>();

            try
            {
                // Add physical theme files from Themes directory
                if (Directory.Exists(_themesDirectory))
                {
                    var physicalThemes = Directory.GetFiles(_themesDirectory, "*Theme.xaml")
                        .Select(f => Path.GetFileNameWithoutExtension(f));
                    foreach (var theme in physicalThemes)
                    {
                        if (theme != null)
                            themes.Add(theme);
                    }
                }

                // Add built-in embedded themes
                var builtInThemes = new[]
                {
                    "DarkTheme", "LightTheme", "NordTheme", "DraculaTheme",
                    "OceanTheme", "SunsetTheme", "CyberpunkTheme", "ForestTheme",
                    "ArcticTheme", "HalloweenTheme", "ChristmasTheme", "DiwaliTheme",
                    "HanukkahTheme", "EidTheme", "LunarNewYearTheme", "EasterTheme",
                    "NowruzTheme", "RamadanTheme", "PrideTheme"
                };

                foreach (var theme in builtInThemes)
                {
                    themes.Add(theme);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting available themes: {ex.Message}");
            }

            return themes.ToList();
        }

        /// <summary>
        /// Validate if a theme name is available
        /// </summary>
        public bool IsThemeNameAvailable(string themeName, string? excludeTheme = null)
        {
            // If we're editing the same theme (name hasn't changed), it's always available
            if (excludeTheme != null && themeName == excludeTheme)
                return true;

            // Check if a physical file already exists
            var filePath = Path.Combine(_themesDirectory, $"{themeName}.xaml");

            // If physical file exists and it's not the excluded theme, name is not available
            if (File.Exists(filePath))
                return false;

            // For new themes or editing built-in themes, the name is available
            // (editing a built-in theme will create a new physical file that overrides the embedded one)
            return true;
        }

        /// <summary>
        /// Generate XAML content for a theme
        /// </summary>
        private string GenerateThemeXaml(Theme theme)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine();
            sb.AppendLine($"    <!-- {theme.DisplayName} Theme Colors -->");

            // Add color definitions
            AppendColor(sb, "BackgroundColor", theme.BackgroundColor);
            AppendColor(sb, "SurfaceColor", theme.SurfaceColor);
            AppendColor(sb, "SurfaceHighlightColor", theme.SurfaceHighlightColor);
            AppendColor(sb, "PrimaryColor", theme.PrimaryColor);
            AppendColor(sb, "PrimaryDarkColor", theme.PrimaryDarkColor);
            AppendColor(sb, "AccentColor", theme.AccentColor);
            AppendColor(sb, "TextPrimaryColor", theme.TextPrimaryColor);
            AppendColor(sb, "TextSecondaryColor", theme.TextSecondaryColor);
            AppendColor(sb, "BorderColor", theme.BorderColor);
            AppendColor(sb, "ErrorColor", theme.ErrorColor);
            AppendColor(sb, "WarningColor", theme.WarningColor);
            AppendColor(sb, "SuccessColor", theme.SuccessColor);

            sb.AppendLine();
            sb.AppendLine("    <!-- Brushes -->");

            // Add brush definitions
            AppendBrush(sb, "BackgroundBrush", "BackgroundColor");
            AppendBrush(sb, "SurfaceBrush", "SurfaceColor");
            AppendBrush(sb, "SurfaceHighlightBrush", "SurfaceHighlightColor");
            AppendBrush(sb, "PrimaryBrush", "PrimaryColor");
            AppendBrush(sb, "PrimaryDarkBrush", "PrimaryDarkColor");
            AppendBrush(sb, "AccentBrush", "AccentColor");
            AppendBrush(sb, "TextPrimaryBrush", "TextPrimaryColor");
            AppendBrush(sb, "TextSecondaryBrush", "TextSecondaryColor");
            AppendBrush(sb, "TextBrush", "TextPrimaryColor");
            AppendBrush(sb, "BorderBrush", "BorderColor");
            AppendBrush(sb, "ErrorBrush", "ErrorColor");
            AppendBrush(sb, "WarningBrush", "WarningColor");
            AppendBrush(sb, "SuccessBrush", "SuccessColor");

            sb.AppendLine();
            sb.AppendLine("</ResourceDictionary>");

            return sb.ToString();
        }

        private void AppendColor(StringBuilder sb, string key, Color color)
        {
            sb.AppendLine($"    <Color x:Key=\"{key}\">#{color.R:X2}{color.G:X2}{color.B:X2}</Color>");
        }

        private void AppendBrush(StringBuilder sb, string brushKey, string colorKey)
        {
            sb.AppendLine($"    <SolidColorBrush x:Key=\"{brushKey}\" Color=\"{{StaticResource {colorKey}}}\"/>");
        }

        /// <summary>
        /// Get display name with emoji for built-in themes
        /// </summary>
        private string GetDisplayNameForTheme(string themeName)
        {
            return themeName switch
            {
                "DarkTheme" => "Dark 🌙",
                "LightTheme" => "Light ☀️",
                "NordTheme" => "Nord 🌊",
                "DraculaTheme" => "Dracula 🦇",
                "OceanTheme" => "Ocean 🌅",
                "SunsetTheme" => "Sunset 🌇",
                "CyberpunkTheme" => "Cyberpunk 🌈",
                "ForestTheme" => "Forest 🌿",
                "ArcticTheme" => "Arctic ❄️",
                "HalloweenTheme" => "Halloween 🎃",
                "ChristmasTheme" => "Christmas 🎄",
                "DiwaliTheme" => "Diwali 🪔",
                "HanukkahTheme" => "Hanukkah 🕎",
                "EidTheme" => "Eid 🌙",
                "LunarNewYearTheme" => "Lunar New Year 🧧",
                "EasterTheme" => "Easter 🐣",
                "NowruzTheme" => "Nowruz 🌱",
                "RamadanTheme" => "Ramadan 🌙",
                "PrideTheme" => "Pride 🏳️‍🌈",
                _ => themeName.Replace("Theme", "").Trim()
            };
        }

        private void SetThemeColor(Theme theme, string key, Color color)
        {
            switch (key)
            {
                case "BackgroundColor":
                    theme.BackgroundColor = color;
                    break;
                case "SurfaceColor":
                    theme.SurfaceColor = color;
                    break;
                case "SurfaceHighlightColor":
                    theme.SurfaceHighlightColor = color;
                    break;
                case "PrimaryColor":
                    theme.PrimaryColor = color;
                    break;
                case "PrimaryDarkColor":
                    theme.PrimaryDarkColor = color;
                    break;
                case "AccentColor":
                    theme.AccentColor = color;
                    break;
                case "TextPrimaryColor":
                    theme.TextPrimaryColor = color;
                    break;
                case "TextSecondaryColor":
                    theme.TextSecondaryColor = color;
                    break;
                case "BorderColor":
                    theme.BorderColor = color;
                    break;
                case "ErrorColor":
                    theme.ErrorColor = color;
                    break;
                case "WarningColor":
                    theme.WarningColor = color;
                    break;
                case "SuccessColor":
                    theme.SuccessColor = color;
                    break;
            }
        }
    }
}
