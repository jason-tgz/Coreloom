using System.Globalization;

namespace Coreloom.Core.Updates;

/// <summary>
/// Semantic version (MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]) with SemVer 2.0 precedence rules.
/// Accepts an optional leading "v" (git tags such as v1.2.3).
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null, string? build = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
        Build = string.IsNullOrEmpty(build) ? null : build;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? PreRelease { get; }

    public string? Build { get; }

    public bool IsPreRelease => PreRelease is not null;

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a valid semantic version.");

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        string? build = null;
        int plus = s.IndexOf('+');
        if (plus >= 0)
        {
            build = s[(plus + 1)..];
            s = s[..plus];
            if (build.Length == 0)
            {
                return false;
            }
        }

        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0))
            {
                return false;
            }
        }

        string[] parts = s.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        if (!TryParsePart(parts[0], out int major) || !TryParsePart(parts[1], out int minor))
        {
            return false;
        }

        int patch = 0;
        if (parts.Length >= 3 && !TryParsePart(parts[2], out patch))
        {
            return false;
        }

        // A fourth numeric part (assembly-style 1.2.3.0) is accepted and ignored when zero.
        if (parts.Length == 4 && (!TryParsePart(parts[3], out int revision) || revision != 0))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, pre, build);
        return true;
    }

    private static bool TryParsePart(string part, out int value) =>
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A version without a pre-release tag has higher precedence.
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;

        string[] a = PreRelease.Split('.');
        string[] b = other.PreRelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool aNum = long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out long an);
            bool bNum = long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out long bn);
            if (aNum && bNum)
            {
                c = an.CompareTo(bn);
            }
            else if (aNum)
            {
                c = -1; // numeric identifiers have lower precedence
            }
            else if (bNum)
            {
                c = 1;
            }
            else
            {
                c = string.CompareOrdinal(a[i], b[i]);
            }

            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString()
    {
        string core = $"{Major}.{Minor}.{Patch}";
        if (PreRelease is not null)
        {
            core += "-" + PreRelease;
        }

        return core;
    }

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
}
