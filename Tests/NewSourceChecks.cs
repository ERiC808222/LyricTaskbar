using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopLyrics.Models;
using DesktopLyrics.Services;

public static class NewSourceChecks
{
    private const string HeaderTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xmlns:i="http://music.apple.com/lyric-ttml-internal" xmlns:ttm="http://www.w3.org/ns/ttml#metadata">
      <head><metadata><i:iTunesMetadata><i:translations>
        <i:translation xml:lang="en-US"><i:text for="L1">English must not be shown as Chinese</i:text></i:translation>
        <i:translation xml:lang="zh-Hans-CN"><i:text for="L1">窗边的光</i:text></i:translation>
      </i:translations></i:iTunesMetadata></metadata></head>
      <body><div><p begin="00:10.000" end="00:12.000" i:key="L1"><span>窓の</span><span>光</span><span ttm:role="x-bg">背景声</span><span ttm:role="x-roman">mado</span></p>
      <p begin="15s" end="17s"><span>You </span><span>and </span><span>me</span></p></div></body>
    </tt>
    """;
    private const string InlineTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><body><div>
    <p begin="1s" end="3s">雨の音<span ttm:role="x-translation" xml:lang="en">Rain</span><span ttm:role="x-translation" xml:lang="zh-CN">雨声</span><span ttm:role="x-roman">ame</span></p>
    </div></body></tt>
    """;
    public static async Task Run(Action<bool, string> check, string scratch)
    {
        var header = TtmlParser.Parse(HeaderTtml);
        check(header[0].Text == "窓の光" && header[0].Translation == "窗边的光", "Apple TTML Chinese translation linked by line key; romanization and background excluded");
        check(header[1].Text == "" && header[1].StartTime == TimeSpan.FromSeconds(12) && header[2].Text == "You and me", "TTML instrumental gaps and English word spacing preserved");
        var inline = TtmlParser.Parse(InlineTtml);
        check(inline[0].Text == "雨の音" && inline[0].Translation == "雨声", "AMLL inline Chinese translations parsed without English substitution");
        var englishOnly = TtmlParser.Parse(HeaderTtml.Replace("zh-Hans-CN", "fr-FR"));
        check(englishOnly[0].Translation == "", "non-Chinese translation remains unavailable in Chinese mode");
        check(TtmlParser.ParseTime("01:02:03.125") == TimeSpan.FromMilliseconds(3723125) && TtmlParser.ParseTime("1234ms") == TimeSpan.FromMilliseconds(1234), "TTML hour and millisecond timestamps");
        try { TtmlParser.Parse("<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///test'>]><tt>&x;</tt>"); check(false, "external XML entity rejected"); }
        catch (System.Xml.XmlException) { check(true, "external XML entity rejected"); }
        check(LyricMatcher.CleanArtist("Fujii Kaze — HELP EVER HURT NEVER") == "Fujii Kaze", "Apple Music SMTC album suffix removed before artist matching");
        using var http = new HttpClient(new SourcesHandler(HeaderTtml, InlineTtml));
        var apple = new AppleLyricsSource(http);
        var track = await apple.ResolveAsync("Shinunoga E-Wa", "Fujii Kaze — HELP EVER HURT NEVER", TimeSpan.FromSeconds(185), default);
        check(track?.Id == 2 && track.JapaneseTitle == "死ぬのがいいわ", "Apple catalog candidate checked and Japanese alias shares exact song ID");
        var appleLines = await apple.FetchAsync(track!, default);
        check(appleLines?[0].SourceName.Contains("Paxsenix") == true && appleLines[0].Translation == "窗边的光", "Apple source provenance retained");
        var amll = new AmllLyricsSource(http, Path.Combine(scratch, "amll-index"));
        var amllLines = await amll.FetchAsync("Shinunoga E-Wa", "Fujii Kaze", TimeSpan.FromSeconds(185), track, default);
        check(amllLines?[0].SourceName == "AMLL 社区歌词库" && amllLines[0].Translation == "雨声", "AMLL match by Apple ID and Chinese provenance retained");
        var unrelated = await amll.FetchAsync("Unknown", "Other singer", TimeSpan.FromSeconds(185), null, default);
        check(unrelated == null, "AMLL missing recording does not return unrelated lyrics");
    }
    private sealed class SourcesHandler(string headerTtml, string inlineTtml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString(); string body;
            if (url.Contains("itunes.apple.com/search")) body = JsonSerializer.Serialize(new {results = new[]{
                new{trackId=1,trackName="Shinunoga E-Wa",artistName="Cover Singer",trackTimeMillis=185000,collectionName="Other"},
                new{trackId=2,trackName="Shinunoga E-Wa",artistName="Fujii Kaze",trackTimeMillis=185000,collectionName="Album"}}});
            else if (url.Contains("itunes.apple.com/lookup")) body=JsonSerializer.Serialize(new{results=new[]{new{trackId=2,trackName="死ぬのがいいわ",artistName="藤井 風"}}});
            else if (url.Contains("lyrics.paxsenix.org")) body=JsonSerializer.Serialize(new{ttmlContent=headerTtml});
            else if (url.EndsWith("raw-lyrics-index.jsonl")) body=JsonSerializer.Serialize(new{metadata=new object[]{new object[]{"appleMusicId",new[]{"2"}},new object[]{"musicName",new[]{"別名"}},new object[]{"artists",new[]{"歌手"}}},rawLyricFile="1234567890-1234-abcd1234.ttml"});
            else body=inlineTtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8)});
        }
    }
}
