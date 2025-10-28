using System.Windows;
using System.Windows.Input;
using Mideej.ViewModels;
using System.Drawing;
using System.Windows.Forms;

namespace Mideej;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private NotifyIcon? _notifyIcon;
    private bool _isClosing = false;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        Loaded += (s, e) => ApplyFontSize(viewModel.FontSizeScale);
        viewModel.FontSizeChanged += (s, e) => ApplyFontSize(viewModel.FontSizeScale);
        
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = new Icon("appicon.ico"),
            Text = "Mideej - MIDI Audio Mixer",
            Visible = false
        };

        _notifyIcon.DoubleClick += (s, e) =>
        {
            Show();
            WindowState = WindowState.Normal;
            _notifyIcon.Visible = false;
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) =>
        {
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
                Hide();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = true;
                    // Show a subtle balloon tip with no icon (cleaner look)
                    _notifyIcon.ShowBalloonTip(1000, "Mideej", "Minimized to tray. Double-click to restore.", ToolTipIcon.None);
                }
            }
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Always allow closing - don't intercept the close button
        _notifyIcon?.Dispose();
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SaveConfigurationAsync();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore();
        }
        else
        {
            DragMove();
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
        Close();
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
