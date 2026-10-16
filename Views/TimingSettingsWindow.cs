using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopLyrics.Models;
using DesktopLyrics.Services;
using DesktopLyrics.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace DesktopLyrics.Views;

public sealed class TimingSettingsWindow : Window
{
    private readonly LyricsViewModel _model;
    private readonly Slider _advance, _fade, _delay;
    private readonly CheckBox _crossfade, _trackFade;
    private readonly ComboBox _transition;
    private readonly TextBlock _advanceValue = new(), _fadeValue = new(), _delayValue = new();
    public TimingSettingsWindow(LyricsViewModel model)
    {
        _model = model;
        Title = I18n.DisplayTiming; Width = 470; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI, Yu Gothic UI, Segoe UI"); FontSize = 13;
        if (model.Language == AppLanguage.Tibetan) { FontFamily = new System.Windows.Media.FontFamily("Microsoft Himalaya"); FontSize = 17; }
        else if (model.Language == AppLanguage.Korean) FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic");
        bool light = model.PrimaryTextBrush is SolidColorBrush brush && brush.Color.R < 100;
        Background = new SolidColorBrush(light ? Color.FromRgb(247, 248, 250) : Color.FromRgb(31, 34, 39));
        Foreground = light ? Brushes.Black : Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = I18n.TimingTitle, FontSize = 19, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = I18n.TimingHelp, TextWrapping = TextWrapping.Wrap, Opacity = .8, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(Label(I18n.LyricAdvance, _advanceValue));
        _advance = Slider(-10000, 10000, 100, model.Settings.LyricAdvanceMilliseconds);
        panel.Children.Add(_advance);
        var steps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 18) };
        steps.Children.Add(ActionButton(I18n.DelayStep, () => _advance.Value -= 100));
        steps.Children.Add(ActionButton(I18n.AdvanceStep, () => _advance.Value += 100));
        steps.Children.Add(ActionButton(I18n.ResetTiming, () => _advance.Value = 0));
        panel.Children.Add(steps);
        _crossfade = new CheckBox { Content = I18n.Crossfade, IsChecked = model.Settings.EnableCrossfade, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(_crossfade);
        _transition = new ComboBox { MinHeight = 30, Foreground = Brushes.Black, Background = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var mode in Enum.GetValues<LyricTransitionMode>()) _transition.Items.Add(new ComboBoxItem { Content = I18n.TransitionName(mode), Tag = mode });
        _transition.SelectedIndex = Math.Clamp((int)model.Settings.LyricTransition, 0, _transition.Items.Count - 1);
        panel.Children.Add(_transition);
        _trackFade = new CheckBox { Content = I18n.TrackFade, IsChecked = model.Settings.EnableTrackTransition, Foreground = Foreground, Margin = new Thickness(0, 4, 0, 8) };
        panel.Children.Add(_trackFade);
        panel.Children.Add(Label(I18n.FadeDuration, _fadeValue));
        _fade = Slider(60, 300, 10, model.Settings.CrossfadeDurationMilliseconds); panel.Children.Add(_fade);
        panel.Children.Add(Label(I18n.MarqueeDelay, _delayValue));
        _delay = Slider(0, 2000, 50, model.Settings.MarqueeStartDelayMilliseconds); panel.Children.Add(_delay);
        panel.Children.Add(new TextBlock { Text = I18n.MarqueeHelp, TextWrapping = TextWrapping.Wrap, FontSize = model.Language == AppLanguage.Tibetan ? 15 : 12, Opacity = .7, Margin = new Thickness(0, 8, 0, 14) });
        var done = ActionButton(I18n.Done, Close); done.HorizontalAlignment = HorizontalAlignment.Right; done.IsDefault = true;
        panel.Children.Add(done);
        _advance.ValueChanged += (_, _) => Apply(); _fade.ValueChanged += (_, _) => Apply(); _delay.ValueChanged += (_, _) => Apply();
        _transition.SelectionChanged += (_, _) => Apply(); _trackFade.Checked += (_, _) => Apply(); _trackFade.Unchecked += (_, _) => Apply();
        _crossfade.Checked += (_, _) => Apply(); _crossfade.Unchecked += (_, _) => Apply();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Closed += (_, _) => model.SaveDisplayTiming();
        Apply();
    }
    private StackPanel Label(string name, TextBlock value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        row.Children.Add(new TextBlock { Text = name, Width = 275, TextWrapping = TextWrapping.Wrap }); row.Children.Add(value); return row;
    }
    private static Slider Slider(double min, double max, double tick, double value) => new()
    {
        Minimum = min, Maximum = max, TickFrequency = tick, SmallChange = tick, LargeChange = tick * 5,
        IsSnapToTickEnabled = true, Value = Math.Clamp(value, min, max), Height = 24,
        ToolTip = I18n.AdjustLive
    };
    private Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(65, 70, 80)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        button.Click += (_, _) => action(); return button;
    }
    private void Apply()
    {
        int advance = (int)Math.Round(_advance.Value);
        _advanceValue.Text = advance == 0 ? "0.0 s" : $"{(advance > 0 ? "+" : "−")}{Math.Abs(advance) / 1000.0:0.0} s";
        _fadeValue.Text = $"{_fade.Value:0} ms"; _delayValue.Text = $"{_delay.Value / 1000:0.00} s";
        _fade.IsEnabled = _crossfade.IsChecked == true || _trackFade.IsChecked == true;
        _transition.IsEnabled = _crossfade.IsChecked == true;
        _model.SetTransitions((LyricTransitionMode)(_transition.SelectedItem as ComboBoxItem)!.Tag, _trackFade.IsChecked == true);
        _model.SetDisplayTiming(advance, _crossfade.IsChecked == true, (int)_fade.Value, (int)_delay.Value);
    }
}
