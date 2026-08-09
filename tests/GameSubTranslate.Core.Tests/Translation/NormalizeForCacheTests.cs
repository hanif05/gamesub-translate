using GameSubTranslate.Translation;
using Xunit;

namespace GameSubTranslate.Core.Tests.Translation;

/// <summary>Regression for the Pragmata OCR-noise issue: the same dialog line read by Tesseract
/// 3 different ways (noise glyphs + nameplate fragments) must collapse to one normalized form so
/// the pipeline fires exactly one translation, not one per frame.</summary>
public class NormalizeForCacheTests
{
    [Fact]
    public void NoiseVariants_CollapseToSameKey()
    {
        // The 3 Pragmata cycles read the same dialog ("Diana, aren't you tired?") 3 different
        // ways. All the nameplate/decoration noise must vanish and the dialog line must survive.
        // Cycle 1 and 2 keep the dialog word-for-word → same key. Cycle 3 mis-reads the first
        // word ("Done" vs "Diana") → a *different* key, but one that's 90% similar, so the
        // fuzzy cache (Fix 3) still reuses the cached translation instead of re-calling the API.
        var variants = new[]
        {
            "| SS Bu as. |\n\\.. 1 =\nA A Diana, aren't you tired? g\n\nAW a!",
            "~ Hugh ss & Scere\nlug WIZ A\n\n. Dy Diana, aren't you tired? Te",
            "» Hugh De\n\nfl A\nDone aren't you tired? me |",
        };
        var normed = variants.Select(TextCleaning.NormalizeForCache).ToList();
        Assert.Equal("Diana, aren't you tired?", normed[0]);
        Assert.Equal("Diana, aren't you tired?", normed[1]);
        Assert.Equal("Done aren't you tired?", normed[2]);
    }

    [Fact]
    public void NameplateFragments_Dropped()
    {
        // From the issue log: each is a character name / UI-chrome fragment with no dialog.
        // ("~ Hugh ss & Scere" is deliberately absent — in the log it always accompanies a
        // dialog line below it, and the dominant-line layer handles that case.)
        Assert.Equal("", TextCleaning.NormalizeForCache("| SS Bu as. |"));
        Assert.Equal("", TextCleaning.NormalizeForCache("\\.. 1 ="));
        Assert.Equal("", TextCleaning.NormalizeForCache("fl A"));
        Assert.Equal("", TextCleaning.NormalizeForCache("» Hugh De"));
    }

    [Fact]
    public void RealDialog_KeepsTheLineAndTrimsNoise()
    {
        // Nameplate + decoration lines are dropped; the dialog line survives with a leading
        // short-word ("A A ", ". Dy ") trimmed off.
        Assert.Equal("Diana, aren't you tired?", TextCleaning.NormalizeForCache("| SS Bu as. |\n\\.. 1 =\nA A Diana, aren't you tired? g\n\nAW a!"));
        Assert.Equal("Halo dunia", TextCleaning.NormalizeForCache("  Halo    dunia  "));
        Assert.Equal("Diana, aren't you tired?", TextCleaning.NormalizeForCache(". Dy Diana, aren't you tired? Te"));
    }

    [Fact]
    public void EmptyInputs_ReturnEmpty()
    {
        Assert.Equal("", TextCleaning.NormalizeForCache(null));
        Assert.Equal("", TextCleaning.NormalizeForCache(""));
        Assert.Equal("", TextCleaning.NormalizeForCache("   \n \n "));
    }
}
