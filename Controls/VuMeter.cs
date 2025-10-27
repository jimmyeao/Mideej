using System.Windows;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Mideej.Controls;

/// <summary>
/// Custom VU Meter control using SkiaSharp for smooth rendering
/// </summary>
public class VuMeter : SKElement
{
    public static readonly DependencyProperty PeakLevelProperty =
        DependencyProperty.Register(nameof(PeakLevel), typeof(float), typeof(VuMeter),
            new PropertyMetadata(0f, OnPeakLevelChanged));

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(VuMeter),
            new PropertyMetadata(Colors.Green, OnColorChanged));

    public float PeakLevel
    {
        get => (float)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static void OnPeakLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VuMeter meter)
        {
            meter.InvalidateVisual();
        }
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VuMeter meter)
        {
            meter.InvalidateVisual();
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;
        var width = info.Width;
        var height = info.Height;

        if (width <= 0 || height <= 0)
            return;

        // Draw background
        using (var bgPaint = new SKPaint
        {
            Color = new SKColor(54, 54, 80),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            var bgRect = new SKRoundRect(new SKRect(0, 0, width, height), 8, 8);
            canvas.DrawRoundRect(bgRect, bgPaint);
        }

        // Calculate level height
        var levelHeight = height * Math.Clamp(PeakLevel, 0f, 1f);

        if (levelHeight > 0)
        {
            // Create gradient based on level
            var colors = new[]
            {
                GetColorForLevel(PeakLevel)
            };

            // Draw level indicator
            using (var levelPaint = new SKPaint
            {
                Color = colors[0],
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            })
            {
                var levelRect = new SKRect(4, height - levelHeight, width - 4, height - 4);
                var roundRect = new SKRoundRect(levelRect, 4, 4);
                canvas.DrawRoundRect(roundRect, levelPaint);
            }
        }

        // Draw segments (dB markers)
        DrawSegments(canvas, width, height);
    }

    private void DrawSegments(SKCanvas canvas, int width, int height)
    {
        using (var paint = new SKPaint
        {
            Color = new SKColor(30, 30, 46, 150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        })
        {
            int segmentCount = 10;
            float segmentHeight = height / (float)segmentCount;

            for (int i = 1; i < segmentCount; i++)
            {
                float y = i * segmentHeight;
                canvas.DrawLine(4, y, width - 4, y, paint);
            }
        }
    }

    private SKColor GetColorForLevel(float level)
    {
        // Green for low levels (0-0.7)
        // Yellow for medium levels (0.7-0.85)
        // Red for high levels (0.85-1.0)

        if (level < 0.7f)
        {
            return new SKColor(16, 185, 129); // Green
        }
        else if (level < 0.85f)
        {
            // Interpolate between green and yellow
            float t = (level - 0.7f) / 0.15f;
            return InterpolateColor(
                new SKColor(16, 185, 129),   // Green
                new SKColor(245, 158, 11),   // Yellow
                t
            );
        }
        else
        {
            // Interpolate between yellow and red
            float t = (level - 0.85f) / 0.15f;
            return InterpolateColor(
                new SKColor(245, 158, 11),   // Yellow
                new SKColor(239, 68, 68),    // Red
                t
            );
        }
    }

    private SKColor InterpolateColor(SKColor color1, SKColor color2, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new SKColor(
            (byte)(color1.Red + (color2.Red - color1.Red) * t),
            (byte)(color1.Green + (color2.Green - color1.Green) * t),
            (byte)(color1.Blue + (color2.Blue - color1.Blue) * t),
            (byte)(color1.Alpha + (color2.Alpha - color1.Alpha) * t)
        );
    }
}
