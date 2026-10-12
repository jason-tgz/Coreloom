using System.Security.Cryptography;

namespace Vigil.Core.Updates;

/// <summary>Parses <c>SHA256SUMS.txt</c> files (sha256sum / Get-FileHash style) and verifies downloads.</summary>
public static class Checksums
{
    /// <summary>
    /// Parses lines of the form <c>&lt;64 hex chars&gt;  &lt;file name&gt;</c> (an optional '*' before the name
    /// marks binary mode). Returns a case-insensitive map of file name → lowercase hex digest.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(content))
        {
            return map;
        }

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length < 66 || line.StartsWith('#'))
            {
                continue;
            }

            string hash = line[..64];
            if (!hash.All(Uri.IsHexDigit))
            {
                continue;
            }

            string name = line[64..].Trim().TrimStart('*').Trim();
            if (name.Length == 0)
            {
                continue;
            }

            map[Path.GetFileName(name)] = hash.ToLowerInvariant();
        }

        return map;
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> VerifyAsync(string filePath, string expectedHex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return false;
        }

        string actual = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual),
            System.Text.Encoding.ASCII.GetBytes(expectedHex.Trim().ToLowerInvariant()));
    }
}
