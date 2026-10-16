using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Coreloom.App.Interop;
using Coreloom.App.Overlay;
using Coreloom.Core.Overlay;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.App.Controls;

/// <summary>
/// The overlay's frame-time graph (see <see cref="FrameGraphModel"/>): how long each of the game's frames took over the
/// last second, newest at the right, gliding along with time. Even pacing is a flat line; a stutter is a spike, tinted
/// amber or red by how far it jumps above the typical frame; the frame limiter's target is a dashed green line.
/// <para>
/// In the live overlay (<see cref="IsLiveProperty"/>, set on its window) this element only holds the graph's place: a
/// <see cref="FrameGraphLayer"/> draws the graph from its own thread into a see-through window laid exactly over it, at
/// an even pace. Everywhere else (the preview in the settings), it draws the same pixels into a bitmap itself, with a
/// still sample trace while no game is running so the look can be judged. Both use <see cref="FrameGraphRaster"/>, so the
/// overlay looks exactly like its preview.
/// </para>
/// </summary>
public sealed class FrameGraph : FrameworkElement
{
    public static readonly DependencyProperty FeedProperty = DependencyProperty.Register(
        nameof(Feed), typeof(IFrameTimeFeed), typeof(FrameGraph), new PropertyMetadata(null, (d, _) => ((FrameGraph)d).Restart()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FrameGraph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((FrameGraph)d).OnLookChanged()));

    /// <summary>Inherited: true inside the live overlay window.</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.RegisterAttached(
        "IsLive", typeof(bool), typeof(FrameGraph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private static readonly TimeSpan MinRedraw = TimeSpan.FromMilliseconds(1000.0 / 120);
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PreviewCheck = TimeSpan.FromMilliseconds(500);

    private readonly FrameGraphModel _model = new();
    private readonly FrameGraphRaster _raster = new();
    private FrameTimeReader? _read;
    private uint[] _pixels = [];
    private WriteableBitmap? _bitmap;
    private FrameGraphLayer? _layer;
    private Window? _window;
    private Rect _placed = Rect.Empty;
    private (uint Color, double Opacity) _look = (0, -1);
    private bool _showingSample;
    private bool _active;
    private bool _hooked;
    private TimeSpan _lastDraw;
    private DispatcherTimer? _idle;

    public FrameGraph()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => Stop(dispose: true);
        IsVisibleChanged += (_, _) => Restart();
    }

    public IFrameTimeFeed? Feed
    {
        get => (IFrameTimeFeed?)GetValue(FeedProperty);
        set => SetValue(FeedProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static bool GetIsLive(DependencyObject element) => (bool)element.GetValue(IsLiveProperty);

    public static void SetIsLive(DependencyObject element, bool value) => element.SetValue(IsLiveProperty, value);

    private uint StrokeColor => Stroke is SolidColorBrush solid ? ((uint)solid.Color.R << 16) | ((uint)solid.Color.G << 8) | solid.Color.B : 0xFFFFFF;

    /// <summary>The group the graph sits in carries the overlay's text opacity.</summary>
    private double InheritedOpacity()
    {
        double opacity = 1;
        for (DependencyObject? node = this; node is not null && node is not Window; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement element)
            {
                opacity *= element.Opacity;
            }
        }

        return opacity;
    }

    private void Restart()
    {
        if (!IsLoaded || !IsVisible || Feed is null)
        {
            Stop(dispose: false);
            return;
        }

        _read = Feed.Read;
        if (GetIsLive(this) && StartLayer())
        {
            return;
        }

        if (_active)
        {
            StartRendering();
        }
        else
        {
            StartIdle();
        }
    }

    private void Stop(bool dispose)
    {
        StopRendering();
        _idle?.Stop();
        _active = false;
        if (_layer is not null)
        {
            if (dispose)
            {
                Unhook();
                _layer.Dispose();
                _layer = null;
            }
            else
            {
                _layer.SetVisible(false);
            }
        }
    }

    // ------------------------------------------------------------------ live: the layer draws it

    private bool StartLayer()
    {
        if (_layer is null)
        {
            var window = Window.GetWindow(this);
            if (window is null || Feed is not { } feed || FrameGraphLayer.TryCreate(window, feed) is not { } layer)
            {
                return false;
            }

            _layer = layer;
            _window = window;
            _window.LocationChanged += OnMoved;
            LayoutUpdated += OnLayoutUpdated;
        }

        StopRendering();
        _idle?.Stop();
        _placed = Rect.Empty;
        _look = (0, -1);
        PlaceLayer();
        RefreshLook();
        _layer.SetVisible(true);
        InvalidateVisual();
        return true;
    }

    private void Unhook()
    {
        if (_window is not null)
        {
            _window.LocationChanged -= OnMoved;
            _window = null;
        }

        LayoutUpdated -= OnLayoutUpdated;
    }

    private void OnMoved(object? sender, EventArgs e) => PlaceLayer();

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        PlaceLayer();
        RefreshLook(); // the overlay's text opacity can change without a new layout of the graph
    }

    private void OnLookChanged() => RefreshLook();

    private void RefreshLook()
    {
        if (_layer is null)
        {
            return;
        }

        var look = (StrokeColor, Math.Round(InheritedOpacity(), 3));
        if (look != _look)
        {
            _look = look;
            _layer.SetLook(look.Item1, look.Item2);
        }
    }

    /// <summary>Lays the layer over the graph's place: its screen rectangle in pixels.</summary>
    private void PlaceLayer()
    {
        if (_layer is null || !IsVisible || PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        var rect = new Rect(Math.Round(topLeft.X), Math.Round(topLeft.Y), Math.Round(bottomRight.X - topLeft.X), Math.Round(bottomRight.Y - topLeft.Y));
        if (rect == _placed)
        {
            return;
        }

        _placed = rect;
        var dpi = VisualTreeHelper.GetDpi(this);
        IntPtr hwnd = _window is not null ? new WindowInteropHelper(_window).Handle : IntPtr.Zero;
        _layer.Place((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, dpi.DpiScaleX, DisplayInfo.RefreshRateAt(hwnd));
    }

    // ------------------------------------------------------------------ preview (and a fallback): drawn here

    /// <summary>Nothing drawing: look for frames now and then.</summary>
    private void StartIdle()
    {
        StopRendering();
        if (_idle is null)
        {
            _idle = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _idle.Tick += (_, _) => Check();
        }

        _idle.Interval = GetIsLive(this) ? IdleCheck : PreviewCheck;
        if (!_idle.IsEnabled)
        {
            _idle.Start();
            Check();
        }
    }

    private void StartRendering()
    {
        _idle?.Stop();
        if (!_hooked)
        {
            _hooked = true;
            _lastDraw = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopRendering()
    {
        if (_hooked)
        {
            _hooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    /// <summary>The idle check: frames have started (follow them), or show the sample / the empty baseline.</summary>
    private void Check()
    {
        if (Feed is not { } feed || _read is null)
        {
            return;
        }

        if (_model.Update(_read, feed.TargetFrameTime, IdleCheck.TotalSeconds))
        {
            _showingSample = false;
            _active = true;
            StartRendering();
            Redraw();
            return;
        }

        if (!GetIsLive(this))
        {
            if (!_showingSample)
            {
                _showingSample = true; // the preview: a still sample trace, drawn once
                _model.LoadSample();
                Redraw();
            }
        }
        else if (_bitmap is null || _showingSample)
        {
            _showingSample = false;
            Redraw(); // the empty baseline, drawn once
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (_lastDraw != TimeSpan.Zero && now - _lastDraw < MinRedraw)
        {
            return; // Rendering fires at the display's rate (and sometimes twice a frame)
        }

        double dt = _lastDraw == TimeSpan.Zero ? MinRedraw.TotalSeconds : (now - _lastDraw).TotalSeconds;
        _lastDraw = now;
        if (Feed is not { } feed || _read is null || !_model.Update(_read, feed.TargetFrameTime, dt))
        {
            _active = false;
            _showingSample = false;
            Redraw();
            StartIdle();
            return;
        }

        Redraw();
    }

    /// <summary>Draws the model into the bitmap (at the display's pixel size) and shows it.</summary>
    private void Redraw()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (width < 2 || height < 2)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
        }

        if (_pixels.Length < width * height)
        {
            _pixels = new uint[width * height];
        }

        _raster.Draw(_pixels, width, height, _model, new FrameGraphLook(StrokeColor, 1, dpi.DpiScaleX));
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_layer is null && _bitmap is not null)
        {
            Redraw();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_layer is null && _bitmap is not null)
        {
            dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }
}
