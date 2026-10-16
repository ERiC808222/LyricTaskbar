using DesktopLyrics.Models;
using DesktopLyrics.Services;

internal static class TimingChecks
{
    public static void Run(Action<bool, string> check, string scratch)
    {
        double now = 0;
        var epoch = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
        DateTimeOffset utc = epoch;
        var clock = new PlaybackClock(() => now, () => utc);
        var duration = TimeSpan.FromSeconds(180);
        void Report(double position, bool playing = true) => clock.Update(TimeSpan.FromSeconds(position), utc, playing);
        var cues = new List<LrcLine> { new() { StartTime = TimeSpan.FromSeconds(9), Text = "first" }, new() { StartTime = TimeSpan.FromSeconds(10), Text = "second" } };
        Report(9.8); now = .21; utc = epoch.AddSeconds(now);
        check(LrcParser.FindActiveIndex(cues, clock.Position(duration)) == 1, "playback clock crosses the next lyric boundary");
        long seeks = clock.SeekVersion;
        Report(9.91);
        check(LrcParser.FindActiveIndex(cues, clock.Position(duration)) == 1 && clock.SeekVersion == seeks, "a small backward SMTC correction never flashes the previous lyric");
        double previous = clock.Position(duration).TotalSeconds;
        bool monotonic = true;
        for (int i = 0; i < 40; i++)
        {
            now += .025; utc = epoch.AddSeconds(now);
            double position = clock.Position(duration).TotalSeconds;
            monotonic &= position >= previous;
            previous = position;
        }
        check(monotonic, "jitter correction remains monotonic throughout forty display frames");
        clock.Update(TimeSpan.FromSeconds(2), epoch.AddSeconds(.1), true);
        check(clock.Position(duration).TotalSeconds >= previous, "out-of-order timeline reports cannot rewind the clock");
        Report(3);
        check(Math.Abs(clock.Position(duration).TotalSeconds - 3) < .001 && clock.SeekVersion > seeks, "an intentional backward seek still changes lyrics immediately");
        Report(20); now += .4; utc = epoch.AddSeconds(now);
        double pausePosition = clock.Position(duration).TotalSeconds;
        // A duplicate paused/resumed report must not add the entire pause duration.
        var pauseStamp = utc; Report(pausePosition, false);
        now += 60; utc = epoch.AddSeconds(now);
        check(Math.Abs(clock.Position(duration).TotalSeconds - pausePosition) < .001, "paused playback does not advance");
        clock.Update(TimeSpan.FromSeconds(pausePosition), pauseStamp, true);
        check(Math.Abs(clock.Position(duration).TotalSeconds - pausePosition) < .001, "resuming with an old timeline does not count paused time");
        now += .5; utc = epoch.AddSeconds(now);
        check(Math.Abs(clock.Position(duration).TotalSeconds - pausePosition - .5) < .001, "resumed clock advances smoothly");
        clock.Update(TimeSpan.FromSeconds(pausePosition), pauseStamp, true);
        check(Math.Abs(clock.Position(duration).TotalSeconds - pausePosition - .5) < .001, "duplicate metadata and timeline reports do not reset interpolation");
        utc = utc.AddHours(-1); now += .5;
        check(Math.Abs(clock.Position(duration).TotalSeconds - pausePosition - 1) < .001, "wall-clock changes do not affect playback interpolation");
        double beforeRate = clock.Position(duration).TotalSeconds;
        clock.Update(TimeSpan.FromSeconds(pausePosition), pauseStamp, true, 1.5);
        now += .5;
        check(Math.Abs(clock.Position(duration).TotalSeconds - beforeRate - .75) < .001, "playback rate changes with duplicate timelines do not rewind position");
        clock.Reset(); Report(5); now += .5; Report(6); now += .5;
        check(Math.Abs(clock.Position(duration).TotalSeconds - 6.5) < .001, "new tracks reset prior timing history");
        clock.Reset(); Report(200);
        check(clock.Position(duration) == duration, "playback interpolation clamps to duration");
        clock.Reset(); utc = epoch; now = 0; Report(9.8);
        now = 1; utc = epoch.AddSeconds(1); Report(9.8);
        check(Math.Abs(clock.Position(duration).TotalSeconds - 10.8) < .001, "fresh timestamps with repeated coarse positions cannot flash an older lyric");
        clock.Reset(); utc = epoch.AddSeconds(1);
        clock.Update(TimeSpan.FromSeconds(5), epoch, true);
        check(Math.Abs(clock.Position(duration).TotalSeconds - 6) < .001, "initial playback compensates for the producer timestamp age");
        check(LyricTiming.DisplayPosition(TimeSpan.FromSeconds(9.8), 300) == TimeSpan.FromSeconds(10.1), "positive lyric offset advances display without seeking audio");
        check(LrcParser.FindActiveIndex(cues, LyricTiming.DisplayPosition(TimeSpan.FromSeconds(10.1), -300)) == 0, "negative lyric offset delays the displayed line");
        check(LyricTiming.DisplayPosition(TimeSpan.FromSeconds(.1), -500) == TimeSpan.Zero, "lyric delay never produces a negative position");
        check(LyricTiming.DisplayPosition(TimeSpan.FromSeconds(20), 99999) == TimeSpan.FromSeconds(30), "lyric advance is bounded at ten seconds");
        var shortLine = LyricTiming.Marquee(200, 250, 150, 3);
        check(shortLine.Distance == 0, "short lines do not scroll");
        var longLine = LyricTiming.Marquee(420, 220, 150, 3);
        check(longLine.Distance == 200 && longLine.Delay + longLine.Duration < 3, "long line scroll fits within its cue when a readable speed permits");
        var quick = LyricTiming.Marquee(420, 220, 0, 3);
        check(quick.Delay == 0, "zero wait starts long-line scrolling immediately");
        var veryLong = LyricTiming.Marquee(2220, 220, 150, .4);
        check(veryLong.Duration >= veryLong.Distance / 180, "extreme text length does not create an unreadably fast marquee");
        var settings = new SettingsService(System.IO.Path.Combine(scratch, "timing"));
        settings.Settings.LyricAdvanceMilliseconds = 300; settings.Settings.EnableCrossfade = false;
        settings.Settings.CrossfadeDurationMilliseconds = 200; settings.Settings.MarqueeStartDelayMilliseconds = 50; settings.SaveSettings();
        var restored = new SettingsService(System.IO.Path.Combine(scratch, "timing")).Settings;
        check(restored.LyricAdvanceMilliseconds == 300 && !restored.EnableCrossfade && restored.CrossfadeDurationMilliseconds == 200 && restored.MarqueeStartDelayMilliseconds == 50, "display timing preferences persist after restart");
    }
}
