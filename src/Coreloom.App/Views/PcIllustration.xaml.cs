using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Coreloom.App.Views;

/// <summary>
/// The PC on the Overview header: an original vector illustration bound to the detected hardware, or a render of the
/// real build when one is supplied (see <see cref="PcImages"/>).
/// </summary>
public partial class PcIllustration : UserControl
{
    public PcIllustration()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyCustomImage();
    }

    private void ApplyCustomImage()
    {
        var image = PcImages.Get();
        bool custom = image is not null;
        CustomImage.Source = image;
        CustomImage.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        Art.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
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
