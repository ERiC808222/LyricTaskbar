using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopLyrics.Models;
using DesktopLyrics.Services;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
var scratch = Path.Combine(Path.GetTempPath(), "LyricBar-tests-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);

var lines = LrcParser.ParseBilingual("[offset:+100]\n[00:01.050]窓の光\n[00:02.000]\n[00:03.000]雨の音", "[offset:+100]\n[00:01.070]窗边的光\n[00:09.000]不要错配");
Check(lines[0].StartTime == TimeSpan.FromMilliseconds(1150) && lines[0].Translation == "窗边的光", "millisecond timestamps and offset alignment");
Check(lines[1].Text == "" && lines[1].Translation == "" && lines[2].Translation == "", "instrumental gaps and distant translations remain empty");
var bilingual = LrcParser.ParseBilingual("[00:01.00]雨の音\n[00:01.00]雨声\n[00:02.00]風の音\n[00:02.00]风声");
Check(bilingual.Count == 2 && bilingual[0].Translation == "雨声", "same timestamp bilingual LRC becomes one paired line");
var close = LrcParser.ParseBilingual("[00:10.000]一番\n[00:10.300]二番", "[00:10.300]第二句");
Check(close[0].Translation == "" && close[1].Translation == "第二句", "adjacent lines do not steal an exact translation");
Check(LrcParser.FindActiveIndex(bilingual, TimeSpan.FromMilliseconds(999)) == -1 && LrcParser.FindActiveIndex(bilingual, TimeSpan.FromSeconds(2)) == 1, "active line boundaries");
I18n.CurrentLanguage = AppLanguage.Chinese;
Check(LyricsPresentation.Secondary(bilingual, 0, true) == "雨声" && LyricsPresentation.Secondary(bilingual, 0, false) == "風の音", "translation and next-line modes are distinct");
Check(LyricsPresentation.Secondary(lines, 2, true) == I18n.NoTranslation && LyricsPresentation.Secondary(lines, 1, true) == "", "missing translations are labeled and gaps remain blank");
Check(LyricMatcher.Normalize("ｶﾞﾗｽ") == LyricMatcher.Normalize("ガラス"), "half-width Japanese kana and dakuten normalization");
Check(LyricsService.GenerateSearchCandidates("夏の風【歌詞付き】", "歌手 - Topic").Last().Title == "夏の風", "presentation labels removed without deleting Japanese titles");
Check(LyricsService.GenerateSearchCandidates("誰かの聲（日本語版）", "歌手").All(pair => pair.Title.Contains("聲") && pair.Title.Contains("日本語版")), "kanji spelling and Japanese version labels preserved");
Check(LyricMatcher.Score("夏の風", "歌手", 240, "夏の風", "歌手", 242) > 0 && LyricMatcher.Score("夏の風", "歌手", 240, "夏の風", "別の人", 240) < 0, "artist identity required before duration ranking");
Check(LyricMatcher.Score("夏の風", "歌手", 240, "夏の風 (Live)", "歌手", 240) < 0 && LyricMatcher.Score("夏の風", "歌手", 240, "夏の風", "歌手", 30) < 0, "live versions and short excerpts rejected");
var store = new LocalLyricsStore(Path.Combine(scratch, "lyrics"));
store.Save("夏の風", "歌手", TimeSpan.FromSeconds(240), bilingual);
Check(store.Load("夏の風", "歌手", TimeSpan.FromSeconds(240))?[0].Translation == "雨声" && store.Load("夏の風", "歌手", TimeSpan.FromSeconds(300)) == null, "local bilingual overrides persist per recording duration");
var sjis = Path.Combine(scratch, "shiftjis.lrc");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
File.WriteAllBytes(sjis, Encoding.GetEncoding(932).GetBytes("[00:01.00]雨の音"));
Check(LocalLyricsStore.ReadLrcFile(sjis).Contains("雨の音"), "Shift-JIS LRC import preserves Japanese");

var fake = new FakeHandler(qqTranslation: false);
var fetched = await new LyricsService(new HttpClient(fake), new LocalLyricsStore(Path.Combine(scratch, "network")), LyricsSourceMode.Original).FetchLyricsAsync("夏の風", "歌手", TimeSpan.FromSeconds(240));
Check(fetched?[0].Translation == "窗边的光" && fake.SelectedId == "2", "NetEase translated lyrics and correct candidate selection");
var qq = new FakeHandler(qqTranslation: true);
var qqLines = await new LyricsService(new HttpClient(qq), new LocalLyricsStore(Path.Combine(scratch, "qq")), LyricsSourceMode.Original).FetchLyricsAsync("夏の風", "歌手", TimeSpan.FromSeconds(240));
Check(qqLines?[0].Translation == "来自 QQ 的译文", "QQ bilingual source wins over original-only NetEase");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await new LyricsService().FetchLyricsAsync("夏の風", "歌手", TimeSpan.FromSeconds(240), cancelled.Token); Check(false,"cancellation"); }
catch (OperationCanceledException) { Check(true, "cancelled track fetch cannot return stale lyrics"); }
await NewSourceChecks.Run(Check, scratch);
TimingChecks.Run(Check, scratch);
AutoStartChecks.Run(Check, scratch);
FullscreenChecks.Run(Check, scratch);
DisplayPlacementChecks.Run(Check);
await MultilingualChecks.Run(Check, scratch);
foreach (var language in Enum.GetValues<AppLanguage>()) { I18n.CurrentLanguage = language; Check(I18n.SelectTranslation.Length > 0 && I18n.AboutDialog("1.2").Length > 0, "localized UI " + language); }
Console.WriteLine($"{count} checks passed.");

sealed class FakeHandler(bool qqTranslation) : HttpMessageHandler
{
    public string SelectedId { get; private set; } = "";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string text;
        var url = request.RequestUri!.ToString();
        if (url.Contains("music.163.com/api/search")) text = JsonSerializer.Serialize(new { result = new { songs = new[] {
            new { id=1,name="夏の風",duration=240000,artists=new[]{new{name="別の人"}} },
            new { id=2,name="夏の風",duration=240000,artists=new[]{new{name="歌手"}} } } } });
        else if (url.Contains("music.163.com/api/song/lyric")) { SelectedId = url.Contains("id=2") ? "2" : "1"; text = JsonSerializer.Serialize(new { lrc = new { lyric="[00:01.000]窓の光" }, tlyric = new { lyric=qqTranslation ? "" : "[00:01.010]窗边的光" } }); }
        else if (url.Contains("client_search_cp")) text = JsonSerializer.Serialize(new { data = new { song = new { list = qqTranslation ? new[]{new{songname="夏の風",songmid="abc",interval=240,singer=new[]{new{name="歌手"}}}} : Array.Empty<object>() } } });
        else if (url.Contains("fcg_query_lyric")) text = "callback(" + JsonSerializer.Serialize(new { lyric=Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01.000]窓の光")), trans=Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01.000]来自 QQ 的译文")) }) + ");";
        else text = "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
    }
}
