using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Hardware;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>A lighting color offered next to the PC picture.</summary>
public sealed class GlowOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public GlowOptionViewModel(PcGlow glow, string name, string? hex)
    {
        Glow = glow;
        Name = name;
        Hex = hex;
    }

    public PcGlow Glow { get; }

    /// <summary>"Blue", "Rainbow", "Accent color".</summary>
    public string Name { get; }

    /// <summary>Swatch color as #RRGGBB; null for Rainbow (a gradient) and Accent (the theme's brush).</summary>
    public string? Hex { get; }

    public bool IsRainbow => Glow == PcGlow.Rainbow;

    public bool IsAccent => Glow == PcGlow.Accent;

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// The Overview header: the board name as the title, the graphics card as the subtitle, two columns of key specs, and
/// what the PC picture needs (the card's model name, the temperature on the pump screen, the lighting color and how
/// fast its fans turn).
/// </summary>
public sealed class SystemSummaryViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private GpuSize _gpuSize = GpuSize.None;
    private string _gpuLabel = "GPU";
    private string _screenValue = "—";
    private string _screenLabel = "CPU";
    private PcGlow _glow = PcGlow.Blue;
    private double _topFanSpeed = PcFans.UnknownSpeed;
    private double _bottomFanSpeed = PcFans.UnknownSpeed;
    private string _fanToolTip = PcFans.UnknownToolTip;
    private bool _animateFans = true;

    public SystemSummaryViewModel()
    {
        GlowOptions =
        [
            new GlowOptionViewModel(PcGlow.Blue, "Blue", PcFans.GlowHex(PcGlow.Blue)),
            new GlowOptionViewModel(PcGlow.Purple, "Purple", PcFans.GlowHex(PcGlow.Purple)),
            new GlowOptionViewModel(PcGlow.Red, "Red", PcFans.GlowHex(PcGlow.Red)),
            new GlowOptionViewModel(PcGlow.Green, "Green", PcFans.GlowHex(PcGlow.Green)),
            new GlowOptionViewModel(PcGlow.Rainbow, "Rainbow", null),
            new GlowOptionViewModel(PcGlow.Accent, "Accent color", null),
        ];
        SelectGlowCommand = new RelayCommand<GlowOptionViewModel>(option =>
        {
            if (option is not null)
            {
                Glow = option.Glow;
            }
        });
        UpdateSelection();
    }

    /// <summary>Raised when the user picks another lighting color (the Overview saves it).</summary>
    public event Action<PcGlow>? GlowChanged;

    /// <summary>"ASUS ROG STRIX X870E-E GAMING WIFI" (the computer name when the firmware does not name the board).</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>"NVIDIA GeForce RTX 5090" (the processor when there is no graphics card).</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    /// <summary>Processor, cores, memory, storage.</summary>
    public ObservableCollection<KeyValueItem> LeftSpecs { get; } = [];

    /// <summary>Graphics memory, driver, Windows, BIOS.</summary>
    public ObservableCollection<KeyValueItem> RightSpecs { get; } = [];

    public GpuSize GpuSize
    {
        get => _gpuSize;
        private set
        {
            if (SetProperty(ref _gpuSize, value))
            {
                OnPropertyChanged(nameof(ShowGpuLabel));
            }
        }
    }

    /// <summary>The card in the picture carries the model name only for a discrete graphics card.</summary>
    public bool ShowGpuLabel => GpuSize != GpuSize.None;

    /// <summary>Model name on the card in the picture, e.g. "RTX 5090".</summary>
    public string GpuLabel
    {
        get => _gpuLabel;
        private set => SetProperty(ref _gpuLabel, value);
    }

    /// <summary>Big number on the pump screen, e.g. "54°".</summary>
    public string ScreenValue
    {
        get => _screenValue;
        private set => SetProperty(ref _screenValue, value);
    }

    /// <summary>Small label under it, e.g. "CPU".</summary>
    public string ScreenLabel
    {
        get => _screenLabel;
        private set => SetProperty(ref _screenLabel, value);
    }

    // ---- Lighting ---------------------------------------------------------------------------------

    public IReadOnlyList<GlowOptionViewModel> GlowOptions { get; }

    public IRelayCommand<GlowOptionViewModel> SelectGlowCommand { get; }

    public PcGlow Glow
    {
        get => _glow;
        set
        {
            if (SetProperty(ref _glow, value))
            {
                UpdateSelection();
                GlowChanged?.Invoke(value);
            }
        }
    }

    // ---- Fans -------------------------------------------------------------------------------------

    /// <summary>Turns per second of the three fans at the top of the picture (the CPU cooler's).</summary>
    public double TopFanSpeed
    {
        get => _topFanSpeed;
        private set => SetProperty(ref _topFanSpeed, value);
    }

    /// <summary>Turns per second of the three fans at the bottom (the case fans).</summary>
    public double BottomFanSpeed
    {
        get => _bottomFanSpeed;
        private set => SetProperty(ref _bottomFanSpeed, value);
    }

    /// <summary>Which readings the fans follow, e.g. "Top fans: CPU Fan, 1,240 RPM · Bottom fans: 2 case fans, 860 RPM".</summary>
    public string FanToolTip
    {
        get => _fanToolTip;
        private set => SetProperty(ref _fanToolTip, value);
    }

    /// <summary>False with Reduce motion on: the fans stand still.</summary>
    public bool AnimateFans
    {
        get => _animateFans;
        set => SetProperty(ref _animateFans, value);
    }

    // ---- Updates from the Overview ------------------------------------------------------------------

    public void SetIdentity(string title, string subtitle, GpuSize gpuSize, string gpuLabel)
    {
        Title = title;
        Subtitle = subtitle;
        GpuSize = gpuSize;
        GpuLabel = gpuLabel;
    }

    public void SetSpecs(IReadOnlyList<KeyValueItem> left, IReadOnlyList<KeyValueItem> right)
    {
        Sync(LeftSpecs, left);
        Sync(RightSpecs, right);
    }

    public void SetScreen(string value, string label)
    {
        ScreenValue = value;
        ScreenLabel = label;
    }

    public void SetFans(PcFanReading reading)
    {
        TopFanSpeed = reading.TopSpeed;
        BottomFanSpeed = reading.BottomSpeed;
        FanToolTip = reading.ToolTip;
    }

    /// <summary>Applies the saved color without raising <see cref="GlowChanged"/>.</summary>
    public void LoadGlow(PcGlow glow)
    {
        if (SetProperty(ref _glow, glow, nameof(Glow)))
        {
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        foreach (var option in GlowOptions)
        {
            option.IsSelected = option.Glow == _glow;
        }
    }

    private static void Sync(ObservableCollection<KeyValueItem> target, IReadOnlyList<KeyValueItem> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
