using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coreloom.Core.Gaming;

/// <summary>Where Steam keeps a game's pictures, from its store item (file names carry a hash for newer uploads).</summary>
/// <param name="Covers">Tall 600 × 900 library covers, best first.</param>
/// <param name="Banners">Wide pictures (the store header), best first.</param>
public sealed record SteamAssets(IReadOnlyList<string> Covers, IReadOnlyList<string> Banners)
{
    public static SteamAssets None { get; } = new([], []);
}

/// <summary>A picture to try downloading: its address and whether it is a tall cover (else a wide banner).</summary>
public readonly record struct PictureSource(string Url, bool IsCover);

/// <summary>
/// Finds pictures for games that have none on the PC through Steam's public store (no account or key): the store
/// search by name (most games from other launchers are on Steam too), then the store item's pictures. Only the game's
/// name or Steam app id is sent. Matching is strict, so a game gets no picture rather than another game's.
/// </summary>
public static partial class SteamStore
{
    private const string AssetHost = "https://shared.akamai.steamstatic.com/store_item_assets/";

    /// <summary>The store search for <paramref name="name"/> (results in English, US store).</summary>
    public static string SearchUrl(string name) =>
        "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(name.Trim()) + "&l=english&cc=US";

    /// <summary>The store item request for <paramref name="appId"/> with its pictures.</summary>
    public static string ItemUrl(string appId)
    {
        string json = "{\"ids\":[{\"appid\":" + long.Parse(appId, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            + "}],\"context\":{\"language\":\"english\",\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
        return "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(json);
    }

    /// <summary>The apps in a store search answer: name and app id, in Steam's order.</summary>
    public static IReadOnlyList<(string Name, string AppId)> ParseSearch(string json)
    {
        var results = new List<(string, string)>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() != "app")
                    || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("id", out var id))
                {
                    continue;
                }

                string? appId = id.ValueKind switch
                {
                    JsonValueKind.Number when id.TryGetInt64(out long n) && n > 0 => n.ToString(CultureInfo.InvariantCulture),
                    JsonValueKind.String when long.TryParse(id.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long n) && n > 0 => n.ToString(CultureInfo.InvariantCulture),
                    _ => null,
                };
                if (appId is not null && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    results.Add((name.GetString()!, appId));
                }
            }
        }
        catch (JsonException)
        {
            // an error page: no results
        }

        return results;
    }

    /// <summary>
    /// The Steam app that is <paramref name="gameName"/>: the same name once case, ™/®, apostrophes, punctuation and
    /// accents are set aside, else the same name without an edition ("Deluxe Edition", "GOTY"). Null when none is;
    /// a soundtrack, a DLC or another game of the series never counts.
    /// </summary>
    public static string? BestMatch(string gameName, IEnumerable<(string Name, string AppId)> results)
    {
        string wanted = Normalize(gameName);
        if (wanted.Length == 0)
        {
            return null;
        }

        var list = results.ToList();
        foreach (var (name, appId) in list)
        {
            if (Normalize(name) == wanted)
            {
                return appId;
            }
        }

        string wantedBase = WithoutEdition(wanted);
        if (wantedBase.Length == 0)
        {
            return null;
        }

        foreach (var (name, appId) in list)
        {
            if (WithoutEdition(Normalize(name)) == wantedBase)
            {
                return appId;
            }
        }

        return null;
    }

    /// <summary>"Assassin’s Creed® Shadows™" → "assassins creed shadows".</summary>
    public static string Normalize(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (char c in name.Normalize(NormalizationForm.FormD))
        {
            switch (c)
            {
                case '™' or '®' or '©' or '\'' or '’' or '‘' or '`' or '´' or 'ʼ':
                    continue; // marks and apostrophes vanish ("Assassin’s" = "Assassins")
                case '&':
                    text.Append(" and ");
                    continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // accents: "Pokémon" = "Pokemon"
            }

            text.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return Spaces().Replace(text.ToString(), " ").Trim();
    }

    /// <summary>A normalized name without a trailing edition: "witcher 3 wild hunt game of the year edition" → "witcher 3 wild hunt".</summary>
    public static string WithoutEdition(string normalized)
    {
        string text = normalized;
        for (string before = string.Empty; before != text;)
        {
            before = text;
            text = Edition().Replace(text, string.Empty).Trim();
        }

        return text;
    }

    /// <summary>The pictures in a store item answer, as full addresses.</summary>
    public static SteamAssets ParseAssets(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("store_items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return SteamAssets.None;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Object
                    || !assets.TryGetProperty("asset_url_format", out var format) || format.ValueKind != JsonValueKind.String
                    || format.GetString() is not { Length: > 0 } pattern || !pattern.Contains("${FILENAME}", StringComparison.Ordinal))
                {
                    continue;
                }

                List<string> Urls(params string[] names) =>
                [
                    .. names
                        .Select(n => assets.TryGetProperty(n, out var file) && file.ValueKind == JsonValueKind.String ? file.GetString() : null)
                        .Where(f => !string.IsNullOrWhiteSpace(f) && !f!.Contains("..", StringComparison.Ordinal))
                        .Select(f => AssetHost + pattern.Replace("${FILENAME}", f, StringComparison.Ordinal)),
                ];

                return new SteamAssets(Urls("library_capsule_2x", "library_capsule"), Urls("header"));
            }
        }
        catch (JsonException)
        {
            // an error page: nothing
        }

        return SteamAssets.None;
    }

    /// <summary>
    /// Pictures to try for <paramref name="appId"/>, best first: the store item's own (which find newer games, whose
    /// files carry a hash), then the long-standing addresses, covers before the wide header.
    /// </summary>
    public static IReadOnlyList<PictureSource> PictureUrls(string appId, SteamAssets? assets)
    {
        var sources = new List<PictureSource>();
        void Add(string url, bool cover)
        {
            if (!sources.Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                sources.Add(new PictureSource(url, cover));
            }
        }

        foreach (string url in assets?.Covers ?? [])
        {
            Add(url, true);
        }

        Add($"{AssetHost}steam/apps/{appId}/library_600x900_2x.jpg", true);
        foreach (string url in SteamLibrary.CoverUrls(appId))
        {
            Add(url, true);
        }

        foreach (string url in assets?.Banners ?? [])
        {
            Add(url, false);
        }

        Add($"{AssetHost}steam/apps/{appId}/header.jpg", false);
        return sources;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // "… deluxe edition", "… game of the year edition", "… goty": a qualifier counts only before "edition" ("Tetris
    // Ultimate" is a game of its own), except GOTY / game of the year, which stand alone too.
    [GeneratedRegex(@"(?:(?:\s+(?:goty|game of the year|standard|deluxe|ultimate|gold|premium|complete|definitive|special|digital|collectors|anniversary|enhanced|legendary|launch|founders|champion|cross gen|vault))*\s+edition|\s+(?:goty|game of the year))$")]
    private static partial Regex Edition();
}
