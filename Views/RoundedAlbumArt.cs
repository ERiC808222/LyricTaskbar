using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DesktopLyrics.Views;

public sealed class RoundedAlbumArt : Grid
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(RoundedAlbumArt), new PropertyMetadata(null, (d, e) => ((RoundedAlbumArt)d)._image.Source = (ImageSource?)e.NewValue));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(RoundedAlbumArt), new PropertyMetadata(6.0, (d, _) => ((RoundedAlbumArt)d).UpdateClip()));
    private readonly System.Windows.Controls.Image _image = new() { Stretch = Stretch.UniformToFill };
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public RoundedAlbumArt()
    {
        Background = new SolidColorBrush(Color.FromRgb(37, 37, 48));
        Children.Add(new TextBlock { Text = "♫", FontSize = 20, Foreground = new SolidColorBrush(Color.FromRgb(120, 124, 136)), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality); Children.Add(_image);
        SizeChanged += (_, _) => UpdateClip();
    }
    protected override System.Windows.Size ArrangeOverride(System.Windows.Size size) { var result = base.ArrangeOverride(size); UpdateClip(size); return result; }
    private void UpdateClip(System.Windows.Size? size = null)
    {
        var actual = size ?? new System.Windows.Size(ActualWidth, ActualHeight);
        double radius = Math.Clamp(Radius, 0, Math.Min(actual.Width, actual.Height) / 2);
        Clip = new RectangleGeometry(new Rect(0, 0, actual.Width, actual.Height), radius, radius);
    }
}
