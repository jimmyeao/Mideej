using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Mideej.Views;

/// <summary>
/// Transparent always-on-top overlay that shows channel volumes when adjusted.
/// Uses SkiaSharp for GPU-accelerated rendering. Does not steal focus.
/// </summary>
public partial class VolumeOverlay : Window
{
    // Win32 constants to prevent focus stealing
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // Current state
    private List<float> _volumes = new();
    private List<string> _labels = new();
    private List<bool> _muted = new();
    private List<bool> _soloed = new();
    private List<bool> _isOutputDevice = new();
    private bool _anySoloed;

    // Auto-close timer
    private DispatcherTimer? _autoCloseTimer;
    private int _timeoutSeconds = 5;

    // Throttling to prevent stale values
    private DateTime _lastUpdate = DateTime.MinValue;
    private const int MIN_UPDATE_INTERVAL_MS = 16; // ~60fps

    // Staleness guard: force repaint periodically while visible
    private DispatcherTimer? _refreshTimer;

    // Cached paint objects for performance
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _valueTextPaint;
    private readonly SKPaint _arcBackgroundPaint;
    private readonly SKPaint _mutedPaint;

    // Event for position persistence
    public event EventHandler<Point>? PositionChanged;

    // Callback to fetch live volume values (guards against stale data)
    public Func<(List<float> volumes, List<string> labels, List<bool> muted, List<bool> soloed, List<bool> isOutputDevice)>? GetCurrentValues { get; set; }

    public VolumeOverlay()
    {
        InitializeComponent();

        // Prevent focus stealing
        this.ShowActivated = false;
        this.Focusable = false;
        this.IsTabStop = false;

        // Pre-allocate paint objects
        _backgroundPaint = new SKPaint
        {
            Color = new SKColor(30, 30, 30, 210),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _borderPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 60),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        _textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 12,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        _valueTextPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 14,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        _arcBackgroundPaint = new SKPaint
        {
            Color = new SKColor(60, 60, 60),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        _mutedPaint = new SKPaint
        {
            Color = new SKColor(255, 60, 60),
            TextSize = 10,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        // Dragging support
        this.MouseLeftButtonDown += OnMouseLeftButtonDown;

        // Setup refresh timer to guard against stale values
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Apply Win32 styles to prevent focus stealing
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    protected override void OnActivated(EventArgs e)
    {
        // Don't call base - prevents activation/focus stealing
    }

    /// <summary>
    /// Update the overlay with new volume data and show it.
    /// Throttled to ~60fps. Restarts auto-close timer on each call.
    /// </summary>
    public void ShowVolumes(List<float> volumes, List<string> labels, List<bool> muted,
        List<bool>? soloed = null, List<bool>? isOutputDevice = null)
    {
        // Throttle updates to prevent excessive rendering
        var now = DateTime.Now;
        if ((now - _lastUpdate).TotalMilliseconds < MIN_UPDATE_INTERVAL_MS)
            return;
        _lastUpdate = now;

        // Store values (defensive copy)
        _volumes = new List<float>(volumes);
        _labels = new List<string>(labels);
        _muted = new List<bool>(muted);
        _soloed = soloed != null ? new List<bool>(soloed) : new List<bool>();
        _isOutputDevice = isOutputDevice != null ? new List<bool>(isOutputDevice) : new List<bool>();
        _anySoloed = _soloed.Any(s => s);

        // Resize window to fit content
        UpdateWindowSize();

        // Force repaint
        OverlayCanvas.InvalidateVisual();

        // Show if not visible (use Visibility to avoid focus stealing)
        if (!this.IsVisible)
        {
            this.Visibility = Visibility.Visible;
            _refreshTimer?.Start();
        }

        // Restart auto-close timer
        RestartAutoCloseTimer();
    }

    /// <summary>
    /// Configure the auto-close timeout. Set to 0 to keep overlay visible permanently.
    /// </summary>
    public void SetTimeout(int seconds)
    {
        _timeoutSeconds = seconds;
        SetupAutoCloseTimer(seconds);
    }

    /// <summary>
    /// Set the overlay opacity (0.0 to 1.0)
    /// </summary>
    public void SetOverlayOpacity(double opacity)
    {
        byte alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
        _backgroundPaint.Color = new SKColor(30, 30, 30, alpha);
        _borderPaint.Color = new SKColor(255, 255, 255, (byte)(alpha * 0.3));

        if (this.IsVisible)
            OverlayCanvas.InvalidateVisual();
    }

    /// <summary>
    /// Set the overlay position
    /// </summary>
    public void SetPosition(double x, double y)
    {
        // Validate against virtual screen bounds
        var screenWidth = SystemParameters.VirtualScreenWidth;
        var screenHeight = SystemParameters.VirtualScreenHeight;
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;

        this.Left = Math.Clamp(x, screenLeft, screenLeft + screenWidth - this.Width);
        this.Top = Math.Clamp(y, screenTop, screenTop + screenHeight - this.Height);
    }

    private void UpdateWindowSize()
    {
        int count = _volumes.Count;
        if (count == 0) return;

        // Each channel takes ~90px width, plus padding
        double targetWidth = Math.Max(200, count * 90 + 40);
        double targetHeight = 120;

        if (Math.Abs(this.Width - targetWidth) > 1 || Math.Abs(this.Height - targetHeight) > 1)
        {
            this.Width = targetWidth;
            this.Height = targetHeight;
        }
    }

    private void SetupAutoCloseTimer(int timeoutSeconds)
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;

        if (timeoutSeconds > 0)
        {
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(timeoutSeconds)
            };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                _refreshTimer?.Stop();
                this.Visibility = Visibility.Collapsed;
            };
        }
    }

    private void RestartAutoCloseTimer()
    {
        if (_autoCloseTimer != null)
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Start();
        }
    }

    /// <summary>
    /// Refresh timer tick - pulls fresh values via callback to guard against
    /// the overlay showing stale volume data (known issue from DeejNG).
    /// </summary>
    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!this.IsVisible || GetCurrentValues == null)
            return;

        try
        {
            var (volumes, labels, muted, soloed, isOutputDevice) = GetCurrentValues();

            if (volumes.Count == 0)
                return;

            // Check if values actually changed using adaptive tolerance
            bool soloChanged = _anySoloed != soloed.Any(s => s) ||
                               !_soloed.SequenceEqual(soloed);
            if (!HasValuesChanged(volumes, muted) && !soloChanged)
                return;

            _volumes = new List<float>(volumes);
            _labels = new List<string>(labels);
            _muted = new List<bool>(muted);
            _soloed = new List<bool>(soloed);
            _isOutputDevice = new List<bool>(isOutputDevice);
            _anySoloed = _soloed.Any(s => s);

            OverlayCanvas.InvalidateVisual();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Overlay Refresh] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Adaptive tolerance comparison to prevent stale values at extremes.
    /// Uses tighter tolerance near 0% and 100% (the DeejNG bug fix).
    /// </summary>
    private bool HasValuesChanged(List<float> newVolumes, List<bool> newMuted)
    {
        if (newVolumes.Count != _volumes.Count)
            return true;

        for (int i = 0; i < newVolumes.Count; i++)
        {
            // Check mute state change
            if (i < _muted.Count && i < newMuted.Count && _muted[i] != newMuted[i])
                return true;

            float diff = Math.Abs(newVolumes[i] - _volumes[i]);

            // Adaptive tolerance: tighter near extremes (0% and 100%)
            bool isAtExtreme = newVolumes[i] <= 0.01f || newVolumes[i] >= 0.99f ||
                               _volumes[i] <= 0.01f || _volumes[i] >= 0.99f;

            if (isAtExtreme)
            {
                if (diff > 0.001f) return true; // 0.1% tolerance at extremes
            }
            else
            {
                if (diff > 0.005f) return true; // 0.5% tolerance normally
            }
        }

        return false;
    }

    private void OverlayCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (_volumes.Count == 0) return;

        float width = info.Width;
        float height = info.Height;

        // Draw rounded rectangle background
        var bgRect = new SKRoundRect(new SKRect(0, 0, width, height), 12, 12);
        canvas.DrawRoundRect(bgRect, _backgroundPaint);
        canvas.DrawRoundRect(bgRect, _borderPaint);

        int count = _volumes.Count;
        float cellWidth = (width - 40) / count;
        float centerY = height / 2f;
        float arcRadius = 28;

        for (int i = 0; i < count; i++)
        {
            float centerX = 20 + cellWidth * i + cellWidth / 2f;
            float volume = Math.Clamp(_volumes[i], 0f, 1f);
            bool isMuted = i < _muted.Count && _muted[i];
            bool isSoloed = i < _soloed.Count && _soloed[i];
            bool isOutput = i < _isOutputDevice.Count && _isOutputDevice[i];
            string label = i < _labels.Count ? _labels[i] : $"Ch {i + 1}";
            int percent = (int)(volume * 100);

            // Determine if this channel should be greyed out
            // Grey out when solo is active, this channel isn't soloed, and it's not an output device
            bool isGreyedOut = _anySoloed && !isSoloed && !isOutput;

            // Overall alpha for greyed-out channels
            byte channelAlpha = isGreyedOut ? (byte)80 : (byte)255;

            // Draw arc background (gray ring)
            var arcRect = new SKRect(
                centerX - arcRadius, centerY - arcRadius - 5,
                centerX + arcRadius, centerY + arcRadius - 5);

            float startAngle = 135;
            float sweepAngle = 270;

            using var arcBgPaint = new SKPaint
            {
                Color = isGreyedOut ? new SKColor(60, 60, 60, 50) : new SKColor(60, 60, 60),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 6,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawArc(arcRect, startAngle, sweepAngle, false, arcBgPaint);

            // Draw volume arc (colored)
            if (!isMuted && volume > 0.001f)
            {
                float valueSweep = sweepAngle * volume;
                SKColor arcColor;
                if (isSoloed)
                    arcColor = new SKColor(60, 140, 255); // Blue for soloed channel
                else if (isGreyedOut)
                    arcColor = new SKColor(100, 100, 100, channelAlpha); // Dim grey
                else
                    arcColor = GetVolumeColor(volume);

                using var arcPaint = new SKPaint
                {
                    Color = arcColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 6,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true
                };
                canvas.DrawArc(arcRect, startAngle, valueSweep, false, arcPaint);
            }

            // Draw percentage text in center of arc
            if (isMuted)
            {
                _mutedPaint.Color = isGreyedOut
                    ? new SKColor(255, 60, 60, channelAlpha)
                    : new SKColor(255, 60, 60);
                canvas.DrawText("MUTE", centerX, centerY + 2, _mutedPaint);
            }
            else
            {
                _valueTextPaint.Color = isGreyedOut
                    ? new SKColor(255, 255, 255, channelAlpha)
                    : SKColors.White;
                canvas.DrawText($"{percent}%", centerX, centerY + 2, _valueTextPaint);
            }

            // Draw channel label below arc
            string truncatedLabel = TruncateLabel(label, cellWidth - 8);
            _textPaint.TextAlign = SKTextAlign.Center;
            _textPaint.Color = isGreyedOut
                ? new SKColor(255, 255, 255, channelAlpha)
                : SKColors.White;
            canvas.DrawText(truncatedLabel, centerX, height - 10, _textPaint);
        }
    }

    private static SKColor GetVolumeColor(float volume)
    {
        if (volume < 0.66f) return new SKColor(50, 205, 50);     // Green
        if (volume < 0.80f) return new SKColor(255, 215, 0);     // Gold
        if (volume < 0.90f) return new SKColor(255, 165, 0);     // Orange
        return new SKColor(255, 50, 50);                          // Red
    }

    private string TruncateLabel(string label, float maxWidth)
    {
        float measured = _textPaint.MeasureText(label);
        if (measured <= maxWidth) return label;

        // Truncate with ellipsis
        for (int len = label.Length - 1; len > 0; len--)
        {
            string truncated = label[..len] + "...";
            if (_textPaint.MeasureText(truncated) <= maxWidth)
                return truncated;
        }

        return "...";
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();

            // Notify about position change for persistence
            PositionChanged?.Invoke(this, new Point(this.Left, this.Top));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _refreshTimer?.Stop();

        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
        _valueTextPaint.Dispose();
        _arcBackgroundPaint.Dispose();
        _mutedPaint.Dispose();

        base.OnClosed(e);
    }
}
