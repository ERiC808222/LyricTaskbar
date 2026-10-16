using DesktopLyrics.Models;
using System.Text.Json;
namespace DesktopLyrics.Services;
public static class I18n
{
    public static AppLanguage CurrentLanguage { get; set; } = AppLanguage.Chinese;
    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Extra = LoadTranslations();
    private static Dictionary<AppLanguage, Dictionary<string, string>> LoadTranslations()
    {
        var result = new Dictionary<AppLanguage, Dictionary<string, string>>();
        var assembly = typeof(I18n).Assembly;
        foreach (var (language, code) in new[] { (AppLanguage.Korean, "ko"), (AppLanguage.Tibetan, "bo"), (AppLanguage.French, "fr"), (AppLanguage.Spanish, "es") })
        {
            var name = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith($"Languages.{code}.json", StringComparison.Ordinal));
            if (name == null) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            result[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        return result;
    }
    private static string T(string en, string zh, string ja) => CurrentLanguage switch
    {
        AppLanguage.Japanese => ja, AppLanguage.Chinese => zh,
        _ => Extra.TryGetValue(CurrentLanguage, out var values) && values.TryGetValue(en, out var translated) ? translated : en
    };
    public static string LanguageName(AppLanguage language) => language switch { AppLanguage.Chinese => "简体中文", AppLanguage.Japanese => "日本語", AppLanguage.Korean => "한국어", AppLanguage.Tibetan => "བོད་སྐད།", AppLanguage.French => "Français", AppLanguage.Spanish => "Español", _ => "English" };
    public static string LyricsLanguageMenu => T("Lyric search language", "歌词检索语言", "歌詞検索の言語");
    public static string LyricsLanguageName(LyricsLanguageMode language) => language switch
    {
        LyricsLanguageMode.Auto => T("Automatic", "自动识别", "自動判定"), LyricsLanguageMode.Japanese => "日本語", LyricsLanguageMode.Korean => "한국어",
        LyricsLanguageMode.Tibetan => "བོད་སྐད།", LyricsLanguageMode.French => "Français", LyricsLanguageMode.Spanish => "Español", LyricsLanguageMode.Chinese => "中文", _ => "English"
    };
    public static string TrackFade => T("Fade cover and track details together", "切歌时封面与曲目信息同步渐变", "曲の切り替えでジャケットと曲名を同時にフェード");
    public static string TransitionName(LyricTransitionMode mode) => mode switch
    {
        LyricTransitionMode.SlideFromLeft => T("Slide in from left", "从左滑入", "左からスライド"),
        LyricTransitionMode.SlideFromBottom => T("Rise from below", "从下浮现", "下からフェードイン"),
        LyricTransitionMode.RevealFromLeft => T("Reveal gradually from left", "从左渐显", "左から徐々に表示"),
        _ => T("Crossfade", "交叉渐变", "クロスフェード")
    };
    public static string DisplayTiming => T("Lyric display settings…", "歌词显示调节…", "歌詞表示の調整…");
    public static string AutoStart => T("Start when signing in to Windows", "开机自启（登录 Windows 时）", "Windowsログイン時に自動起動");
    public static string AutoStartHint => T("Start this copy at sign-in; uncheck to remove it", "勾选后登录时启动当前版本；取消勾选即可关闭", "チェックするとこのバージョンを自動起動、解除すると無効化");
    public static string AutoStartFailed => T("Could not change the auto-start setting.", "未能修改开机自启设置。", "自動起動の設定を変更できませんでした。");
    public static string FullscreenTopmost => T("Keep lyrics on top in fullscreen", "全屏时保持置顶", "全画面でも歌詞を最前面に表示");
    public static string FullscreenTopmostHint => T("Off: hide on the display with a fullscreen foreground app; restore when leaving fullscreen", "关闭时遇到同屏全屏应用会隐藏歌词，退出全屏后自动恢复", "無効時は同じモニターの全画面アプリで歌詞を隠し、終了すると復元");
    public static string TimingTitle => T("Smooth, on-time lyrics", "歌词过渡与同步", "歌詞の切り替えと同期");
    public static string TimingHelp => T("Positive values show lyrics earlier; negative values delay them. Changes apply immediately without seeking the song.", "歌词慢了就调成正数，快了就调成负数。调整立即生效，只影响歌词，不改变歌曲播放位置。", "正の値で歌詞を早め、負の値で遅らせます。曲の再生位置は変更されません。調整はすぐ反映されます。");
    public static string LyricAdvance => T("Lyric advance / delay", "歌词提前／延后", "歌詞を早める／遅らせる");
    public static string DelayStep => T("Delay 0.1 s", "延后 0.1 秒", "0.1秒遅く");
    public static string AdvanceStep => T("Advance 0.1 s", "提前 0.1 秒", "0.1秒早く");
    public static string ResetTiming => T("Reset to zero", "归零", "ゼロに戻す");
    public static string Crossfade => T("Animate original and translation together", "启用切句动效（原词、中文同步）", "原文と訳文を同時にアニメーション");
    public static string FadeDuration => T("Transition duration", "渐变时长", "切り替え時間");
    public static string MarqueeDelay => T("Wait before scrolling long lines", "长句开始横向滚动前等待", "長い行のスクロール待ち時間");
    public static string MarqueeHelp => T("Only overflowing lines scroll. Less waiting starts the scroll earlier; scrolling speed adapts to the time remaining for this line.", "只有超出宽度的长句才会横向滚动；减小等待时间会更早开始滚动。速度会按这句剩余时间调整。", "表示幅を超える行だけスクロールします。待ち時間を短くすると早く動き始め、行の残り時間に合わせて速度を調整します。");
    public static string Done => T("Done", "完成", "完了");
    public static string AdjustLive => T("Applies immediately; saved when closed", "立即生效，关闭窗口后保存", "すぐ反映され、閉じると保存されます");
    public static string WaitingForPlayback => T("Waiting for music…", "等待音乐播放…", "音楽の再生を待っています…");
    public static string SupportedMediaSources => T("Apple Music / Spotify / browser media", "支持 Apple Music / Spotify / 浏览器媒体", "Apple Music / Spotify / ブラウザーに対応");
    public static string FetchingLyrics => T("Fetching lyrics…", "正在检索歌词…", "歌詞を検索中…");
    public static string NoLyricsFound => T("No synced lyrics found", "未找到同步歌词", "同期歌詞が見つかりません");
    public static string NoTranslation => T("No Chinese translation for this line", "此句暂无中文译文", "この行の中国語訳はありません");
    public static string NotPlaying => T("Not playing", "未在播放", "再生していません");
    public static string Idle => T("Idle", "空闲", "待機中");
    public static string LyricsFetchFailed => T("Lyrics request failed", "歌词获取失败", "歌詞の取得に失敗しました");
    public static string SyncedLines(int count) => string.Format(T("{0} synced lines", "已同步 {0} 行歌词", "{0} 行の歌詞を同期しました"), count);
    public static string LockPosition => T("Lock position", "锁定位置", "位置を固定");
    public static string UnlockPosition => T("Unlock position (drag / resize)", "解锁位置（拖动／调整长度）", "位置の固定を解除（移動／幅の調整）");
    public static string DualLineLyrics => T("Two lyric lines", "双行歌词显示", "歌詞を2行で表示");
    public static string ShowTranslation => T("Chinese translation on second line", "第二行显示中文译文", "2行目に中国語訳を表示");
    public static string ImportLyrics => T("Import lyrics for this track…", "为当前歌曲导入歌词…", "この曲の歌詞を読み込む…");
    public static string RemoveLocal => T("Use online lyrics for this track", "当前歌曲恢复使用在线歌词", "この曲をオンライン歌詞に戻す");
    public static string PlayFirst => T("Play a track before importing lyrics.", "请先播放一首歌曲，再导入歌词。", "先に曲を再生してから歌詞を読み込んでください。");
    public static string InvalidLrc => T("The file has no timestamped lyrics.", "文件中没有有效的带时间戳歌词。", "ファイルに時刻付きの歌詞がありません。");
    public static string SelectOriginal => T("Select original or bilingual LRC", "选择日文原歌词或双语 LRC", "原文または対訳付きのLRCを選択");
    public static string SelectTranslation => T("Select Chinese LRC; Cancel to skip", "选择中文译文 LRC；已有双语或没有译文时点取消", "中国語訳のLRCを選択（不要ならキャンセル）");
    public static string ThemeMenu => T("Theme", "主题配色", "テーマ");
    public static string ThemeAuto => T("Follow Windows", "跟随系统", "Windowsに合わせる");
    public static string ThemeDark => T("White text (dark taskbar)", "白色文字（深色任务栏）", "白い文字（暗いタスクバー）");
    public static string ThemeLight => T("Dark text (light taskbar)", "深色文字（浅色任务栏）", "黒い文字（明るいタスクバー）");
    public static string LanguageMenu => T("Interface language", "界面语言", "表示言語");
    public static string DisplayMenu => T("Display monitor", "放置到显示器", "表示するモニター");
    public static string DisplayItem(int index, bool primary, int width, int height) => $"{T("Display", "显示器", "モニター")} {index} {(primary ? T("(Primary)", "（主屏）", "（メイン）") : "")} [{width}×{height}]";
    public static string RefreshPlayback => T("Refresh playback and lyrics", "刷新播放状态与歌词", "再生情報と歌詞を更新");
    public static string ResetPosition => T("Reset taskbar position", "重置任务栏位置", "タスクバー上の位置をリセット");
    public static string CheckForUpdates => T("Upstream project", "原版项目", "元のプロジェクト");
    public static string CheckingUpdates => T("Checking…", "正在检查…", "確認中…");
    public static string AlreadyLatestVersion(string version) => version;
    public static string NewVersionFound(string tag) => tag;
    public static string NewVersionNotificationTitle(string tag) => tag;
    public static string NewVersionNotificationBody => T("View upstream releases", "查看原版发布", "元のリリースを表示");
    public static string AboutMenu => T("About LyricBar JP", "关于 LyricBar JP", "LyricBar JPについて");
    public static string AboutDialog(string version) => $"LyricBar JP {version}\n" + T("Multilingual lyrics with available Chinese translations. Based on ysheng920/LyricBar (MIT).", "多语言原歌词＋现有中文译文。基于 ysheng920/LyricBar（MIT），保留原版许可。", "多言語の歌詞＋既存の中国語訳。ysheng920/LyricBar（MIT）を基に作成。");
    public static string LyricsSourceMenu => T("Lyrics source", "歌词来源", "歌詞の取得先");
    public static string SourceModeName(LyricsSourceMode mode) => mode switch
    {
        LyricsSourceMode.Auto => T("Automatic (prefer Chinese translations)", "自动（优先中文译文）", "自動（中国語訳を優先）"),
        LyricsSourceMode.AppleMusic => T("Apple Music via Paxsenix", "Apple Music（Paxsenix 第三方接口）", "Apple Music（Paxsenix経由）"),
        LyricsSourceMode.Amll => T("AMLL community", "AMLL 社区歌词库", "AMLL コミュニティー"),
        _ => T("Original four providers", "原有四个平台", "従来の4つの取得先")
    };
    public static string ExitMenu => T("Exit", "退出", "終了");
}
