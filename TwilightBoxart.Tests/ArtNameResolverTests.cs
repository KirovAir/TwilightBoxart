using TwilightBoxart.Core.Index;

namespace TwilightBoxart.Tests;

/// <summary>
/// libretro-thumbnails follows no single No-Intro vintage, so an exact filename comparison misses 42%
/// of the corpus and reports every miss as "this game has no box art". These pin the rules that close
/// that gap, and, just as importantly, the ones that must NOT close it.
/// </summary>
[TestClass]
public class ArtNameResolverTests
{
    private static readonly string[] GameBoy =
    [
        "Fidgetts, The (Japan)",
        "Fidgetts, The (USA, Europe) (En,Fr,De,Es,It,Nl,Sv)",
        "Tetris (World)",
        "Asterix _ Obelix (Europe)",
        "Hook (USA)",
        "Double Dragon (Japan)",
        "Double Dragon (USA)",
        "Croc (USA, Europe)",
        "Croc (USA, Europe) (Beta)",
        "Mario no Picross (Japan) (SGB Enhanced)"
    ];

    [TestMethod]
    public void Resolve_PrefersTheExactName()
    {
        var match = new ThumbnailIndex(GameBoy).Resolve("Tetris (World)");

        Assert.AreEqual("Tetris (World)", match?.FileName);
        Assert.AreEqual(ArtMatchTier.Exact, match?.Tier);
    }

    /// <summary>
    /// The reported bug. No-Intro added a language tag to Japan-region English releases; the thumbnail
    /// was filed before that happened, so the two names describe one dump.
    /// </summary>
    [TestMethod]
    public void Resolve_DropsALanguageTagTheThumbnailPredates()
    {
        var match = new ThumbnailIndex(GameBoy).Resolve("Fidgetts, The (Japan) (En)");

        Assert.AreEqual("Fidgetts, The (Japan)", match?.FileName);
        Assert.AreEqual(ArtMatchTier.Language, match?.Tier);
    }

    /// <summary>
    /// Tags that are not languages carry identity and must survive the same pass, or "(Rev 1)" and
    /// "(GB Compatible)" collapse into each other and a title gets the wrong cover.
    /// </summary>
    [TestMethod]
    [DataRow("Tetris (World) (Rev 1)")]
    [DataRow("Tetris (World) (GB Compatible)")]
    [DataRow("Tetris (World) (Unl)")]
    public void Resolve_KeepsTagsThatAreNotLanguages(string name)
    {
        var match = new ThumbnailIndex(["Tetris (World)", "Tetris (World) (Rev 1)"]).Resolve(name);

        // It may still resolve by title, but never by pretending the tag was a language.
        Assert.AreNotEqual(ArtMatchTier.Language, match?.Tier);
    }

    /// <summary>
    /// The rule the whole tier order exists for: when the right region has a cover, nothing else may
    /// win. "Double Dragon" ships in both halves of the world and the boxes are not the same picture.
    /// </summary>
    [TestMethod]
    public void Resolve_TakesTheMatchingRegionBeforeAnyOther()
    {
        var index = new ThumbnailIndex(GameBoy);

        Assert.AreEqual("Double Dragon (Japan)", index.Resolve("Double Dragon (Japan) (Rev 1)")?.FileName);
        Assert.AreEqual("Double Dragon (USA)", index.Resolve("Double Dragon (USA) (Beta)")?.FileName);
    }

    /// <summary>
    /// Two files differing only by language tag are two different releases, so collapsing the tag has
    /// to be unambiguous or the tier picks one of them at random.
    /// </summary>
    [TestMethod]
    public void Resolve_RefusesAnAmbiguousLanguageCollapse()
    {
        var index = new ThumbnailIndex(["Boxxle (Japan) (En)", "Boxxle (Japan) (Ja)"]);

        Assert.AreNotEqual(ArtMatchTier.Language, index.Resolve("Boxxle (Japan) (Fr)")?.Tier);
    }

    /// <summary>
    /// The tier that exists so an Asian pirate cart takes the Japanese cover rather than the American
    /// one. Without it the only remaining option is the cross-region tier, which is the last resort.
    /// </summary>
    [TestMethod]
    public void Resolve_PrefersTheSameRegionFamilyBeforeCrossingTheWorld()
    {
        var index = new ThumbnailIndex(["Zanac (Japan)", "Zanac (USA)"]);
        var match = index.Resolve("Zanac (Taiwan) (En) (Pirate)");

        Assert.AreEqual("Zanac (Japan)", match?.FileName);
        Assert.AreEqual(ArtMatchTier.RegionFamily, match?.Tier);
    }

    /// <summary>A World cover serves more of the library than any single-region one, so it wins a tie.</summary>
    [TestMethod]
    public void Resolve_PrefersAWorldCoverWhenNothingMatchesTheRegion()
    {
        var index = new ThumbnailIndex(["Gemfire (World)", "Gemfire (Japan)"]);

        Assert.AreEqual("Gemfire (World)", index.Resolve("Gemfire (Brazil) (En)")?.FileName);
    }

    /// <summary>
    /// The same title spelled two ways. Pinyin splits words differently between DATs and punctuation
    /// drifts, and neither changes which game it is.
    /// </summary>
    [TestMethod]
    [DataRow("Yinglie Qunxiazhuan (China) (Pirate)", "Ying Lie Qun Xia Zhuan (Asia)")]
    [DataRow("Earthworm Jim 2 (Taiwan) (En) (Pirate)", "Earth Worm Jim 2 (Asia)")]
    [DataRow("Super Breakout (USA)", "Super Breakout! (USA)")]
    public void Resolve_LooksThroughSpacingAndPunctuation(string ours, string theirs)
    {
        Assert.AreEqual(theirs, new ThumbnailIndex([theirs]).Resolve(ours)?.FileName);
    }

    /// <summary>
    /// Why the tier above is an equality and not a fuzzy match. Levenshtein scores every one of these
    /// above 0.90, and every one of them is a different game with a different box. A digit is not noise.
    /// </summary>
    [TestMethod]
    [DataRow("Tomb Raider 2 (Taiwan)", "Tomb Raider (Europe)")]
    [DataRow("World Championship Soccer (Japan, USA)", "World Championship Soccer II (USA)")]
    [DataRow("FIFA Soccer 99 (China) (Pirate)", "FIFA Soccer 97 (USA, Europe)")]
    [DataRow("Super Mario World 9 (Asia) (En) (Pirate)", "Super Mario World (USA)")]
    public void Resolve_NeverBorrowsACoverFromANumberedSequel(string ours, string theirs)
    {
        Assert.IsNull(new ThumbnailIndex([theirs]).Resolve(ours));
    }

    /// <summary>
    /// Characters that join or qualify a title have to survive into the key. Every pair here is real and
    /// sits on the same console in the current index; drop the character and they become one string.
    /// </summary>
    [TestMethod]
    [DataRow("Dragon Warrior III (USA)", "Dragon Warrior I _ II (USA)")]
    [DataRow("Final Fantasy III (Japan)", "Final Fantasy I, II (Japan)")]
    [DataRow("Love Plus (Japan)", "Love Plus+ (Japan)")]
    [DataRow("Korg DS-10 Synthesizer (USA)", "Korg DS-10+ Synthesizer (USA)")]
    public void Resolve_DoesNotConfuseTitlesThatDifferOnlyByAJoiner(string ours, string theirs)
    {
        Assert.IsNull(new ThumbnailIndex([theirs]).Resolve(ours));
    }

    /// <summary>The other direction: the compilation still finds its own cover.</summary>
    [TestMethod]
    public void Resolve_StillFindsTheCompilationsOwnCover()
    {
        // "Dragon Warrior I _ II" is how libretro stores the ampersand; see LibRetroArtSource.SanitizeName.
        var index = new ThumbnailIndex(["Dragon Warrior I _ II (USA)"]);

        Assert.IsNotNull(index.Resolve("Dragon Warrior I & II (USA) (Rev 1)"));
    }

    /// <summary>
    /// Dropping a language tag the other side never carried is a naming difference. Swapping one
    /// language for another is a different release, and must not pass as one.
    /// </summary>
    [TestMethod]
    public void Resolve_WillNotSwapOneLanguageForAnother()
    {
        Assert.AreNotEqual(
            ArtMatchTier.Language,
            new ThumbnailIndex(["Boxxle (Japan) (Ja)"]).Resolve("Boxxle (Japan) (Fr)")?.Tier);
    }

    /// <summary>
    /// The marker can sit anywhere in the tag, and "Hacker International" is a real NES publisher whose
    /// covers are retail. Both directions matter: 31 genuine covers hang on the second one.
    /// </summary>
    [TestMethod]
    [DataRow("Wagyan Land (Japan) (Possible Proto)", false)]
    [DataRow("Wagyan Land (Japan) (Auto Demo)", false)]
    [DataRow("Wagyan Land (Japan) (Promo)", false)]
    [DataRow("Wagyan Land (Japan) (Hacker International)", true)]
    [DataRow("Wagyan Land (Japan)", true)]
    public void IsRetailRelease_ReadsTheWholeTag(string fileName, bool retail)
    {
        Assert.AreEqual(retail, RomName.IsRetailRelease(fileName));
    }

    /// <summary>A foreign cover beats no cover, but only once the same region has been ruled out.</summary>
    [TestMethod]
    public void Resolve_FallsBackAcrossRegionsLast()
    {
        var match = new ThumbnailIndex(GameBoy).Resolve("Hook (Japan) (Proto)");

        Assert.AreEqual("Hook (USA)", match?.FileName);
        Assert.AreEqual(ArtMatchTier.AnyRegion, match?.Tier);
    }

    /// <summary>
    /// A prototype's own thumbnail is a picture of a prototype, not of a box, so it can never stand in
    /// for the retail cover the caller actually wants.
    /// </summary>
    [TestMethod]
    public void Resolve_NeverSubstitutesAPrototypeThumbnail()
    {
        var match = new ThumbnailIndex(GameBoy).Resolve("Croc (USA, Europe) (Beta 1)");

        Assert.AreEqual("Croc (USA, Europe)", match?.FileName);
    }

    /// <summary>
    /// GoodNES dump flags ("[b]", "[tr en ...]") litter the NES repository and none of them is a
    /// picture of a box, so a bare-title match must not pick one up. The unflagged file next to it is
    /// a real cover and should still be found.
    /// </summary>
    [TestMethod]
    public void Resolve_IgnoresDumpFlaggedFiles()
    {
        Assert.IsNull(new ThumbnailIndex(["Contra (1988-02)(Konami)(US)[b3]"]).Resolve("Contra (USA) (Beta)"));

        var match = new ThumbnailIndex(["Contra (1988-02)(Konami)(US)[b3]", "Contra (1988-02)(Konami)(US)"])
            .Resolve("Contra (USA) (Beta)");

        Assert.AreEqual("Contra (1988-02)(Konami)(US)", match?.FileName);

        // And it counts as the right region: "(US)" is GoodNES for USA, so this must not be reached by
        // the cross-region tier that exists only for when nothing local was found.
        Assert.AreEqual(ArtMatchTier.Region, match?.Tier);
    }

    /// <summary>
    /// Titles whose regional releases are different games: "Super Mario Bros. 2" is Lost Levels in
    /// Japan and a reskinned Doki Doki Panic in the USA. Cross-region borrowing must not apply.
    /// </summary>
    [TestMethod]
    public void Resolve_RefusesToBorrowACoverForADivergentTitle()
    {
        var index = new ThumbnailIndex(["Super Mario Bros. 2 (USA)", "Super Mario Bros. 2 (Europe)"]);

        Assert.IsNull(index.Resolve("Super Mario Bros. 2 (Japan) (Rev 1)"));
        Assert.AreEqual("Super Mario Bros. 2 (USA)", index.Resolve("Super Mario Bros. 2 (USA) (Beta)")?.FileName);
    }

    /// <summary>Ampersands are stored as underscores upstream, so the sanitiser has to run before any comparison.</summary>
    [TestMethod]
    public void Resolve_AppliesLibRetrosIllegalCharacterRule()
    {
        Assert.AreEqual("Asterix _ Obelix (Europe)", new ThumbnailIndex(GameBoy).Resolve("Asterix & Obelix (Europe)")?.FileName);
    }

    [TestMethod]
    public void Resolve_ReturnsNullWhenTheTitleIsAbsent()
    {
        Assert.IsNull(new ThumbnailIndex(GameBoy).Resolve("Waimanu - Daring Slides (World)"));
    }

    // the name parsing the tiers are built on

    [TestMethod]
    [DataRow("Fidgetts, The (Japan) (En)", "Fidgetts, The (Japan)")]
    [DataRow("Fidgetts, The (USA, Europe) (En,Fr,De,Es,It,Nl,Sv)", "Fidgetts, The (USA, Europe)")]
    [DataRow("Zelda (Japan) (Rev 1) (SGB Enhanced)", "Zelda (Japan) (Rev 1) (SGB Enhanced)")]
    [DataRow("Wordyl (World) (Pt-BR) (v1.0.3)", "Wordyl (World) (v1.0.3)")]
    public void StripLanguageTags_RemovesOnlyLanguages(string input, string expected)
    {
        Assert.AreEqual(expected, RomName.StripLanguageTags(input));
    }

    [TestMethod]
    [DataRow("Croc (USA, Europe) (Beta 1)", "Croc")]
    [DataRow("Contra (1988-02)(Konami)(US)[b3]", "Contra")]
    public void BareTitle_DropsEveryTag(string input, string expected)
    {
        Assert.AreEqual(expected, RomName.BareTitle(input));
    }

    [TestMethod]
    [DataRow("Croc (USA, Europe) (Rev 1)", "usa,europe")]
    [DataRow("Zelda (Japan) (En)", "japan")]
    [DataRow("Adulting! (World) (v2.0)", "world")]
    [DataRow("Contra (1988-02)(Konami)(US)", "usa")]
    [DataRow("Contra (1988-02)(Konami)(LJN)", "")]
    public void Regions_ReadsTheFirstRegionTagOnly(string input, string expected)
    {
        Assert.AreEqual(expected, string.Join(',', RomName.Regions(input)));
    }
}
