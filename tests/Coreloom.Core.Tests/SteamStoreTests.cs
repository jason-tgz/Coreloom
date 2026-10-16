using Coreloom.Core.Gaming;

namespace Coreloom.Core.Tests;

public class SteamStoreTests
{
    // Steam's answer for "Assassin's Creed Shadows" (October 2026), shortened.
    private const string Search = """
        {"items":[
          {"type":"app","name":"Assassin’s Creed Shadows","id":3159330,"tiny_image":"https:\/\/shared.akamai.steamstatic.com\/store_item_assets\/steam\/apps\/3159330\/fe6c8f40f405c8bb3a05788d1316ebd8e3c31b6a\/capsule_231x87.jpg?t=1786637452"},
          {"type":"app","name":"Assassin's Creed Shadows - Premium Starter Pack","id":3362570},
          {"type":"app","name":"Assassin's Creed Shadows - Claws of Awaji","id":3362550}
        ]}
        """;

    private const string Item = """
        {"response":{"store_items":[{"item_type":0,"id":3159330,"success":1,"visible":true,"name":"Assassin’s Creed Shadows","appid":3159330,
          "assets":{"asset_url_format":"steam/apps/3159330/${FILENAME}?t=1789746114",
            "main_capsule":"fe6c8f40f405c8bb3a05788d1316ebd8e3c31b6a/capsule_616x353.jpg",
            "header":"header.jpg",
            "library_capsule":"b52322f78cbdc7e6c267d873056abba7916f7ff3/library_600x900.jpg",
            "library_capsule_2x":"b52322f78cbdc7e6c267d873056abba7916f7ff3/library_600x900_2x.jpg"}}]}}
        """;

    [Fact]
    public void Search_FindsTheGameItself_NotItsDownloadableContent()
    {
        var results = SteamStore.ParseSearch(Search);
        Assert.Equal(3, results.Count);
        Assert.Equal("3159330", SteamStore.BestMatch("Assassin's Creed Shadows", results)); // ’ and ' are the same
        Assert.Equal("3159330", SteamStore.BestMatch("Assassin's Creed® Shadows™", results));
        Assert.Null(SteamStore.BestMatch("Assassin's Creed Mirage", results));
    }

    [Theory]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", "The Witcher 3: Wild Hunt", true)]
    [InlineData("Diablo IV", "Diablo® IV", true)]
    [InlineData("Battlefield™ 2042", "Battlefield 2042", true)]
    [InlineData("Fallout 4 GOTY", "Fallout 4", true)]
    [InlineData("Red Dead Redemption 2", "Red Dead Redemption 2: Ultimate Edition", true)]
    [InlineData("Pokémon & Friends", "Pokemon and Friends", true)]
    [InlineData("Tetris Ultimate", "Tetris", false)] // a qualifier counts only before "Edition"
    [InlineData("VALORANT", "VALORANT Soundtrack", false)]
    [InlineData("Halo Infinite", "Halo: The Master Chief Collection", false)]
    [InlineData("Edition", "Something", false)]
    public void Names_MatchOnlyTheSameGame(string game, string onSteam, bool matches)
    {
        string? found = SteamStore.BestMatch(game, [(onSteam, "42")]);
        Assert.Equal(matches ? "42" : null, found);
    }

    [Fact]
    public void AnExactName_WinsOverAnEditionMatch()
    {
        Assert.Equal("2", SteamStore.BestMatch("Control Ultimate Edition", [("Control", "1"), ("Control Ultimate Edition", "2")]));
    }

    [Fact]
    public void Normalize_SetsAsideMarksApostrophesPunctuationAndAccents()
    {
        Assert.Equal("assassins creed shadows", SteamStore.Normalize("Assassin’s Creed® Shadows™"));
        Assert.Equal("tom clancys rainbow six siege", SteamStore.Normalize("Tom Clancy's Rainbow Six® Siege"));
        Assert.Equal("pokemon and friends", SteamStore.Normalize("  Pokémon  &  Friends! "));
        Assert.Equal("the witcher 3 wild hunt", SteamStore.WithoutEdition(SteamStore.Normalize("The Witcher 3: Wild Hunt – Game of the Year Edition")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Access denied</html>")]
    [InlineData("{\"items\":\"none\"}")]
    [InlineData("{\"items\":[{\"type\":\"sub\",\"name\":\"Bundle\",\"id\":5},{\"type\":\"app\",\"name\":\"\",\"id\":6},{\"type\":\"app\",\"name\":\"X\",\"id\":-1}]}")]
    public void Search_IgnoresAnythingButApps(string json) => Assert.Empty(SteamStore.ParseSearch(json));

    [Fact]
    public void Item_GivesTheHashedPictures_ThenTheLongStandingAddresses()
    {
        var assets = SteamStore.ParseAssets(Item);
        Assert.Equal(
            "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3159330/b52322f78cbdc7e6c267d873056abba7916f7ff3/library_600x900_2x.jpg?t=1789746114",
            assets.Covers[0]);
        Assert.Equal(2, assets.Covers.Count);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3159330/header.jpg?t=1789746114", Assert.Single(assets.Banners));

        var sources = SteamStore.PictureUrls("3159330", assets);
        Assert.Equal(assets.Covers[0], sources[0].Url);
        Assert.True(sources[0].IsCover);
        Assert.Contains(sources, s => s.IsCover && s.Url == "https://cdn.cloudflare.steamstatic.com/steam/apps/3159330/library_600x900_2x.jpg");
        int lastCover = sources.ToList().FindLastIndex(s => s.IsCover);
        int firstBanner = sources.ToList().FindIndex(s => !s.IsCover);
        Assert.True(lastCover < firstBanner, "covers come before the wide header");
        Assert.Equal(sources.Count, sources.Select(s => s.Url).Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"response\":{}}")]
    [InlineData("{\"response\":{\"store_items\":[{\"id\":1,\"success\":2}]}}")]
    [InlineData("{\"response\":{\"store_items\":[{\"assets\":{\"asset_url_format\":\"steam/apps/1/x.jpg\"}}]}}")]
    public void Item_WithoutPictures_LeavesTheLongStandingAddresses(string json)
    {
        var assets = SteamStore.ParseAssets(json);
        Assert.Empty(assets.Covers);
        var sources = SteamStore.PictureUrls("1", assets);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1/library_600x900_2x.jpg", sources[0].Url);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1/header.jpg", sources[^1].Url);
        Assert.False(sources[^1].IsCover);
    }

    [Fact]
    public void Requests_SendOnlyTheNameOrTheAppId()
    {
        Assert.Equal("https://store.steampowered.com/api/storesearch/?term=Assassin%27s%20Creed%20Shadows&l=english&cc=US", SteamStore.SearchUrl(" Assassin's Creed Shadows "));
        string item = SteamStore.ItemUrl("3159330");
        Assert.StartsWith("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=", item);
        Assert.Contains(Uri.EscapeDataString("{\"appid\":3159330}"), item);
        Assert.Contains(Uri.EscapeDataString("\"include_assets\":true"), item);
        Assert.Throws<FormatException>(() => SteamStore.ItemUrl("1; drop"));
    }
}
