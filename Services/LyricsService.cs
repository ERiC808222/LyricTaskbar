using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

// The upstream providers are retained; Japanese spelling and bilingual responses are preserved.
public class LyricsService
{
    private readonly HttpClient _http;
    private readonly AppleLyricsSource _apple;
    private readonly AmllLyricsSource _amll;
    public LyricsSourceMode SourceMode { get; set; }
    public LyricsLanguageMode SearchLanguage { get; set; } = LyricsLanguageMode.Auto;
    public string LastDiagnostic => _apple.LastDiagnostic;
    private readonly LocalLyricsStore _local;
    private readonly ConcurrentDictionary<string, List<LrcLine>> _cache = new();
    public LyricsService(HttpClient? http = null, LocalLyricsStore? local = null, LyricsSourceMode mode = LyricsSourceMode.Auto)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LyricBarJP/1.2 (personal desktop lyrics)");
        _local = local ?? new LocalLyricsStore();
        _apple = new AppleLyricsSource(http);
        _amll = new AmllLyricsSource(http);
        SourceMode = mode;
    }
    public void ClearCache() => _cache.Clear();
    public record SearchPair(string Title, string Artist);
    public static List<SearchPair> GenerateSearchCandidates(string title, string artist)
    {
        // FormKC handles full-width Latin and half-width kana without converting kanji to Chinese.
        var pairs = new List<SearchPair> { new(title.Normalize(NormalizationForm.FormKC).Trim(), artist.Normalize(NormalizationForm.FormKC).Trim()) };
        var cleaned = new SearchPair(LyricMatcher.CleanTitle(title), LyricMatcher.CleanArtist(artist));
        if (!pairs.Contains(cleaned)) pairs.Add(cleaned);
        return pairs;
    }
    public async Task<List<LrcLine>?> FetchLyricsAsync(string title, string artist, TimeSpan duration, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var mode = SourceMode;
        var key = $"{mode}:{SearchLanguage}:" + LocalLyricsStore.TrackKey(title, artist, duration);
        var local = _local.Load(title, artist, duration);
        if (local?.Count > 0) return Mark(local, "本地导入", "");
        if (_cache.TryGetValue(key, out var cached)) return cached;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        async Task<T?> Safe<T>(Func<Task<T?>> fetch) where T : class
        {
            try { return await fetch(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or System.Xml.XmlException) { return null; }
        }
        List<LrcLine>? result = null;
        if (mode == LyricsSourceMode.Original)
            result = await Safe(() => FetchOriginalAsync(title, LyricMatcher.CleanArtist(artist), duration, budget.Token));
        else
        {
            var apple = await Safe(() => _apple.ResolveAsync(title, artist, duration, budget.Token, SearchLanguage));
            if (mode == LyricsSourceMode.AppleMusic)
                result = apple == null ? null : await Safe(() => _apple.FetchAsync(apple, budget.Token));
            else if (mode == LyricsSourceMode.Amll)
                result = await Safe(() => _amll.FetchAsync(title, artist, duration, apple, budget.Token));
            else
            {
                var amll = await Safe(() => _amll.FetchAsync(title, artist, duration, apple, budget.Token));
                if (amll?.Any(line => line.Translation.Length > 0) == true) result = amll;
                else
                {
                    var official = apple == null ? null : await Safe(() => _apple.FetchAsync(apple, budget.Token));
                    if (official?.Any(line => line.Translation.Length > 0) == true) result = official;
                    else
                    {
                        // Bilingual results are preferred in automatic mode. Resolve the Japanese
                        // metadata using the same Apple ID instead of guessing a romanized alias.
                        List<LrcLine>? original = null;
                        var aliases = apple?.LocalizedAliases?.Select(alias => new SearchPair(alias.Title, alias.Artist)).ToList() ?? new();
                        if (apple != null && aliases.Count == 0) aliases.Add(new(apple.JapaneseTitle, apple.JapaneseArtist));
                        aliases.Add(new(title, LyricMatcher.CleanArtist(artist)));
                        foreach (var pair in aliases.Distinct())
                        {
                            if (budget.IsCancellationRequested) break;
                            var candidate = await Safe(() => FetchOriginalAsync(pair.Title, pair.Artist, duration, budget.Token));
                            if (candidate?.Any(line => line.Translation.Length > 0) == true) { original = candidate; break; }
                            original ??= candidate;
                        }
                        result = original?.Any(line => line.Translation.Length > 0) == true ? original : official ?? amll ?? original;
                    }
                }
            }
        }
        ct.ThrowIfCancellationRequested();
        if (result?.Any(line => line.Text.Length > 0) == true) _cache[key] = result;
        return result;
    }
    private static List<LrcLine> Mark(List<LrcLine> lines, string source, string url)
    {
        foreach (var line in lines) { line.SourceName = source; line.SourceUrl = url; }
        return lines;
    }

    private async Task<List<LrcLine>?> FetchOriginalAsync(string title, string artist, TimeSpan duration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        ct.ThrowIfCancellationRequested();
        var local = _local.Load(title, artist, duration);
        if (local?.Count > 0) return local;
        var key = LocalLyricsStore.TrackKey(title, artist, duration);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(25));
        List<LrcLine>? fallback = null;
        async Task<List<LrcLine>?> Safe(Func<Task<List<LrcLine>?>> fetch)
        {
            try { return await fetch(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException) { return null; }
        }
        foreach (var pair in GenerateSearchCandidates(title, artist))
        {
            ct.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested) break;
            var results = await Task.WhenAll(Safe(() => NetEase(pair, duration, budget.Token)), Safe(() => QQ(pair, duration, budget.Token)), Safe(() => LrcLib(pair, duration, budget.Token)));
            var best = results.Where(lines => lines?.Any(line => line.Text.Length > 0) == true)
                .OrderByDescending(lines => lines!.Count(line => line.Translation.Length > 0)).FirstOrDefault();
            if (best != null)
            {
                if (best.Any(line => line.Translation.Length > 0)) { _cache[key] = best; return best; }
                fallback ??= best;
            }
            if (fallback == null && !budget.IsCancellationRequested)
            {
                var originals = await Task.WhenAll(Safe(() => Kugou(pair, duration, budget.Token)));
                fallback = originals.FirstOrDefault(lines => lines?.Any(line => line.Text.Length > 0) == true);
            }
        }
        ct.ThrowIfCancellationRequested();
        if (fallback != null) _cache[key] = fallback;
        return fallback;
    }

    private static string Text(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
    private static double Number(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private static string Artists(JsonElement song, string key) => song.TryGetProperty(key, out var artists) && artists.ValueKind == JsonValueKind.Array
        ? string.Join(" ", artists.EnumerateArray().Select(artist => Text(artist, "name"))) : "";
    private static string Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return WebUtility.HtmlDecode(Encoding.UTF8.GetString(Convert.FromBase64String(text)));
    }
    private async Task<JsonDocument?> Get(string url, CancellationToken ct, bool qq = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (qq) request.Headers.Referrer = new Uri("https://y.qq.com/");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        // Keep the existing QQ JSONP wrapper compatible without executing scripts.
        var body = await response.Content.ReadAsStringAsync(ct);
        if (qq && !body.TrimStart().StartsWith('{'))
        {
            var first = body.IndexOf('{'); var last = body.LastIndexOf('}');
            if (first >= 0 && last >= first) body = body[first..(last + 1)];
        }
        return JsonDocument.Parse(body);
    }
    private static JsonElement? Select(JsonElement array, SearchPair pair, TimeSpan duration,
        Func<JsonElement, string> title, Func<JsonElement, string> artist, Func<JsonElement, double> seconds)
    {
        JsonElement? best = null; double bestScore = -1;
        foreach (var song in array.EnumerateArray())
        {
            var score = LyricMatcher.Score(pair.Title, pair.Artist, duration.TotalSeconds, title(song), artist(song), seconds(song));
            if (score > bestScore) { bestScore = score; best = song; }
        }
        return best;
    }
    private static string Keyword(SearchPair pair) => Uri.EscapeDataString($"{pair.Title} {pair.Artist}".Trim());

    private async Task<List<LrcLine>?> NetEase(SearchPair pair, TimeSpan duration, CancellationToken ct)
    {
        using var search = await Get($"https://music.163.com/api/search/get/web?s={Keyword(pair)}&type=1&offset=0&total=true&limit=12", ct);
        if (search == null || !search.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs)) return null;
        var song = Select(songs, pair, duration, song => Text(song, "name"), song => Artists(song, "artists"), song => Number(song, "duration") / 1000);
        if (song == null) return null;
        var id = song.Value.GetProperty("id").GetInt64();
        using var response = await Get($"https://music.163.com/api/song/lyric?os=pc&id={id}&lv=-1&kv=-1&tv=-1", ct);
        if (response == null || !response.RootElement.TryGetProperty("lrc", out var original)) return null;
        var translation = response.RootElement.TryGetProperty("tlyric", out var translated) ? Text(translated, "lyric") : "";
        return Mark(LrcParser.ParseBilingual(Text(original, "lyric"), translation), "网易云音乐", $"https://music.163.com/song?id={id}");
    }
    private async Task<List<LrcLine>?> QQ(SearchPair pair, TimeSpan duration, CancellationToken ct)
    {
        using var search = await Get($"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=12&w={Keyword(pair)}&format=json", ct, true);
        if (search == null || !search.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("song", out var info) || !info.TryGetProperty("list", out var songs)) return null;
        var song = Select(songs, pair, duration, song => Text(song, "songname"), song => Artists(song, "singer"), song => Number(song, "interval"));
        if (song == null) return null;
        var mid = Uri.EscapeDataString(Text(song.Value, "songmid"));
        using var response = await Get($"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mid}&format=json&nobase64=0", ct, true);
        if (response == null) return null;
        var original = Decode(Text(response.RootElement, "lyric"));
        string translation = "";
        try { translation = Decode(Text(response.RootElement, "trans")); } catch (FormatException) { }
        return Mark(LrcParser.ParseBilingual(original, translation), "QQ 音乐", $"https://y.qq.com/n/ryqq/songDetail/{mid}");
    }
    private async Task<List<LrcLine>?> LrcLib(SearchPair pair, TimeSpan duration, CancellationToken ct)
    {
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(pair.Title)}&artist_name={Uri.EscapeDataString(pair.Artist)}";
        if (duration > TimeSpan.Zero) url += $"&duration={(int)duration.TotalSeconds}";
        using var exact = await Get(url, ct);
        if (exact != null && LyricMatcher.Score(pair.Title, pair.Artist, duration.TotalSeconds, Text(exact.RootElement,"trackName"), Text(exact.RootElement,"artistName"), Number(exact.RootElement,"duration")) >= 0)
        {
            var lines = LrcParser.ParseBilingual(Text(exact.RootElement, "syncedLyrics"));
            if (lines.Count > 0) return Mark(lines, "LRCLIB", "https://lrclib.net/");
        }
        using var search = await Get($"https://lrclib.net/api/search?q={Keyword(pair)}", ct);
        if (search == null || search.RootElement.ValueKind != JsonValueKind.Array) return null;
        var synced = search.RootElement.EnumerateArray().Where(song => !string.IsNullOrWhiteSpace(Text(song, "syncedLyrics"))).ToArray();
        var best = synced.OrderByDescending(song => LyricMatcher.Score(pair.Title, pair.Artist, duration.TotalSeconds, Text(song,"trackName"), Text(song,"artistName"), Number(song,"duration"))).FirstOrDefault();
        if (best.ValueKind == JsonValueKind.Undefined || LyricMatcher.Score(pair.Title,pair.Artist,duration.TotalSeconds,Text(best,"trackName"),Text(best,"artistName"),Number(best,"duration")) < 0) return null;
        return Mark(LrcParser.ParseBilingual(Text(best, "syncedLyrics")), "LRCLIB", "https://lrclib.net/");
    }
    private async Task<List<LrcLine>?> Kugou(SearchPair pair, TimeSpan duration, CancellationToken ct)
    {
        using var search = await Get($"https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={Keyword(pair)}&page=1&pagesize=12", ct);
        if (search == null || !search.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("info", out var songs)) return null;
        var song = Select(songs, pair, duration, song => Text(song, "songname"), song => Text(song, "singername"), song => Number(song, "duration"));
        if (song == null) return null;
        var hash = Uri.EscapeDataString(Text(song.Value, "hash"));
        using var lyrics = await Get($"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={Keyword(pair)}&duration={(long)duration.TotalMilliseconds}&hash={hash}", ct);
        if (lyrics == null || !lyrics.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) return null;
        var candidate = candidates[0];
        var id = Uri.EscapeDataString(candidate.GetProperty("id").ToString());
        var key = Uri.EscapeDataString(Text(candidate, "accesskey"));
        using var response = await Get($"https://krcs.kugou.com/download?ver=1&client=mobi&id={id}&accesskey={key}&fmt=lrc&charset=utf8", ct);
        return response == null ? null : Mark(LrcParser.ParseBilingual(Decode(Text(response.RootElement, "content"))), "酷狗音乐", "https://www.kugou.com/");
    }
}
