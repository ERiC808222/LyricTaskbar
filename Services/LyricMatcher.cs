using System.Text;
using System.Text.RegularExpressions;

namespace DesktopLyrics.Services;

/// <summary>Preserves original scripts; matches track identity before using its lyrics.</summary>
public static class LyricMatcher
{
    public static bool ContainsKana(string value) => Regex.IsMatch(value, @"[\u3040-\u30ff\uff66-\uff9f]");

    public static string Normalize(string value) => Regex.Replace(
        (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\s\p{P}\p{S}]", "");

    public static string CleanTitle(string value)
    {
        var title = (value ?? "").Normalize(NormalizationForm.FormKC).Trim();
        // Remove presentation labels only. Keep version labels and legitimate Japanese brackets.
        title = Regex.Replace(title,
            @"\s*[\[【(](?:official(?:\s+(?:music\s*)?video|\s+audio)?|lyrics?(?:\s+video)?|MV|HD|4K|公式(?:MV|動画)?|歌詞(?:付き|動画)?)[\]】)]\s*",
            " ", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*[-|]\s*(?:official\s+(?:music\s+)?video|official\s+audio|lyrics?\s+video|公式MV|歌詞動画)\s*$", "", RegexOptions.IgnoreCase);
        return Regex.Replace(title, @"\s+", " ").Trim();
    }

    public static string CleanArtist(string value)
    {
        var artist = (value ?? "").Normalize(NormalizationForm.FormKC).Trim();
        // Native Apple Music exposes "artist — album" in its SMTC Artist field.
        artist = Regex.Replace(artist, @"\s+[—•·]\s+.*$", "");
        return Regex.Replace(artist, @"\s*(?:-\s*Topic|VEVO)$", "", RegexOptions.IgnoreCase).Trim();
    }

    private static string Version(string value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        // Avoid substituting a cover, instrumental, live or different-language recording.
        return string.Join("|", new[] { "live", "first take", "ライブ", "现场", "acoustic", "アコースティック", "remix", "リミックス", "instrumental", "inst", "off vocal", "カラオケ", "cover", "カバー", "english", "japanese", "korean", "tibetan", "french", "spanish", "français", "español", "한국어" }
            .Where(token => Regex.IsMatch(text, @"(?:^|[\s\p{P}])" + Regex.Escape(token) + @"(?:$|[\s\p{P}])")));
    }

    private static bool BilingualAlias(string expected, string actual)
    {
        if (Regex.IsMatch(actual, @"伴奏|现场|翻自|翻唱|remix|cover|live|版", RegexOptions.IgnoreCase)) return false;
        var parts = Regex.Split(actual, @"\s*[-–—()（）]\s*").Where(part => part.Length > 0).ToArray();
        if (parts.Length != 2) return false;
        bool Native(string value) => ContainsKana(value) || LanguageSupport.HasKorean(value) || LanguageSupport.HasTibetan(value) || Regex.IsMatch(value, @"[\u3400-\u9fff]");
        if (Native(parts[0]) == Native(parts[1])) return false;
        var key = Normalize(LanguageSupport.FoldLatinAccents(expected));
        return parts.Any(part => Normalize(LanguageSupport.FoldLatinAccents(part)) == key);
    }

    public static double Score(string expectedTitle, string expectedArtist, double expectedSeconds,
        string actualTitle, string actualArtist, double actualSeconds)
    {
        var title = Normalize(CleanTitle(expectedTitle));
        var candidate = Normalize(CleanTitle(actualTitle));
        bool exactTitle = title == candidate;
        if (!exactTitle) { title = Normalize(LanguageSupport.FoldLatinAccents(title)); candidate = Normalize(LanguageSupport.FoldLatinAccents(candidate)); }
        bool bilingualAlias = title != candidate && expectedSeconds > 0 && actualSeconds > 0 && BilingualAlias(CleanTitle(expectedTitle), CleanTitle(actualTitle));
        if (title.Length == 0 || (title != candidate && !bilingualAlias) || Version(expectedTitle) != Version(actualTitle)) return -1;
        var artist = Normalize(CleanArtist(expectedArtist));
        var candidateArtist = Normalize(CleanArtist(actualArtist));
        artist = Normalize(LanguageSupport.FoldLatinAccents(artist)); candidateArtist = Normalize(LanguageSupport.FoldLatinAccents(candidateArtist));
        if (bilingualAlias && (artist.Length == 0 || artist != candidateArtist)) return -1;
        if (artist.Length > 0 && (candidateArtist.Length == 0 ||
            (artist != candidateArtist && !artist.Contains(candidateArtist) && !candidateArtist.Contains(artist)))) return -1;
        var difference = Math.Abs(expectedSeconds - actualSeconds);
        if (expectedSeconds > 0 && actualSeconds > 0 && difference > Math.Max(8, expectedSeconds * 0.035)) return -1;
        return 100 + (exactTitle ? 2 : 0) + (artist == candidateArtist ? 20 : 0) - (expectedSeconds > 0 && actualSeconds > 0 ? difference : 0);
    }
}
