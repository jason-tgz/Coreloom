using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Coreloom.App.Themes;
using Coreloom.Core.Hardware;

namespace Coreloom.App.Controls;

/// <summary>
/// Finds vendor logo files. Coreloom does not draw or bundle other companies' logos: when official files are
/// supplied they are shown, otherwise <see cref="BrandMark"/> falls back to a plain-text vendor badge.
/// Lookup, first hit wins, for key "nvidia" on a dark theme:
///   1. the user's logo folder: nvidia-on-dark.png, then nvidia.png
///      (%AppData%\Coreloom\Vendors, or Data\Vendors in portable mode)
///   2. files embedded at build time from assets/vendors/ with the same names.
/// Light themes look for "-on-light" first. Decoded images are frozen, capped in size and cached.
/// </summary>
internal static class BrandLogos
{
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxPixelHeight = 256;

    private static readonly Dictionary<string, BitmapSource?> Cache = [];
    private static HashSet<string>? _embedded;
    private static string? _folder;

    /// <summary>Sets the folder users can drop logo files into (null disables the folder lookup).</summary>
    public static void Configure(string? userFolder)
    {
        _folder = userFolder;
        Cache.Clear();
    }

    /// <summary>The logo for <paramref name="key"/> ("nvidia", "amd", "intel"), or null when no file is installed.</summary>
    public static BitmapSource? Get(string key, bool darkTheme)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        string cacheKey = key + (darkTheme ? "|dark" : "|light");
        if (!Cache.TryGetValue(cacheKey, out var image))
        {
            string variant = $"{key}-{(darkTheme ? "on-dark" : "on-light")}.png";
            string plain = $"{key}.png";
            image = FromFolder(variant) ?? FromFolder(plain) ?? FromResource(variant) ?? FromResource(plain);
            Cache[cacheKey] = image;
        }

        return image;
    }

    private static BitmapSource? FromFolder(string fileName)
    {
        if (string.IsNullOrEmpty(_folder))
        {
            return null;
        }

        try
        {
            string path = Path.Combine(_folder, fileName);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaxFileBytes)
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Decode(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BitmapSource? FromResource(string fileName)
    {
        try
        {
            if (!EmbeddedNames().Contains("assets/vendors/" + fileName))
            {
                return null;
            }

            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Vendors/{fileName}", UriKind.Absolute));
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

    /// <summary>Names of the resources compiled into the app (lower-case, e.g. "assets/vendors/nvidia.png"), read once.</summary>
    private static HashSet<string> EmbeddedNames()
    {
        if (_embedded is not null)
        {
            return _embedded;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var assembly = typeof(BrandLogos).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
            if (stream is not null)
            {
                using var reader = new ResourceReader(stream);
                foreach (DictionaryEntry entry in reader)
                {
                    if (entry.Key is string name)
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception)
        {
            // No embedded logos is the normal case.
        }

        _embedded = names;
        return names;
    }

    private static BitmapSource Decode(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        if (bitmap.PixelHeight <= MaxPixelHeight)
        {
            return bitmap;
        }

        // Oversized files are scaled down once so a huge image can never sit in memory.
        double scale = (double)MaxPixelHeight / bitmap.PixelHeight;
        var scaled = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }
}

/// <summary>
/// The vendor mark shown next to a CPU or GPU name: the supplied logo file when one is installed, otherwise a
/// small badge with the vendor's name in plain text. Collapsed when the vendor is unknown.
/// </summary>
public sealed class BrandMark : Border
{
    public static readonly DependencyProperty VendorProperty = DependencyProperty.Register(
        nameof(Vendor), typeof(string), typeof(BrandMark), new PropertyMetadata(string.Empty, OnVendorChanged));

    public static readonly DependencyProperty MarkHeightProperty = DependencyProperty.Register(
        nameof(MarkHeight), typeof(double), typeof(BrandMark), new PropertyMetadata(22.0, OnVendorChanged));

    private bool _attached;

    public BrandMark()
    {
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = true;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Rebuild();
    }

    /// <summary>"nvidia", "amd" or "intel" (see <see cref="HardwareVendors.Key"/>).</summary>
    public string Vendor
    {
        get => (string)GetValue(VendorProperty);
        set => SetValue(VendorProperty, value);
    }

    public double MarkHeight
    {
        get => (double)GetValue(MarkHeightProperty);
        set => SetValue(MarkHeightProperty, value);
    }

    private static void OnVendorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BrandMark)d).Rebuild();

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += OnThemeChanged;
        }

        Rebuild();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Rebuild);

    private void Rebuild()
    {
        var vendor = HardwareVendors.FromKey(Vendor);
        if (vendor == HardwareVendor.Unknown)
        {
            Child = null;
            ClearValue(BackgroundProperty);
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        string name = HardwareVendors.DisplayName(vendor);
        var logo = BrandLogos.Get(HardwareVendors.Key(vendor), ThemeService.Instance?.IsDark ?? true);
        if (logo is not null)
        {
            ClearValue(BackgroundProperty);
            CornerRadius = new CornerRadius(0);
            Padding = new Thickness(0);
            var image = new Image
            {
                Source = logo,
                Height = MarkHeight,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                SnapsToDevicePixels = false,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Child = image;
            ToolTip = name;
            return;
        }

        // No logo file installed: a neutral text badge in the app's own typeface and colours.
        SetResourceReference(BackgroundProperty, "Brush.ControlHover");
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 3, 8, 3);
        var label = new TextBlock
        {
            Text = name,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Child = label;
        ToolTip = name;
    }
}
