using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Mideej.Services;
using Mideej.ViewModels;

namespace Mideej;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Register services
        services.AddSingleton<IMidiService, MidiService>();
        services.AddSingleton<IAudioSessionManager, AudioSessionManager>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();

        // Register MainWindow
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load configuration
        var configService = _serviceProvider?.GetService<IConfigurationService>();
        if (configService != null)
        {
            await configService.LoadSettingsAsync();
        }

        // Create and show main window
        var mainWindow = _serviceProvider?.GetService<MainWindow>();
        if (mainWindow != null)
        {
            mainWindow.Show();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Save configuration before exiting
        var mainWindow = Current.MainWindow as MainWindow;
        if (mainWindow?.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SaveConfigurationAsync();
        }

        // Dispose services
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
