using System.Windows.Media;

namespace Mideej.Models
{
    /// <summary>
    /// Represents a complete theme with all color properties
    /// </summary>
    public class Theme
    {
        public string Name { get; set; } = "CustomTheme";
        public string DisplayName { get; set; } = "Custom Theme";

        // Core Colors
        public Color BackgroundColor { get; set; } = Color.FromRgb(30, 30, 46);
        public Color SurfaceColor { get; set; } = Color.FromRgb(42, 42, 60);
        public Color SurfaceHighlightColor { get; set; } = Color.FromRgb(54, 54, 80);
        public Color PrimaryColor { get; set; } = Color.FromRgb(59, 130, 246);
        public Color PrimaryDarkColor { get; set; } = Color.FromRgb(37, 99, 235);
        public Color AccentColor { get; set; } = Color.FromRgb(16, 185, 129);
        public Color TextPrimaryColor { get; set; } = Color.FromRgb(229, 231, 235);
        public Color TextSecondaryColor { get; set; } = Color.FromRgb(156, 163, 175);
        public Color BorderColor { get; set; } = Color.FromRgb(64, 64, 80);
        public Color ErrorColor { get; set; } = Color.FromRgb(239, 68, 68);
        public Color WarningColor { get; set; } = Color.FromRgb(245, 158, 11);
        public Color SuccessColor { get; set; } = Color.FromRgb(16, 185, 129);

        /// <summary>
        /// Creates a clone of this theme
        /// </summary>
        public Theme Clone()
        {
            return new Theme
            {
                Name = this.Name,
                DisplayName = this.DisplayName,
                BackgroundColor = this.BackgroundColor,
                SurfaceColor = this.SurfaceColor,
                SurfaceHighlightColor = this.SurfaceHighlightColor,
                PrimaryColor = this.PrimaryColor,
                PrimaryDarkColor = this.PrimaryDarkColor,
                AccentColor = this.AccentColor,
                TextPrimaryColor = this.TextPrimaryColor,
                TextSecondaryColor = this.TextSecondaryColor,
                BorderColor = this.BorderColor,
                ErrorColor = this.ErrorColor,
                WarningColor = this.WarningColor,
                SuccessColor = this.SuccessColor
            };
        }
    }
}
