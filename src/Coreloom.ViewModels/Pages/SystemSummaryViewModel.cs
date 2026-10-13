using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Hardware;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// The Overview header: the board name as the title, the graphics card as the subtitle, two columns of key specs, and
/// what the PC illustration needs (memory sticks, graphics card size and badge, the temperature on the pump screen).
/// </summary>
public sealed class SystemSummaryViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _fourMemorySticks;
    private GpuSize _gpuSize = GpuSize.None;
    private string _gpuLabel = "GPU";
    private string _screenValue = "—";
    private string _screenLabel = "CPU";

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

    public bool FourMemorySticks
    {
        get => _fourMemorySticks;
        private set => SetProperty(ref _fourMemorySticks, value);
    }

    public GpuSize GpuSize
    {
        get => _gpuSize;
        private set
        {
            if (SetProperty(ref _gpuSize, value))
            {
                OnPropertyChanged(nameof(HasLargeGpu));
                OnPropertyChanged(nameof(HasMediumGpu));
            }
        }
    }

    public bool HasLargeGpu => GpuSize == GpuSize.Large;

    public bool HasMediumGpu => GpuSize == GpuSize.Medium;

    /// <summary>Model badge on the drawn graphics card, e.g. "RTX 5090".</summary>
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

    public void SetIdentity(string title, string subtitle, bool fourMemorySticks, GpuSize gpuSize, string gpuLabel)
    {
        Title = title;
        Subtitle = subtitle;
        FourMemorySticks = fourMemorySticks;
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
