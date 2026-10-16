using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public sealed class AmllLyricsSource
{
    private const string Root = "https://raw.githubusercontent.com/amll-dev/amll-ttml-db/refs/heads/main/";
    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    public AmllLyricsSource(HttpClient? http = null, string? folder = null)
    { _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) }; _folder = folder ?? Path.Combine(SettingsService.DataFolder, "amll-index"); }

    private async Task<string?> Index(CancellationToken ct)
    {
        var path = Path.Combine(_folder, "raw-lyrics-index.jsonl");
        await _indexGate.WaitAsync(ct);
        try
        {
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(1)) return await File.ReadAllTextAsync(path, ct);
            try
            {
                using var response = await _http.GetAsync(Root + "metadata/raw-lyrics-index.jsonl", ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    Directory.CreateDirectory(_folder);
                    await File.WriteAllTextAsync(path + ".tmp", content, ct);
                    File.Move(path + ".tmp", path, true);
                    return content;
                }
            }
            catch (HttpRequestException) { }
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
        }
        finally { _indexGate.Release(); }
    }

    public async Task<List<LrcLine>?> FetchAsync(string title, string artist, TimeSpan duration, AppleCatalogTrack? apple, CancellationToken ct)
    {
        var index = await Index(ct); if (index == null) return null;
        var candidates = new List<(string File, int Score)>();
        foreach (var line in index.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var record = JsonDocument.Parse(line);
            if (!record.RootElement.TryGetProperty("metadata", out var entries) || !record.RootElement.TryGetProperty("rawLyricFile", out var file)) continue;
            var meta = entries.EnumerateArray().ToDictionary(item => item[0].GetString() ?? "", item => item[1].EnumerateArray().Select(value => value.GetString() ?? "").ToArray());
            var exactId = apple != null && meta.GetValueOrDefault("appleMusicId", Array.Empty<string>()).Contains(apple.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var titles = meta.GetValueOrDefault("musicName", Array.Empty<string>());
            var artists = meta.GetValueOrDefault("artists", Array.Empty<string>());
            var names = new[] { title, apple?.JapaneseTitle ?? title }.Concat(apple?.LocalizedAliases?.Select(alias => alias.Title) ?? Array.Empty<string>()).Select(LyricMatcher.Normalize).ToArray();
            var artistNames = new[] { LyricMatcher.CleanArtist(artist), apple?.JapaneseArtist ?? artist }.Concat(apple?.LocalizedAliases?.Select(alias => alias.Artist) ?? Array.Empty<string>()).Select(LyricMatcher.Normalize).ToArray();
            var nameMatch = titles.Any(name => names.Contains(LyricMatcher.Normalize(name))) && artists.Any(name => artistNames.Contains(LyricMatcher.Normalize(name)));
            var filename = file.GetString() ?? "";
            if ((exactId || nameMatch) && Regex.IsMatch(filename, @"^\d+-\d+-[a-fA-F0-9]+\.ttml$")) candidates.Add((filename, exactId ? 2 : 1));
        }
        List<LrcLine>? best = null; int bestScore = -1;
        foreach (var candidate in candidates.OrderByDescending(item => item.Score).ThenByDescending(item => item.File).Take(3))
        {
            ct.ThrowIfCancellationRequested();
            using var response = await _http.GetAsync(Root + "raw-lyrics/" + candidate.File, ct);
            if (!response.IsSuccessStatusCode) continue;
            var lines = TtmlParser.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!lines.Any(line => line.Text.Length > 0)) continue;
            if (duration > TimeSpan.Zero && lines.Max(line => line.StartTime) > duration.Add(TimeSpan.FromSeconds(8))) continue;
            var score = lines.Count(line => line.Translation.Length > 0);
            if (score <= bestScore) continue;
            foreach (var line in lines) { line.SourceName = "AMLL 社区歌词库"; line.SourceUrl = "https://github.com/amll-dev/amll-ttml-db/blob/main/raw-lyrics/" + candidate.File; }
            best = lines; bestScore = score;
            if (score > 0 && candidate.Score == 2) break;
        }
        return best;
    }
}
