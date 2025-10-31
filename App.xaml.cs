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
        services.AddSingleton<IMediaControlService, MediaControlService>();

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

        // Initialize media control service
        var mediaControlService = _serviceProvider?.GetService<IMediaControlService>();
        if (mediaControlService != null)
        {
            await mediaControlService.InitializeAsync();
        }

        // Create and show main window
        var mainWindow = _serviceProvider?.GetService<MainWindow>();
        if (mainWindow != null)
        {
            // Start minimized purely based on saved setting (no switch required)
            bool shouldStartMinimized = configService?.CurrentSettings.StartMinimized == true;
            
            mainWindow.Show();

            if (shouldStartMinimized)
            {
                var minimizeToTray = configService?.CurrentSettings.MinimizeToTray == true;
                
                // Apply minimize state after window is fully shown to ensure tray icon initializes
                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    mainWindow.WindowState = WindowState.Minimized;
                    if (minimizeToTray)
                    {
                        mainWindow.ApplyStartupMinimizeToTray(true);
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
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
