using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Mideej.Models;

/// <summary>
/// Represents a group of audio sessions from the same application
/// </summary>
public class AudioSessionGroup : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Process name (e.g., "spotify", "chrome")
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the application
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Number of sessions in this group
    /// </summary>
    public int SessionCount => Sessions.Count;

    /// <summary>
    /// All sessions in this group
    /// </summary>
    public ObservableCollection<AudioSessionInfo> Sessions { get; set; } = new();

    /// <summary>
    /// Whether all sessions in this group are selected
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;

                // Select/deselect all sessions in the group
                foreach (var session in Sessions)
                {
                    session.IsSelected = value;
                }

                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether the group is expanded to show individual sessions
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
