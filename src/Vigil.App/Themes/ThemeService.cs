using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Vigil.App.Behaviors;
using Vigil.App.Interop;
using Vigil.Core.Settings;
using Vigil.ViewModels.Services;

namespace Vigil.App.Themes;

/// <summary>
/// Owns all theme brushes ("Brush.*" and "Series.*.Brush" resources). Applying a theme or accent animates
/// the existing brush colors, so the whole UI cross-fades without rebuilding any visuals.
/// Also applies reduced motion, the Mica backdrop and the immersive dark title bar.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly string[] SurfaceKeys = ["WindowBackground", "Sidebar", "ContentLayer", "TitleBar"];
    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);
    private AppearanceSettings? _current;
    private bool _isDark = true;
    private bool _initialized;

    public ThemeService(ResourceDictionary resources)
    {
        _resources = resources;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _current?.Theme == ThemePreference.System)
            {
                var current = _current;
                Application.Current?.Dispatcher.InvokeAsync(() => Apply(current));
            }
        };
    }

    public static ThemeService? Instance { get; private set; }

    public event EventHandler? ThemeChanged;

    public bool IsDark => _isDark;

    public bool IsMicaSupported => NativeWindow.IsMicaSupported;

    /// <summary>True when Mica is active for the main window (transparent surfaces).</summary>
    public bool IsMicaActive { get; private set; }

    public Color AccentColor { get; private set; } = ThemePalette.Hex(AccentPalette.Default);

    /// <summary>Creates every brush with the dark palette. Call once, before any window is created.</summary>
    public void Initialize(AppearanceSettings appearance)
    {
        Instance = this;
        foreach (var (key, color) in ThemePalette.Dark)
        {
            var brush = new SolidColorBrush(color);
            string resourceKey = key.StartsWith("Series.", StringComparison.Ordinal) ? key + ".Brush" : "Brush." + key;
            _brushes[resourceKey] = brush;
            _resources[resourceKey] = brush;
        }

        foreach (string key in new[] { "Brush.Accent", "Brush.AccentSoft", "Brush.AccentHover", "Brush.OnAccent", "Series.Accent.Brush" }.Concat(SurfaceKeys.Select(k => "Brush." + k)))
        {
            var brush = new SolidColorBrush(Colors.Transparent);
            _brushes[key] = brush;
            _resources[key] = brush;
        }

        Apply(appearance, animate: false);
        _initialized = true;
    }

    public void Apply(AppearanceSettings appearance) => Apply(appearance, animate: _initialized && !appearance.ReducedMotion);

    private void Apply(AppearanceSettings appearance, bool animate)
    {
        _current = appearance;
        _isDark = appearance.Theme switch
        {
            ThemePreference.Light => false,
            ThemePreference.System => !SystemUsesLightTheme(),
            _ => true,
        };

        var palette = _isDark ? ThemePalette.Dark : ThemePalette.Light;
        var duration = TimeSpan.FromMilliseconds(animate ? 280 : 0);
        foreach (var (key, color) in palette)
        {
            string resourceKey = key.StartsWith("Series.", StringComparison.Ordinal) ? key + ".Brush" : "Brush." + key;
            SetColor(resourceKey, color, duration);
        }

        AccentColor = ThemePalette.Hex(AccentPalette.IsValidHex(appearance.Accent) ? appearance.Accent : AccentPalette.Default);
        SetColor("Brush.Accent", AccentColor, duration);
        SetColor("Series.Accent.Brush", AccentColor, duration);
        SetColor("Brush.AccentSoft", ThemePalette.WithAlpha(AccentColor, (byte)(_isDark ? 0x2E : 0x24)), duration);
        SetColor("Brush.AccentHover", Blend(AccentColor, _isDark ? Colors.White : Colors.Black, 0.12), duration);
        SetColor("Brush.OnAccent", ThemePalette.ContrastingText(AccentColor), duration);

        IsMicaActive = appearance.UseMica && IsMicaSupported;
        var background = palette["Background"];
        var surface = palette["Surface"];
        SetColor("Brush.WindowBackground", IsMicaActive ? Colors.Transparent : background, TimeSpan.Zero);
        SetColor("Brush.Sidebar", IsMicaActive ? Colors.Transparent : surface, duration);
        SetColor("Brush.ContentLayer", IsMicaActive ? ThemePalette.WithAlpha(background, 0xE0) : background, duration);
        SetColor("Brush.TitleBar", background, duration);

        AnimatedText.IsGloballyEnabled = appearance.AnimateValues && !appearance.ReducedMotion;
        Motion.ReducedMotion = appearance.ReducedMotion;

        if (Application.Current is { } app)
        {
            foreach (Window window in app.Windows)
            {
                NativeWindow.ApplyBackdrop(window, _isDark, IsMicaActive);
            }
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies DWM attributes to a newly created window.</summary>
    public void ApplyToWindow(Window window) => NativeWindow.ApplyBackdrop(window, _isDark, IsMicaActive);

    public Brush GetBrush(string resourceKey) =>
        _brushes.TryGetValue(resourceKey, out var brush) ? brush : Brushes.Gray;

    /// <summary>Brush for a semantic series color key such as "Cpu" or "Gpu".</summary>
    public Brush GetSeriesBrush(string? colorKey) =>
        _brushes.TryGetValue($"Series.{colorKey ?? "Accent"}.Brush", out var brush) ? brush : _brushes["Series.Accent.Brush"];

    private void SetColor(string key, Color color, TimeSpan duration)
    {
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushes[key] = brush;
            _resources[key] = brush;
            return;
        }

        if (duration <= TimeSpan.Zero)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }

        var animation = new ColorAnimation(color, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) =>
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        a.A,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
