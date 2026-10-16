using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DesktopLyrics.Models;

namespace DesktopLyrics.Services;

public static class TtmlParser
{
    private static string Attribute(XElement element, string name) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value ?? "";
    private static bool Chinese(string language) => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("cmn", StringComparison.OrdinalIgnoreCase);
    private static string Content(XElement element)
    {
        var result = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Value) || !text.Value.Contains('\n')) result.Append(text.Value);
            }
            else if (node is XElement child)
            {
                var role = Attribute(child, "role");
                if (role is "x-translation" or "x-roman" or "x-bg") continue;
                result.Append(Content(child));
            }
        }
        return result.ToString();
    }
    public static TimeSpan? ParseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.EndsWith("ms", StringComparison.Ordinal) && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) return TimeSpan.FromMilliseconds(ms);
        if (value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return TimeSpan.FromSeconds(seconds);
        var parts = value.Split(':');
        if (parts.Length is 2 or 3 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            double sum = 0;
            foreach (var part in parts) sum = sum * 60 + double.Parse(part, CultureInfo.InvariantCulture);
            return TimeSpan.FromSeconds(sum);
        }
        return null;
    }

    public static List<LrcLine> Parse(string text)
    {
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 3_000_000 });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name.LocalName != "tt") throw new FormatException("Not a TTML lyric document.");
        var body = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "body");
        if (body == null) return new();
        var headerTranslations = new Dictionary<string, string>();
        foreach (var block in document.Root.Descendants().Where(element => element.Name.LocalName == "translation" && Chinese(Attribute(element, "lang"))))
            foreach (var line in block.Elements().Where(element => element.Name.LocalName == "text"))
                if (Attribute(line, "for") is { Length: > 0 } key) headerTranslations[key] = Content(line).Trim();

        var timed = new List<(LrcLine Line, TimeSpan? End)>();
        foreach (var paragraph in body.Descendants().Where(element => element.Name.LocalName == "p"))
        {
            var begin = ParseTime(Attribute(paragraph, "begin"));
            var end = ParseTime(Attribute(paragraph, "end"));
            if (begin == null || begin < TimeSpan.Zero) continue;
            var original = Content(paragraph).Trim();
            if (original.Length == 0) continue;
            var translated = paragraph.Descendants().Where(element => Attribute(element, "role") == "x-translation" && Chinese(Attribute(element, "lang")))
                .Where(element => !element.Ancestors().Any(parent => Attribute(parent, "role") == "x-bg"))
                .Select(element => Content(element).Trim()).Where(value => value.Length > 0).FirstOrDefault() ?? "";
            if (headerTranslations.TryGetValue(Attribute(paragraph, "key"), out var header)) translated = header;
            timed.Add((new LrcLine { StartTime = begin.Value, Text = original, Translation = translated }, end));
        }
        var groups = timed.GroupBy(pair => pair.Line.StartTime).OrderBy(group => group.Key).Select(group =>
            (Line: new LrcLine { StartTime = group.Key, Text = string.Join(" / ", group.Select(pair => pair.Line.Text).Distinct()),
                Translation = string.Join(" / ", group.Select(pair => pair.Line.Translation).Where(value => value.Length > 0).Distinct()) },
             End: group.Where(pair => pair.End != null).Select(pair => pair.End).Max())).ToList();
        var lines = new List<LrcLine>();
        for (var index = 0; index < groups.Count; index++)
        {
            var pair = groups[index]; lines.Add(pair.Line);
            if (pair.End != null && pair.End > pair.Line.StartTime &&
                (index + 1 == groups.Count || pair.End.Value.Add(TimeSpan.FromMilliseconds(250)) < groups[index + 1].Line.StartTime))
                lines.Add(new LrcLine { StartTime = pair.End.Value });
        }
        return lines;
    }
}
