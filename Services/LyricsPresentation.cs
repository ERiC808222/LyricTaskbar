using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public static class LyricsPresentation
{
    public static string Secondary(IReadOnlyList<LrcLine> lyrics, int index, bool showTranslation)
    {
        if (index < 0 || index >= lyrics.Count || string.IsNullOrWhiteSpace(lyrics[index].Text)) return string.Empty;
        if (showTranslation)
            return string.IsNullOrWhiteSpace(lyrics[index].Translation) ? I18n.NoTranslation : lyrics[index].Translation;
        return index + 1 < lyrics.Count ? lyrics[index + 1].Text : string.Empty;
    }
}
