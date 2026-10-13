using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Coreloom.App.Behaviors;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Views;

/// <summary>
/// The PC in the Overview header: a render split into layers (see build/pc-render.py) whose glow takes the chosen color
/// and whose fans turn with the PC's fan speeds, or a picture of the real build when one is supplied (see
/// <see cref="PcImages"/>).
/// </summary>
/// <remarks>
/// The fans only turn while they can be seen: not on another page, not with the window minimized or in the tray, and
/// not with Reduce motion on. They are capped at 60 frames per second. Each fan has its own clock so its speed can
/// change without a jump.
/// </remarks>
public partial class PcIllustration : UserControl
{
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow), typeof(PcGlow), typeof(PcIllustration), new PropertyMetadata(PcGlow.Blue, (d, _) => ((PcIllustration)d).ApplyGlow(animate: true)));

    public static readonly DependencyProperty TopFanSpeedProperty = DependencyProperty.Register(
        nameof(TopFanSpeed), typeof(double), typeof(PcIllustration), new PropertyMetadata(0.0, (d, _) => ((PcIllustration)d).UpdateFans()));

    public static readonly DependencyProperty BottomFanSpeedProperty = DependencyProperty.Register(
        nameof(BottomFanSpeed), typeof(double), typeof(PcIllustration), new PropertyMetadata(0.0, (d, _) => ((PcIllustration)d).UpdateFans()));

    public static readonly DependencyProperty AnimateFansProperty = DependencyProperty.Register(
        nameof(AnimateFans), typeof(bool), typeof(PcIllustration), new PropertyMetadata(true, (d, _) => ((PcIllustration)d).UpdateFans()));

    private const int FanFrameRate = 60;
    private const int RainbowFrameRate = 30;
    private static readonly TimeSpan RainbowCycle = TimeSpan.FromSeconds(14);

    private readonly SolidColorBrush _glowBrush = new(Color.FromRgb(0x0D, 0x6B, 0xFF));
    private readonly Fan[] _top;
    private readonly Fan[] _bottom;
    private AnimationClock? _rainbow;
    private Window? _window;
    private bool _custom;

    public PcIllustration()
    {
        InitializeComponent();
        GlowLayer.Fill = _glowBrush;
        _top = [new(FanTop1Turn, FanTop1Sharp, FanTop1Blur, 1), new(FanTop2Turn, FanTop2Sharp, FanTop2Blur, 1), new(FanTop3Turn, FanTop3Sharp, FanTop3Blur, 1)];
        _bottom = [new(FanBottom1Turn, FanBottom1Sharp, FanBottom1Blur, -1), new(FanBottom2Turn, FanBottom2Sharp, FanBottom2Blur, -1), new(FanBottom3Turn, FanBottom3Sharp, FanBottom3Blur, -1)];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateClocks();
    }

    /// <summary>Lighting color of the picture.</summary>
    public PcGlow Glow
    {
        get => (PcGlow)GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    /// <summary>Turns per second of the three top fans (0 = stopped).</summary>
    public double TopFanSpeed
    {
        get => (double)GetValue(TopFanSpeedProperty);
        set => SetValue(TopFanSpeedProperty, value);
    }

    /// <summary>Turns per second of the three bottom fans (0 = stopped).</summary>
    public double BottomFanSpeed
    {
        get => (double)GetValue(BottomFanSpeedProperty);
        set => SetValue(BottomFanSpeedProperty, value);
    }

    /// <summary>False with Reduce motion on: the fans stand still.</summary>
    public bool AnimateFans
    {
        get => (bool)GetValue(AnimateFansProperty);
        set => SetValue(AnimateFansProperty, value);
    }

    private bool CanAnimate => !_custom && IsLoaded && IsVisible && _window is not { WindowState: WindowState.Minimized };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyCustomImage();
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += OnWindowStateChanged;
        }

        ApplyGlow(animate: false);
        UpdateFans();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }

        UpdateClocks();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateClocks();

    private void ApplyCustomImage()
    {
        var image = PcImages.Get();
        _custom = image is not null;
        CustomImage.Source = image;
        CustomImage.Visibility = _custom ? Visibility.Visible : Visibility.Collapsed;
        Art.Visibility = _custom ? Visibility.Collapsed : Visibility.Visible;
        GlowPicker.Visibility = _custom ? Visibility.Collapsed : Visibility.Visible; // a photo has its own lighting
        if (_custom)
        {
            Picture.ToolTip = null; // the fan readings don't describe a still picture
        }
    }

    // ---- Lighting ---------------------------------------------------------------------------------

    private void ApplyGlow(bool animate)
    {
        var from = _glowBrush.Color; // the color on screen now (mid-transition or mid-cycle included)
        StopRainbow();
        var glow = Glow;
        if (glow == PcGlow.Accent)
        {
            GlowLayer.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Accent"); // follows accent changes
            return;
        }

        GlowLayer.Fill = _glowBrush;
        if (glow == PcGlow.Rainbow)
        {
            UpdateClocks(); // starts the cycle once the picture can be seen
            return;
        }

        var target = (Color)ColorConverter.ConvertFromString(PcFans.GlowHex(glow) ?? "#0D6BFF");
        _glowBrush.Color = target;
        _glowBrush.BeginAnimation(SolidColorBrush.ColorProperty, animate && IsLoaded && !Motion.ReducedMotion && from != target
            ? new ColorAnimation(from, target, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }
            : null);
    }

    private void StartRainbow()
    {
        // The fixed choices plus amber, cyan and pink in between, around the color wheel.
        string[] wheel = ["#FF2933", "#FFB01A", "#1AD96B", "#18C8FF", "#0D6BFF", "#9E4DFF", "#FF2ED1", "#FF2933"];
        var frames = new ColorAnimationUsingKeyFrames { Duration = RainbowCycle, RepeatBehavior = RepeatBehavior.Forever };
        for (int i = 0; i < wheel.Length; i++)
        {
            frames.KeyFrames.Add(new LinearColorKeyFrame((Color)ColorConverter.ConvertFromString(wheel[i]), KeyTime.FromPercent(i / (double)(wheel.Length - 1))));
        }

        Timeline.SetDesiredFrameRate(frames, RainbowFrameRate);
        _rainbow = frames.CreateClock();
        _glowBrush.ApplyAnimationClock(SolidColorBrush.ColorProperty, _rainbow);
    }

    private void StopRainbow()
    {
        if (_rainbow is null)
        {
            return;
        }

        _glowBrush.ApplyAnimationClock(SolidColorBrush.ColorProperty, null);
        _rainbow = null;
    }

    // ---- Fans -------------------------------------------------------------------------------------

    private void UpdateFans() => UpdateClocks();

    /// <summary>
    /// Starts, re-paces or stops every animation depending on the speeds and on whether anyone can see the picture.
    /// Nothing ticks while it can't be seen, so a hidden Overview costs no rendering.
    /// </summary>
    private void UpdateClocks()
    {
        bool run = CanAnimate;
        double top = AnimateFans ? TopFanSpeed : 0;
        double bottom = AnimateFans ? BottomFanSpeed : 0;
        foreach (var fan in _top)
        {
            fan.Update(top, run);
        }

        foreach (var fan in _bottom)
        {
            fan.Update(bottom, run);
        }

        if (Glow == PcGlow.Rainbow && GlowLayer.Fill == _glowBrush)
        {
            if (!run)
            {
                _rainbow?.Controller?.Pause();
            }
            else if (_rainbow is null)
            {
                StartRainbow();
            }
            else
            {
                _rainbow.Controller?.Resume();
            }
        }
    }

    /// <summary>One fan: its turn (added to the fan's starting angle) and the sharp and blurred blades.</summary>
    private sealed class Fan
    {
        private readonly RotateTransform _turn;
        private readonly UIElement _sharp;
        private readonly UIElement _blur;
        private readonly int _direction;
        private AnimationClock? _clock;

        public Fan(RotateTransform turn, UIElement sharp, UIElement blur, int direction)
        {
            _turn = turn;
            _sharp = sharp;
            _blur = blur;
            _direction = direction;
        }

        public void Update(double turnsPerSecond, bool visible)
        {
            double speed = double.IsFinite(turnsPerSecond) ? Math.Clamp(turnsPerSecond, 0, PcFans.MaxSpeed) : 0;

            // Faster fans blur their blades, as they do to the eye.
            double blur = Math.Clamp((speed - 0.5) / 1.5, 0, 0.85);
            _blur.Opacity = blur;
            _sharp.Opacity = 1 - blur;

            if (!visible || speed < 0.01)
            {
                Stop();
                return;
            }

            if (_clock is null)
            {
                // One turn a second, paced through the clock's speed: no jump when the fan speeds up or slows down.
                var turn = new DoubleAnimation(0, 360 * _direction, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever, IsAdditive = true };
                Timeline.SetDesiredFrameRate(turn, FanFrameRate);
                _clock = turn.CreateClock();
                _turn.ApplyAnimationClock(RotateTransform.AngleProperty, _clock);
            }

            if (_clock.Controller is { } controller)
            {
                controller.SpeedRatio = speed;
            }
        }

        /// <summary>Removes the animation and keeps the blades where they are.</summary>
        private void Stop()
        {
            if (_clock is null)
            {
                return;
            }

            double angle = _turn.Angle % 360;
            _turn.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            _turn.Angle = angle;
            _clock = null;
        }
    }
}

/// <summary>
/// Finds an optional picture of the user's PC (for example a 3D render of the build) to show instead of the drawn
/// illustration. First hit wins: <c>pc.png</c> or <c>pc.jpg</c> in the settings folder (%AppData%\Coreloom, or Data next
/// to a portable copy), then <c>assets/pc/pc.png</c> embedded at build time. Images are decoded once, at most 800 px
/// wide, frozen and cached.
/// </summary>
internal static class PcImages
{
    private const long MaxFileBytes = 12 * 1024 * 1024;
    private const int DecodeWidth = 800;
    private static string? _folder;
    private static bool _loaded;
    private static BitmapSource? _image;

    public static void Configure(string? folder)
    {
        _folder = folder;
        _loaded = false;
        _image = null;
    }

    public static BitmapSource? Get()
    {
        if (!_loaded)
        {
            _loaded = true;
            _image = FromFolder("pc.png") ?? FromFolder("pc.jpg") ?? FromResource();
        }

        return _image;
    }

    private static BitmapSource? FromFolder(string fileName)
    {
        if (string.IsNullOrEmpty(_folder))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(Path.Combine(_folder, fileName));
            if (!info.Exists || info.Length is 0 or > MaxFileBytes)
            {
                return null;
            }

            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Decode(stream);
        }
        catch (Exception)
        {
            return null; // an unreadable or invalid file just means the drawn illustration is used
        }
    }

    private static BitmapSource? FromResource()
    {
        try
        {
            if (!HasEmbeddedImage())
            {
                return null;
            }

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PC/pc.png", UriKind.Absolute));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            return Decode(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasEmbeddedImage()
    {
        var assembly = typeof(PcImages).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
        if (stream is null)
        {
            return false;
        }

        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Key is string name && name.Equals("assets/pc/pc.png", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static BitmapSource Decode(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = DecodeWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
