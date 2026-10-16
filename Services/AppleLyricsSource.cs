using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public sealed record LocalizedTrackAlias(string Country, string Title, string Artist);
public sealed record AppleCatalogTrack(long Id, string Title, string Artist, string Album, double Seconds, string JapaneseTitle, string JapaneseArtist, IReadOnlyList<LocalizedTrackAlias>? LocalizedAliases = null);

public sealed class AppleLyricsSource
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, AppleCatalogTrack> _catalog = new();
    public string LastDiagnostic { get; private set; } = "";
    public AppleLyricsSource(HttpClient? http = null) => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
    public async Task<AppleCatalogTrack?> ResolveAsync(string title, string artist, TimeSpan duration, CancellationToken ct, LyricsLanguageMode language = LyricsLanguageMode.Auto)
    {
        artist = LyricMatcher.CleanArtist(artist);
        var key = language + ":" + LocalLyricsStore.TrackKey(title, artist, duration);
        if (_catalog.TryGetValue(key, out var cached)) { LastDiagnostic = $"catalog cached id={cached.Id}"; return cached; }
        JsonElement? best = null; double score = -1;
        foreach (var country in LanguageSupport.CatalogRegions(language, title + " " + artist))
        {
            using var response = await _http.GetAsync($"https://itunes.apple.com/search?term={Uri.EscapeDataString(title + " " + artist)}&entity=song&limit=12&country={country}", ct);
            LastDiagnostic = $"catalog {country} HTTP {(int)response.StatusCode}; expected seconds={duration.TotalSeconds}";
            if (!response.IsSuccessStatusCode) continue;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("results", out var results)) continue;
            foreach (var song in results.EnumerateArray())
            {
                var current = LyricMatcher.Score(title, artist, duration.TotalSeconds, song.GetProperty("trackName").GetString() ?? "", song.GetProperty("artistName").GetString() ?? "", song.GetProperty("trackTimeMillis").GetDouble()/1000);
                if (current > score) { score = current; best = song.Clone(); }
            }
            LastDiagnostic += $"; candidates={results.GetArrayLength()}; best score={score}";
            if (best != null) break;
        }
        if (best == null) return null;
        var selected = best.Value;
        var id = selected.GetProperty("trackId").GetInt64();
        LastDiagnostic += $"; selected id={id}";
        var canonicalTitle = selected.GetProperty("trackName").GetString() ?? "";
        var canonicalArtist = selected.GetProperty("artistName").GetString() ?? "";
        string japaneseTitle = canonicalTitle, japaneseArtist = canonicalArtist;
        var aliases = new List<LocalizedTrackAlias>();
        foreach (var country in LanguageSupport.AliasRegions(language, title + " " + artist))
        {
            try
            {
                string locale = country switch { "jp" => "ja_jp", "kr" => "ko_kr", "fr" => "fr_fr", "es" => "es_es", _ => "zh_cn" };
                using var localizedResponse = await _http.GetAsync($"https://itunes.apple.com/lookup?id={id}&entity=song&country={country}&lang={locale}", ct);
                if (!localizedResponse.IsSuccessStatusCode) continue;
                using var localized = JsonDocument.Parse(await localizedResponse.Content.ReadAsStringAsync(ct));
                foreach (var song in localized.RootElement.GetProperty("results").EnumerateArray())
                    if (song.TryGetProperty("trackId", out var trackId) && trackId.GetInt64() == id)
                    {
                        string localTitle = song.GetProperty("trackName").GetString() ?? canonicalTitle;
                        string localArtist = song.GetProperty("artistName").GetString() ?? canonicalArtist;
                        aliases.Add(new(country, localTitle, localArtist));
                        if (country == "jp") { japaneseTitle = localTitle; japaneseArtist = localArtist; }
                        break;
                    }
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        var track = new AppleCatalogTrack(id, canonicalTitle, canonicalArtist, selected.GetProperty("collectionName").GetString() ?? "",
            selected.GetProperty("trackTimeMillis").GetDouble()/1000, japaneseTitle, japaneseArtist, aliases);
        _catalog[key] = track;
        return track;
    }
    public async Task<List<LrcLine>?> FetchAsync(AppleCatalogTrack track, CancellationToken ct)
    {
        // The transport is a public third-party relay, not a supported Apple public lyric API.
        using var response = await _http.GetAsync($"https://lyrics.paxsenix.org/apple-music/lyrics?id={track.Id}", ct);
        LastDiagnostic += $"; lyrics HTTP {(int)response.StatusCode}";
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty("ttmlContent", out var ttml) || string.IsNullOrWhiteSpace(ttml.GetString())) return null;
        var lines = TtmlParser.Parse(ttml.GetString()!);
        foreach (var line in lines) { line.SourceName = "Apple Music（Paxsenix 第三方接口）"; line.SourceUrl = $"https://music.apple.com/us/song/{track.Id}"; }
        return lines;
    }
}
