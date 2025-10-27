using CommunityToolkit.Mvvm.ComponentModel;

namespace Mideej.ViewModels;

/// <summary>
/// Base class for all ViewModels in the application
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Called when the ViewModel is initialized
    /// </summary>
    public virtual void OnNavigatedTo()
    {
    }

    /// <summary>
    /// Called when navigating away from the ViewModel
    /// </summary>
    public virtual void OnNavigatedFrom()
    {
    }
}
