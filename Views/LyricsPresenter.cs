using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopLyrics.Services;
using DesktopLyrics.Models;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace DesktopLyrics.Views;

/// <summary>Two reusable lyric layers; original and translation always share one fade clock.</summary>
public sealed class LyricsPresenter : Grid
{
    private static DependencyProperty Register<T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(LyricsPresenter), new FrameworkPropertyMetadata(value, Changed));
    public static readonly DependencyProperty PrimaryTextProperty = Register(nameof(PrimaryText), "");
    public static readonly DependencyProperty SecondaryTextProperty = Register(nameof(SecondaryText), "");
    public static readonly DependencyProperty IsDualLineProperty = Register(nameof(IsDualLine), true);
    public static readonly DependencyProperty PrimaryBrushProperty = Register<Brush>(nameof(PrimaryBrush), Brushes.White);
    public static readonly DependencyProperty SecondaryBrushProperty = Register<Brush>(nameof(SecondaryBrush), Brushes.LightGray);
    public static readonly DependencyProperty EnableCrossfadeProperty = Register(nameof(EnableCrossfade), true);
    public static readonly DependencyProperty FadeMillisecondsProperty = Register(nameof(FadeMilliseconds), 160);
    public static readonly DependencyProperty MarqueeDelayMillisecondsProperty = Register(nameof(MarqueeDelayMilliseconds), 150);
    public static readonly DependencyProperty LineSecondsProperty = Register(nameof(LineSeconds), 10.0);
    public static readonly DependencyProperty FrameVersionProperty = Register(nameof(FrameVersion), 0);
    public static readonly DependencyProperty AnimateChangeProperty = Register(nameof(AnimateChange), true);
    public static readonly DependencyProperty TransitionModeProperty = Register(nameof(TransitionMode), LyricTransitionMode.Crossfade);
    public string PrimaryText { get => (string)GetValue(PrimaryTextProperty); set => SetValue(PrimaryTextProperty, value); }
    public string SecondaryText { get => (string)GetValue(SecondaryTextProperty); set => SetValue(SecondaryTextProperty, value); }
    public bool IsDualLine { get => (bool)GetValue(IsDualLineProperty); set => SetValue(IsDualLineProperty, value); }
    public Brush PrimaryBrush { get => (Brush)GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
    public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    public bool EnableCrossfade { get => (bool)GetValue(EnableCrossfadeProperty); set => SetValue(EnableCrossfadeProperty, value); }
    public int FadeMilliseconds { get => (int)GetValue(FadeMillisecondsProperty); set => SetValue(FadeMillisecondsProperty, value); }
    public int MarqueeDelayMilliseconds { get => (int)GetValue(MarqueeDelayMillisecondsProperty); set => SetValue(MarqueeDelayMillisecondsProperty, value); }
    public double LineSeconds { get => (double)GetValue(LineSecondsProperty); set => SetValue(LineSecondsProperty, value); }
    public int FrameVersion { get => (int)GetValue(FrameVersionProperty); set => SetValue(FrameVersionProperty, value); }
    public bool AnimateChange { get => (bool)GetValue(AnimateChangeProperty); set => SetValue(AnimateChangeProperty, value); }
    public LyricTransitionMode TransitionMode { get => (LyricTransitionMode)GetValue(TransitionModeProperty); set => SetValue(TransitionModeProperty, value); }

    private readonly Layer[] _layers = { new(), new() };
    private DispatcherOperation? _pending;
    private bool _initialized;
    private int _active, _generation, _frame = -1, _delay;
    private double _lineStart;
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    internal bool ForceAnimationForDiagnostics { get; set; }
    public int CommitCount { get; private set; }
    public int MarqueeStartCount { get; private set; }
    public string DisplayedPrimary => _layers[_active].Primary.Text;
    public string DisplayedSecondary => _layers[_active].Secondary.Text;
    public double IncomingOpacity => _layers[_active].Panel.Opacity;
    public double OutgoingOpacity => _layers[1 - _active].Panel.Opacity;
    public double ScrollX => _layers[_active].PrimaryMove.X;
    public double IncomingX => (_layers[_active].Panel.RenderTransform as TranslateTransform)?.X ?? 0;
    public double IncomingY => (_layers[_active].Panel.RenderTransform as TranslateTransform)?.Y ?? 0;
    public bool HasRevealMask => _layers[_active].Panel.OpacityMask != null;

    public LyricsPresenter()
    {
        ClipToBounds = true;
        foreach (var layer in _layers) Children.Add(layer.Panel);
        SizeChanged += (_, _) => Queue();
        Loaded += (_, _) => { if (_initialized) { FinishFade(); StartScroll(_layers[_active], reset: false); } };
        Unloaded += (_, _) => { _pending?.Abort(); _pending = null; FinishFade(); foreach (var layer in _layers) layer.StopScroll(); };
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LyricsPresenter)d).Queue();
    private void Queue()
    {
        if (_pending is { Status: DispatcherOperationStatus.Pending }) return;
        _pending = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { _pending = null; Commit(); }));
    }
    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
    {
        // Also supports offscreen RenderTargetBitmap and the first layout pass.
        if (!_initialized) Commit();
        var desired = base.MeasureOverride(constraint);
        return new System.Windows.Size(double.IsInfinity(constraint.Width) ? desired.Width : Math.Min(desired.Width, constraint.Width), desired.Height);
    }
    private void Commit()
    {
        var current = _layers[_active];
        bool textChanged = !_initialized || current.Primary.Text != PrimaryText || current.Secondary.Text != SecondaryText || current.Dual != IsDualLine;
        bool newFrame = _frame != FrameVersion;
        foreach (var layer in _layers) { layer.Primary.Foreground = PrimaryBrush; layer.Secondary.Foreground = SecondaryBrush; }
        if (!textChanged && !newFrame)
        {
            if (!EnableCrossfade) FinishFade();
            // A translation/color notification does not restart a running marquee.
            if (_delay != MarqueeDelayMilliseconds || current.ScrollWidth != ActualWidth) StartScroll(current, reset: false);
            return;
        }
        CommitCount++;
        bool animate = textChanged && _initialized && EnableCrossfade && AnimateChange && IsDualLine == current.Dual &&
            (IsLoaded || ForceAnimationForDiagnostics) && (SystemParameters.ClientAreaAnimation || ForceAnimationForDiagnostics);
        FinishFade();
        Layer outgoing = current;
        if (textChanged)
        {
            _active = 1 - _active;
            current = _layers[_active];
            current.Set(PrimaryText, SecondaryText, IsDualLine);
        }
        _frame = FrameVersion; _initialized = true; _lineStart = _watch.Elapsed.TotalSeconds;
        current.Panel.Opacity = 1;
        outgoing.Panel.Opacity = textChanged ? 0 : 1;
        UpdateLayout();
        StartScroll(current, reset: true);
        if (animate)
        {
            int generation = ++_generation;
            TransitionEffects.Begin(outgoing.Panel, current.Panel, TransitionMode, FadeMilliseconds, () =>
            {
                if (generation == _generation) { outgoing.StopScroll(); TransitionEffects.Clear(outgoing.Panel); TransitionEffects.Clear(current.Panel); }
            });
        }
        else if (textChanged) outgoing.StopScroll();
    }
    private void FinishFade()
    {
        _generation++;
        for (int i = 0; i < _layers.Length; i++) { TransitionEffects.Clear(_layers[i].Panel); _layers[i].Panel.Opacity = i == _active ? 1 : 0; }
    }
    private void StartScroll(Layer layer, bool reset)
    {
        _delay = MarqueeDelayMilliseconds;
        layer.ScrollWidth = ActualWidth;
        double remaining = Math.Max(.4, LineSeconds - (_watch.Elapsed.TotalSeconds - _lineStart));
        AnimateRow(layer.Primary, layer.PrimaryClip, layer.PrimaryMove, reset, remaining);
        if (layer.Dual) AnimateRow(layer.Secondary, layer.SecondaryClip, layer.SecondaryMove, reset, remaining);
        else { layer.SecondaryMove.BeginAnimation(TranslateTransform.XProperty, null); layer.SecondaryMove.X = 0; }
    }
    private void AnimateRow(TextBlock text, Border clip, TranslateTransform move, bool reset, double remaining)
    {
        double from = reset ? 0 : move.X;
        move.BeginAnimation(TranslateTransform.XProperty, null);
        text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var plan = LyricTiming.Marquee(text.DesiredSize.Width, clip.ActualWidth, reset ? MarqueeDelayMilliseconds : 0, remaining);
        from = Math.Clamp(from, -plan.Distance, 0);
        move.X = from;
        if (plan.Distance <= 0) return;
        double distance = Math.Max(0, plan.Distance + from);
        if (distance < 1) return;
        MarqueeStartCount++;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, -plan.Distance, TimeSpan.FromSeconds(Math.Max(.1, plan.Duration * distance / plan.Distance)))
        { BeginTime = TimeSpan.FromSeconds(plan.Delay), FillBehavior = FillBehavior.HoldEnd });
    }
    private sealed class Layer
    {
        public readonly StackPanel Panel = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        public readonly TextBlock Primary = new() { FontFamily = new FontFamily("Yu Gothic UI, Meiryo, Microsoft YaHei UI, Segoe UI") };
        public readonly TextBlock Secondary = new() { FontSize = 11, FontFamily = new FontFamily("Microsoft YaHei UI, Yu Gothic UI, Meiryo, Segoe UI") };
        public readonly Border PrimaryClip = new() { ClipToBounds = true };
        public readonly Border SecondaryClip = new() { ClipToBounds = true, Margin = new Thickness(0, 1.5, 0, 0) };
        public readonly TranslateTransform PrimaryMove = new(), SecondaryMove = new();
        public bool Dual;
        public double ScrollWidth;
        public Layer()
        {
            Primary.RenderTransform = PrimaryMove; Secondary.RenderTransform = SecondaryMove;
            var first = new StackPanel { Orientation = Orientation.Horizontal }; first.Children.Add(Primary); PrimaryClip.Child = first;
            var second = new StackPanel { Orientation = Orientation.Horizontal }; second.Children.Add(Secondary); SecondaryClip.Child = second;
            Panel.Children.Add(PrimaryClip); Panel.Children.Add(SecondaryClip);
        }
        public void Set(string primary, string secondary, bool dual)
        {
            StopScroll(); Primary.Text = primary; Secondary.Text = secondary; Dual = dual;
            Primary.FontFamily = new FontFamily(LanguageSupport.FontFamilyFor(primary));
            Secondary.FontFamily = new FontFamily(LanguageSupport.FontFamilyFor(secondary));
            Primary.FontSize = LanguageSupport.LyricFontSize(primary, dual);
            Secondary.FontSize = LanguageSupport.HasTibetan(secondary) ? 17 : 11;
            Primary.FontWeight = dual ? FontWeights.Bold : FontWeights.SemiBold;
            SecondaryClip.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        }
        public void StopScroll()
        {
            PrimaryMove.BeginAnimation(TranslateTransform.XProperty, null); PrimaryMove.X = 0;
            SecondaryMove.BeginAnimation(TranslateTransform.XProperty, null); SecondaryMove.X = 0;
        }
    }
}
