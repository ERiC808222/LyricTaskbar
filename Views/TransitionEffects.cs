using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopLyrics.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace DesktopLyrics.Views;

public static class TransitionEffects
{
    public static void Clear(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.OpacityMask = null;
        if (element.RenderTransform is TranslateTransform move)
        { move.BeginAnimation(TranslateTransform.XProperty, null); move.BeginAnimation(TranslateTransform.YProperty, null); move.X = move.Y = 0; }
    }
    public static void Begin(FrameworkElement outgoing, FrameworkElement incoming, LyricTransitionMode mode, int durationMs, Action completed)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 60, 300)));
        var oldFade = new DoubleAnimation(1, 0, duration) { FillBehavior = FillBehavior.Stop };
        var newFade = new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop };
        newFade.Completed += (_, _) => completed();
        if (mode is LyricTransitionMode.SlideFromLeft or LyricTransitionMode.SlideFromBottom)
        {
            var move = new TranslateTransform(); incoming.RenderTransform = move;
            move.BeginAnimation(mode == LyricTransitionMode.SlideFromLeft ? TranslateTransform.XProperty : TranslateTransform.YProperty,
                new DoubleAnimation(mode == LyricTransitionMode.SlideFromLeft ? -12 : 7, 0, duration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        else if (mode == LyricTransitionMode.RevealFromLeft)
        {
            var solid = new GradientStop(Color.FromArgb(255, 255, 255, 255), 0);
            var clear = new GradientStop(Color.FromArgb(0, 255, 255, 255), 0);
            incoming.OpacityMask = new LinearGradientBrush(new GradientStopCollection { solid, clear }, new Point(0, .5), new Point(1, .5));
            solid.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.HoldEnd });
            clear.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(.18, 1, duration) { FillBehavior = FillBehavior.HoldEnd });
        }
        outgoing.BeginAnimation(UIElement.OpacityProperty, oldFade);
        incoming.BeginAnimation(UIElement.OpacityProperty, newFade);
    }
}
