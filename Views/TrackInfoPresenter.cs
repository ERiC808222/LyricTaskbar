using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopLyrics.Models;
using DesktopLyrics.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace DesktopLyrics.Views;

/// <summary>One snapshot and one crossfade clock for cover, title, and artist.</summary>
public sealed class TrackInfoPresenter : Grid
{
    private static DependencyProperty Register<T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(TrackInfoPresenter), new PropertyMetadata(value, (d, _) => ((TrackInfoPresenter)d).Queue()));
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), "");
    public static readonly DependencyProperty ArtistProperty = Register(nameof(Artist), "");
    public static readonly DependencyProperty CoverProperty = Register<ImageSource?>(nameof(Cover), null);
    public static readonly DependencyProperty PrimaryBrushProperty = Register<Brush>(nameof(PrimaryBrush), Brushes.White);
    public static readonly DependencyProperty SecondaryBrushProperty = Register<Brush>(nameof(SecondaryBrush), Brushes.LightGray);
    public static readonly DependencyProperty EnableTransitionProperty = Register(nameof(EnableTransition), true);
    public static readonly DependencyProperty DurationMillisecondsProperty = Register(nameof(DurationMilliseconds), 160);
    public static readonly DependencyProperty FrameVersionProperty = Register(nameof(FrameVersion), 0);
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Artist { get => (string)GetValue(ArtistProperty); set => SetValue(ArtistProperty, value); }
    public ImageSource? Cover { get => (ImageSource?)GetValue(CoverProperty); set => SetValue(CoverProperty, value); }
    public Brush PrimaryBrush { get => (Brush)GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
    public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    public bool EnableTransition { get => (bool)GetValue(EnableTransitionProperty); set => SetValue(EnableTransitionProperty, value); }
    public int DurationMilliseconds { get => (int)GetValue(DurationMillisecondsProperty); set => SetValue(DurationMillisecondsProperty, value); }
    public int FrameVersion { get => (int)GetValue(FrameVersionProperty); set => SetValue(FrameVersionProperty, value); }
    private readonly Layer[] _layers = { new(), new() };
    private DispatcherOperation? _pending;
    private int _active, _generation, _frame;
    private bool _initialized;
    internal bool ForceAnimationForDiagnostics { get; set; }
    public string DisplayedTitle => _layers[_active].Title.Text;
    public string DisplayedArtist => _layers[_active].Artist.Text;
    public ImageSource? DisplayedCover => _layers[_active].Cover.Source;
    public double IncomingOpacity => _layers[_active].Panel.Opacity;
    public double OutgoingOpacity => _layers[1 - _active].Panel.Opacity;
    public int CommitCount { get; private set; }
    public TrackInfoPresenter()
    {
        Width = 176; Height = 40; ClipToBounds = true;
        foreach (var layer in _layers) Children.Add(layer.Panel);
        Unloaded += (_, _) => Finish();
    }
    private void Queue()
    {
        if (_pending is { Status: DispatcherOperationStatus.Pending }) return;
        _pending = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { _pending = null; Commit(); }));
    }
    protected override System.Windows.Size MeasureOverride(System.Windows.Size size) { if (!_initialized) Commit(); return base.MeasureOverride(size); }
    private void Finish()
    {
        _generation++;
        for (int i = 0; i < 2; i++) { TransitionEffects.Clear(_layers[i].Panel); _layers[i].Panel.Opacity = i == _active ? 1 : 0; }
    }
    private void Commit()
    {
        foreach (var layer in _layers) { layer.Title.Foreground = PrimaryBrush; layer.Artist.Foreground = SecondaryBrush; }
        var old = _layers[_active];
        bool change = !_initialized || old.Title.Text != Title || old.Artist.Text != Artist || _frame != FrameVersion;
        if (!change) { old.Cover.Source = Cover; if (!EnableTransition) Finish(); return; }
        bool animate = _initialized && EnableTransition && (IsLoaded || ForceAnimationForDiagnostics) && (SystemParameters.ClientAreaAnimation || ForceAnimationForDiagnostics);
        Finish(); _active = 1 - _active; var next = _layers[_active];
        next.Title.Text = Title; next.Artist.Text = Artist; next.Cover.Source = Cover;
        next.Title.FontFamily = new FontFamily(LanguageSupport.FontFamilyFor(Title)); next.Artist.FontFamily = new FontFamily(LanguageSupport.FontFamilyFor(Artist));
        next.Title.FontSize = LanguageSupport.HasTibetan(Title) ? 17 : 12;
        next.Artist.FontSize = LanguageSupport.HasTibetan(Artist) ? 13 : 10;
        next.Panel.Opacity = 1; old.Panel.Opacity = 0;
        _frame = FrameVersion; _initialized = true; CommitCount++;
        if (animate)
        {
            int generation = ++_generation;
            TransitionEffects.Begin(old.Panel, next.Panel, LyricTransitionMode.Crossfade, DurationMilliseconds, () => { if (generation == _generation) Finish(); });
        }
    }
    private sealed class Layer
    {
        public readonly Grid Panel = new() { Opacity = 0, Height = 40 };
        public readonly RoundedAlbumArt Cover = new() { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Title = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        public readonly TextBlock Artist = new() { FontSize = 10, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        public Layer()
        {
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Panel.Children.Add(Cover);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            info.Children.Add(Title); info.Children.Add(Artist); Grid.SetColumn(info, 1); Panel.Children.Add(info);
        }
    }
}
