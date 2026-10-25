using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.UI;

/// <summary>Trip covers: the trip's own picture, or a generated one in the brand colours with the trip's initials.</summary>
public static class Covers
{
    /// <summary>Trip cover thumbnail, or a calm generated fallback with the trip's initials.</summary>
    public static Control Thumbnail(TravelRepository? repository, TripSnapshot trip, double width, double height, double radius = 8)
    {
        if (CoverImage(repository, trip) is { } bitmap)
            return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill } };
        return Generated(trip.Manifest.Title, width, height, radius);
    }
    public static Bitmap? CoverImage(TravelRepository? repository, TripSnapshot trip)
    {
        if (repository is null || !Guid.TryParse(trip.Manifest.Data["cover"]?["document"]?.ToString(), out var id) || trip.Find(id) is not { } doc) return null;
        try { var path = repository.DocumentPath(doc); return File.Exists(path) ? Bitmap.DecodeToWidth(File.OpenRead(path), 640) : null; }
        catch (Exception ex) when (ex is DomainException or IOException or ArgumentException or NotSupportedException) { return null; }
    }
    private static readonly (string A, string B)[] Gradients = [("#0F7B7A", "#45C4AF"), ("#103B5C", "#1593A5"), ("#1B4C8A", "#5B9BEA"), ("#4C3796", "#9C82EA"), ("#1C653C", "#4FC07E"), ("#844210", "#E2843A"), ("#842B58", "#DD6FA5")];
    public static Control Generated(string title, double width, double height, double radius = 8)
    {
        var hash = 0; foreach (var ch in title) hash = unchecked(hash * 31 + ch);
        var (a, b) = Gradients[Math.Abs(hash % Gradients.Length)];
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) } };
        // Soft rolling hills echo the route-and-landscape idea of the mark without repeating the logo.
        var hills = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,70 C25,52 45,60 60,66 C75,72 85,58 100,50 L100,100 L0,100 Z"), Fill = new SolidColorBrush(Colors.White, 0.16), Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Height = height * 0.55 };
        var hills2 = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,80 C20,70 40,86 65,74 C80,67 90,76 100,72 L100,100 L0,100 Z"), Fill = new SolidColorBrush(Colors.White, 0.14), Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Height = height * 0.4 };
        var initials = new TextBlock { Text = Ui.Initials(title), Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = Math.Max(11, (double.IsNaN(width) ? height : Math.Min(width, height)) * 0.32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = width > 120 ? 0 : 0.95 };
        return new Decoration { Width = width, Height = height, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Background = brush, Child = new Panel { Children = { hills, hills2, initials } } };
    }
}
