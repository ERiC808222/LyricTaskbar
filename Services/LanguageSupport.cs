using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public static class LanguageSupport
{
    public static bool HasTibetan(string text) => Regex.IsMatch(text ?? "", @"[\u0f00-\u0fff]");
    public static bool HasKorean(string text) => Regex.IsMatch(text ?? "", @"[\u1100-\u11ff\u3130-\u318f\uac00-\ud7af]");
    public static string FontFamilyFor(string text) => HasTibetan(text) ? "Microsoft Himalaya, Global User Interface" : HasKorean(text)
        ? "Malgun Gothic, Segoe UI, Global User Interface" : LyricMatcher.ContainsKana(text)
        ? "Yu Gothic UI, Meiryo, Microsoft YaHei UI, Global User Interface" : "Segoe UI, Microsoft YaHei UI, Global User Interface";
    public static double LyricFontSize(string text, bool dual) => HasTibetan(text) ? (dual ? 20 : 25) : dual ? 13.5 : 16.5;
    // Remove only Latin accent marks for catalog matching. Tibetan vowels and Korean jamo remain intact.
    public static string FoldLatinAccents(string text)
    {
        var result = new StringBuilder(); bool latinBase = false;
        foreach (var rune in (text ?? "").Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            { if (!latinBase) result.Append(rune.ToString()); }
            else { latinBase = rune.Value is >= 0x41 and <= 0x24f or >= 0x1e00 and <= 0x1eff; result.Append(rune.ToString()); }
        }
        return result.ToString().Normalize(NormalizationForm.FormKC);
    }
    public static string[] CatalogRegions(LyricsLanguageMode language, string metadata) => language switch
    {
        LyricsLanguageMode.Korean => ["kr", "us"], LyricsLanguageMode.Tibetan => ["cn", "us"],
        LyricsLanguageMode.French => ["fr", "us"], LyricsLanguageMode.Spanish => ["es", "mx", "us"],
        LyricsLanguageMode.Japanese => ["jp", "us"], LyricsLanguageMode.Chinese => ["cn", "us"],
        _ when HasKorean(metadata) => ["kr", "us"], _ when HasTibetan(metadata) => ["cn", "us"],
        _ when LyricMatcher.ContainsKana(metadata) => ["jp", "us"], _ => ["us", "fr", "es"]
    };
    public static string[] AliasRegions(LyricsLanguageMode language, string metadata) => language switch
    {
        LyricsLanguageMode.Korean => ["kr"], LyricsLanguageMode.Tibetan => ["cn"], LyricsLanguageMode.French => ["fr"],
        LyricsLanguageMode.Spanish => ["es"], LyricsLanguageMode.Japanese => ["jp"], LyricsLanguageMode.Chinese => ["cn"],
        _ when HasKorean(metadata) => ["kr"], _ when HasTibetan(metadata) => ["cn"], _ => ["jp"]
    };
}
