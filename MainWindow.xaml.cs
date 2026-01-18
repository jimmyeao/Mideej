using System.Windows;
using System.Windows.Input;
using Mideej.ViewModels;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Mideej;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private NotifyIcon? _notifyIcon;
    private bool _isClosing = false;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private double _previousLeft;
    private double _previousTop;
    private double _previousWidth;
    private double _previousHeight;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        KeyDown += MainWindow_KeyDown;
        Loaded += (s, e) => ApplyFontSize(viewModel.FontSizeScale);
        viewModel.FontSizeChanged += (s, e) => ApplyFontSize(viewModel.FontSizeScale);
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        InitializeTrayIcon();
    }

    // Called by App.xaml.cs to ensure that a startup-minimized window is hidden to tray immediately
    public void ApplyStartupMinimizeToTray(bool minimizeToTray)
    {
        if (WindowState == WindowState.Minimized && minimizeToTray)
        {
            MinimizeToTrayNow(showBalloon: false);
        }
    }

    private void MinimizeToTrayNow(bool showBalloon)
    {
        ShowInTaskbar = false;
        Hide();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
            if (showBalloon)
            {
                _notifyIcon.ShowBalloonTip(1000, "Mideej", "Minimized to tray. Double-click to restore.", ToolTipIcon.None);
            }
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            Icon icon;
            try
            {
                var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appicon.ico");
                icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application;
            }
            catch
            {
                icon = SystemIcons.Application;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "Mideej - MIDI Audio Mixer",
                Visible = false
            };
        }
        catch (Exception ex)
        {
            // If tray icon initialization fails, log and continue without tray support
            Console.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            _notifyIcon = null;
            return;
        }

        _notifyIcon.DoubleClick += (s, e) =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            _notifyIcon.Visible = false;
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            _notifyIcon.Visible = false;
        });
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) =>
        {
            _isClosing = true;
            Close();
        });
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (WindowState == WindowState.Minimized && viewModel.MinimizeToTray)
            {
                MinimizeToTrayNow(showBalloon: true);
            }
            else if ((WindowState == WindowState.Normal || WindowState == WindowState.Maximized))
            {
                // Ensure taskbar shows again when restored
                ShowInTaskbar = true;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                }
            }
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Console.WriteLine("[MainWindow.Closing] Window closing event triggered");

        // If user clicked the window close button (not tray Exit), just minimize to tray
        if (!_isClosing && DataContext is MainWindowViewModel vmSettings && vmSettings.MinimizeToTray)
        {
            e.Cancel = true;
            // Mirror minimize behavior: hide to tray without balloon (user explicitly closed)
            MinimizeToTrayNow(showBalloon: false);
            Console.WriteLine("[MainWindow.Closing] Intercepted close, minimized to tray instead");
            return;
        }

        // Real application exit path (tray Exit or MinimizeToTray disabled)
        if (DataContext is MainWindowViewModel vm)
        {
            try
            {
                Console.WriteLine("[MainWindow.Closing] Calling TurnOffAllLeds()");
                vm.TurnOffAllLeds();
                // Give MIDI messages time to be sent
                System.Threading.Thread.Sleep(100);
                Console.WriteLine("[MainWindow.Closing] LED cleanup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow.Closing] Error turning off LEDs: {ex.Message}");
            }

            await vm.SaveConfigurationAsync();
        }

        _notifyIcon?.Dispose();
        Console.WriteLine("[MainWindow.Closing] Window closing handler complete");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore();
        }
        else
        {
            try
            {
                // Only allow dragging if window state is Normal or Maximized
                // Maximized state will be handled by DragMove - it will restore and then drag
                if (WindowState != WindowState.Minimized && e.LeftButton == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
            catch (Exception ex)
            {
                // DragMove can throw InvalidOperationException if called at wrong time
                // Log but don't crash the app
                Console.WriteLine($"Error during window drag: {ex.Message}");
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        MaximizeRestore();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Route close button through the same logic as window closing
        if (DataContext is MainWindowViewModel vm && vm.MinimizeToTray)
        {
            WindowState = WindowState.Minimized;
            MinimizeToTrayNow(showBalloon: false);
        }
        else
        {
            Close();
        }
    }

    private void MaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void MapTransportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.IsOpen = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsFullScreenMode) && DataContext is MainWindowViewModel viewModel)
        {
            if (viewModel.IsFullScreenMode)
            {
                EnterFullScreen();
            }
            else
            {
                ExitFullScreen();
            }
        }
    }

    private void EnterFullScreen()
    {
        // Save current window state
        _previousWindowState = WindowState;
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _previousLeft = Left;
        _previousTop = Top;
        _previousWidth = Width;
        _previousHeight = Height;

        // Set window to cover entire screen including taskbar
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal; // Reset to normal first
        
        // Get the primary screen bounds
        var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        
        Topmost = true; // Keep window on top of taskbar
    }

    private void ExitFullScreen()
    {
        // Restore previous window state
        Topmost = false;
        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;
        
        Left = _previousLeft;
        Top = _previousTop;
        Width = _previousWidth;
        Height = _previousHeight;
        
        WindowState = _previousWindowState;
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel viewModel && viewModel.IsFullScreenMode)
        {
            viewModel.ToggleFullScreenModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ApplyFontSize(double scale)
    {
        // Update all font size resources with the scale multiplier
        var resources = System.Windows.Application.Current.Resources;
        
        resources["BaseFontSize"] = 12.0 * scale;
        resources["FontSizeSmall"] = 11.0 * scale;
        resources["FontSizeNormal"] = 14.0 * scale;
        resources["FontSizeLarge"] = 18.0 * scale;
        resources["FontSizeTitle"] = 24.0 * scale;
        
        Console.WriteLine($"Font scale applied: {scale}x (Base: {12.0 * scale}, Normal: {14.0 * scale}, Title: {24.0 * scale})");
    }
}
