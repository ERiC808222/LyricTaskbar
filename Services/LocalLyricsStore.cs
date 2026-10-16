using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public sealed class LocalLyricsStore
{
    private readonly string _folder;
    public LocalLyricsStore(string? folder = null) => _folder = folder ?? Path.Combine(SettingsService.DataFolder, "lyrics");

    public static string TrackKey(string title, string artist, TimeSpan duration) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{title.Normalize(NormalizationForm.FormKC)}\n{artist.Normalize(NormalizationForm.FormKC)}\n{Math.Round(duration.TotalSeconds)}")));

    public List<LrcLine>? Load(string title, string artist, TimeSpan duration)
    {
        try
        {
            var path = Path.Combine(_folder, TrackKey(title, artist, duration) + ".json");
            if (!File.Exists(path)) return null;
            var lines = JsonSerializer.Deserialize<List<LrcLine>>(File.ReadAllText(path, Encoding.UTF8));
            return lines?.OrderBy(line => line.StartTime).ToList();
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Save(string title, string artist, TimeSpan duration, List<LrcLine> lines)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, TrackKey(title, artist, duration) + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(lines, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public void Remove(string title, string artist, TimeSpan duration)
    {
        var path = Path.Combine(_folder, TrackKey(title, artist, duration) + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static string ReadLrcFile(string path)
    {
        // Never replace Japanese characters silently when a file uses Shift-JIS.
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'); }
        catch (DecoderFallbackException)
        {
            if (bytes.Length > 1 && bytes[0] == 0xff && bytes[1] == 0xfe)
                return Encoding.Unicode.GetString(bytes).TrimStart('\ufeff');
            if (bytes.Length > 1 && bytes[0] == 0xfe && bytes[1] == 0xff)
                return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\ufeff');
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
    }
}
