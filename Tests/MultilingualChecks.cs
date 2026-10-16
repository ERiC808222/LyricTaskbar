using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopLyrics.Models;
using DesktopLyrics.Services;

public static class MultilingualChecks
{
    public static async Task Run(Action<bool, string> check, string scratch)
    {
        var samples = new[] { "봄빛이 창가에 머물러요", "བོད་སྐད་ཀྱི་གླུ་དབྱངས།", "La lumière entre dans la pièce", "La luz entra por la ventana" };
        foreach (var sample in samples)
        {
            var line = LrcParser.ParseBilingual("[00:01.125]" + sample, "[00:01.125]演示译文")[0];
            check(line.Text == sample && line.Translation == "演示译文", "LRC preserves multilingual original and paired translation: " + sample[..2]);
            var ttml = TtmlParser.Parse("<tt xmlns='http://www.w3.org/ns/ttml' xmlns:m='http://www.w3.org/ns/ttml#metadata'><body><div><p begin='1s' end='3s'>" + sample + "<span m:role='x-translation' xml:lang='zh-CN'>演示译文</span></p></div></body></tt>");
            check(ttml[0].Text == sample && ttml[0].Translation == "演示译文", "TTML preserves multilingual shaping characters and spaces: " + sample[..2]);
        }
        check(LyricMatcher.Score("Ça ira", "Élodie", 200, "Ca ira", "Elodie", 200) > 0, "Latin accent variants match without changing displayed text");
        check(LyricMatcher.Score("Canción", "José", 200, "Cancion", "Jose", 200) > 0, "Spanish diacritics match catalog variants");
        check(LanguageSupport.FoldLatinAccents("བོད་སྐད།") == "བོད་སྐད།" && LyricMatcher.Score("བོད", "ANU", 200, "བད", "ANU", 200) < 0, "Tibetan vowel marks cannot be discarded during Latin matching");
        check(LyricMatcher.Normalize("봄날") == LyricMatcher.Normalize("봄날"), "composed and decomposed Korean syllables match");
        check(LyricMatcher.Score("Fly", "ANU", 200, "Fly (Spanish)", "ANU", 200) < 0, "different language recordings remain rejected");
        check(LyricMatcher.Score("Fly", "ANU", 265, "FLY-飞", "ANU", 264.705) > 0, "bilingual song title alias requires matching artist and duration");
        check(LyricMatcher.Score("Fly", "ANU", 265, "FLY-飞", "Cover singer", 265) < 0 && LyricMatcher.Score("Fly", "ANU", 0, "FLY-飞", "ANU", 265) < 0, "bilingual alias cannot bypass singer or missing-duration checks");
        check(LyricMatcher.Score("Fly", "ANU", 265, "FLY (藏语版)", "ANU", 265) < 0 && LyricMatcher.Score("Fly", "ANU", 265, "FLY (正式版伴奏)", "ANU", 265) < 0, "version tags cannot masquerade as translated song title aliases");
        check(LanguageSupport.FontFamilyFor(samples[0]).StartsWith("Malgun") && LanguageSupport.FontFamilyFor(samples[1]).StartsWith("Microsoft Himalaya"), "Korean and Tibetan select script-specific fonts");
        check(LanguageSupport.CatalogRegions(LyricsLanguageMode.Korean, "Spring Day")[0] == "kr" && LanguageSupport.CatalogRegions(LyricsLanguageMode.French, "Song")[0] == "fr", "selected lyric language controls regional catalog lookup");
        using var http = new HttpClient(new RegionalHandler());
        var catalog = await new AppleLyricsSource(http).ResolveAsync("Spring Day", "BTS", TimeSpan.FromSeconds(274), default, LyricsLanguageMode.Korean);
        check(catalog?.LocalizedAliases?.Single().Title == "봄날" && catalog.LocalizedAliases[0].Country == "kr", "localized alias is accepted only for the same Apple song ID");
        var folder = Path.Combine(scratch, "multilingual-settings"); var settings = new SettingsService(folder);
        settings.Settings.LyricsLanguage = LyricsLanguageMode.Tibetan; settings.Settings.LyricTransition = LyricTransitionMode.RevealFromLeft; settings.Settings.EnableTrackTransition = false; settings.SaveSettings();
        var restored = new SettingsService(folder).Settings;
        check(restored.LyricsLanguage == LyricsLanguageMode.Tibetan && restored.LyricTransition == LyricTransitionMode.RevealFromLeft && !restored.EnableTrackTransition, "search language, reveal effect and track fade preferences persist");
        foreach (var language in new[] { AppLanguage.Korean, AppLanguage.Tibetan, AppLanguage.French, AppLanguage.Spanish })
        {
            I18n.CurrentLanguage = language;
            check(I18n.LanguageMenu != "Interface language" && I18n.TrackFade != "Fade cover and track details together" && I18n.SyncedLines(3).Contains('3'), "new UI locale and dynamic line count " + language);
        }
    }
    private sealed class RegionalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string json = request.RequestUri!.AbsolutePath.EndsWith("search")
                ? JsonSerializer.Serialize(new { results = new[] { new { trackId = 42, trackName = "Spring Day", artistName = "BTS", trackTimeMillis = 274000, collectionName = "Demo" } } })
                : JsonSerializer.Serialize(new { results = new[] { new { trackId = 43, trackName = "Wrong ID", artistName = "BTS" }, new { trackId = 42, trackName = "봄날", artistName = "BTS" } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8) });
        }
    }
}
