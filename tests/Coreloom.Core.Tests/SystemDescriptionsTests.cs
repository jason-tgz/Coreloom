using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;

namespace Coreloom.Core.Tests;

public class SystemDescriptionsTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly UnitFormatter Formatter = new(culture: CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC. ROG STRIX X870E-E GAMING WIFI", "ASUS ROG STRIX X870E-E GAMING WIFI")]
    [InlineData("Micro-Star International Co., Ltd. MAG X870E TOMAHAWK WIFI (MS-7E59)", "MSI MAG X870E TOMAHAWK WIFI")]
    [InlineData("Gigabyte Technology Co., Ltd. X870E AORUS MASTER", "Gigabyte X870E AORUS MASTER")]
    [InlineData("ASRock X870E Nova WiFi", "ASRock X870E Nova WiFi")]
    [InlineData("ASUSTeK COMPUTER INC. ASUS PRIME B650-PLUS", "ASUS PRIME B650-PLUS")]
    [InlineData("Dell Inc. 0X8DXD", "Dell 0X8DXD")]
    [InlineData("NZXT N7 Z790", "NZXT N7 Z790")]
    [InlineData("  Some   Board  ", "Some Board")]
    public void BoardName_UsesTheNamePeopleKnow(string raw, string expected) =>
        Assert.Equal(expected, SystemDescriptions.BoardName(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ASRock Default string")]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("ASUSTeK COMPUTER INC.")]
    public void BoardName_IsNullWhenTheFirmwareDoesNotNameIt(string? raw) =>
        Assert.Null(SystemDescriptions.BoardName(raw));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", "RTX 5090")]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", "RTX 4070 Ti")]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "RTX 4060")]
    [InlineData("AMD Radeon RX 9070 XT", "RX 9070 XT")]
    [InlineData("AMD Radeon(TM) RX 7900 XTX", "RX 7900 XTX")]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", "Arc B580")]
    [InlineData("AMD Radeon(TM) Graphics", "GPU")]
    [InlineData(null, "GPU")]
    public void GpuBadge_IsShortEnoughForTheCard(string? name, string expected)
    {
        string badge = SystemDescriptions.GpuBadge(name);
        Assert.Equal(expected, badge);
        Assert.True(badge.Length <= 12);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", false, GpuSize.Large)]
    [InlineData("NVIDIA GeForce RTX 4080 SUPER", false, GpuSize.Large)]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", false, GpuSize.Medium)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", false, GpuSize.Medium)]
    [InlineData("NVIDIA TITAN RTX", false, GpuSize.Large)]
    [InlineData("AMD Radeon RX 7900 XTX", false, GpuSize.Large)]
    [InlineData("AMD Radeon RX 9070 XT", false, GpuSize.Large)]
    [InlineData("AMD Radeon RX 9060 XT", false, GpuSize.Medium)]
    [InlineData("AMD Radeon RX 7600", false, GpuSize.Medium)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", false, GpuSize.Medium)]
    [InlineData("AMD Radeon(TM) Graphics", true, GpuSize.None)]
    [InlineData(null, false, GpuSize.None)]
    public void GpuSize_FollowsTheTier(string? name, bool integrated, GpuSize expected) =>
        Assert.Equal(expected, SystemDescriptions.GpuSizeFor(name, integrated));

    [Fact]
    public void MemoryLine_DescribesSizeTypeSpeedAndLayout()
    {
        var modules = new[] { Module("DIMM A2", 32, 6000), Module("DIMM B2", 32, 6000) };

        Assert.Equal("64 GB DDR5-6000 · 2 × 32 GB", SystemDescriptions.MemoryLine(64 * GiB, modules, Formatter));
        Assert.False(SystemDescriptions.HasFourMemorySticks(modules));
    }

    [Fact]
    public void MemoryLine_MixedModulesAndEmptySlots()
    {
        var modules = new[] { Module("A1", 16, 3200, "DDR4"), Module("A2", 8, 3200, "DDR4"), Module("B1", 16, 3200, "DDR4"), Module("B2", 0, 0, "Unknown") };

        Assert.Equal("40 GB DDR4-3200 · 3 modules", SystemDescriptions.MemoryLine(40 * GiB, modules, Formatter));
        Assert.True(SystemDescriptions.HasFourMemorySticks(modules));
    }

    [Fact]
    public void MemoryLine_WithoutModuleDetails()
    {
        Assert.Equal("32 GB", SystemDescriptions.MemoryLine(32 * GiB, [], Formatter));
        Assert.Equal("—", SystemDescriptions.MemoryLine(0, [], Formatter));
    }

    [Theory]
    [InlineData(16, 32, "16 cores · 32 threads")]
    [InlineData(1, 1, "1 core · 1 thread")]
    [InlineData(0, 8, "8 threads")]
    public void CoresLine(int cores, int threads, string expected) =>
        Assert.Equal(expected, SystemDescriptions.CoresLine(cores, threads));

    [Theory]
    [InlineData("Windows 11 Pro", "24H2 (build 26100.4061)", "Windows 11 Pro 24H2")]
    [InlineData("Windows 11 Home", "Build 22000.1", "Windows 11 Home")]
    [InlineData("Windows 10 Pro", "", "Windows 10 Pro")]
    public void WindowsLine(string os, string version, string expected) =>
        Assert.Equal(expected, SystemDescriptions.WindowsLine(os, version));

    private static MemoryModuleInfo Module(string locator, int gib, int speed, string type = "DDR5") =>
        new(locator, null, gib * GiB, type, speed, speed, "Vendor", "PART");
}
