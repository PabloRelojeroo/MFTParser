using System.Windows;
using System.Windows.Media;

namespace MftParser.Ui;

public static class Flags {
    public static readonly ImageSource UnitedStates = BuildUnitedStates();
    public static readonly ImageSource Mexico = BuildMexico();

    static ImageSource BuildUnitedStates() {
        var group = new DrawingGroup();
        group.Children.Add(Box("#FFFFFF", 0, 0, 247, 130));
        for (var stripe = 0; stripe < 13; stripe += 2)
            group.Children.Add(Box("#B22234", 0, stripe * 10, 247, 10));
        group.Children.Add(Box("#3C3B6E", 0, 0, 99, 70));

        var stars = new GeometryGroup();
        for (var row = 0; row < 9; row++) {
            var count = row % 2 == 0 ? 6 : 5;
            var offset = row % 2 == 0 ? 8.25 : 16.5;
            for (var col = 0; col < count; col++)
                stars.Children.Add(new EllipseGeometry(new Point(offset + col * 16.5, 7 + row * 7), 2.6, 2.6));
        }
        group.Children.Add(new GeometryDrawing(Brush("#FFFFFF"), null, stars));
        return Freeze(group);
    }

    static ImageSource BuildMexico() {
        var group = new DrawingGroup();
        group.Children.Add(Box("#006847", 0, 0, 70, 120));
        group.Children.Add(Box("#FFFFFF", 70, 0, 70, 120));
        group.Children.Add(Box("#CE1126", 140, 0, 70, 120));
        group.Children.Add(new GeometryDrawing(Brush("#8C5A2B"), null, new EllipseGeometry(new Point(105, 58), 15, 15)));
        var wreath = Geometry.Parse("M88,64 A17,17 0 0 0 122,64");
        group.Children.Add(new GeometryDrawing(null, new Pen(Brush("#3E8E41"), 5), wreath));
        return Freeze(group);
    }

    static GeometryDrawing Box(string color, double x, double y, double width, double height) =>
        new(Brush(color), null, new RectangleGeometry(new Rect(x, y, width, height)));

    static Brush Brush(string color) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    static ImageSource Freeze(DrawingGroup group) {
        group.ClipGeometry = new RectangleGeometry(group.Bounds);
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
