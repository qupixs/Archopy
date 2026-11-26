using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Archopy;

// Native paths from Feather 4.29.2 on its original 24-unit grid.
public sealed class FeatherIcon : Viewbox
{
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["down"] = Shape("M6 9 L12 15 18 9"),
        ["activity"] = Shape("M22 12 H18 L15 21 9 3 6 12 H2"),
        ["login"] = Shape("M9 21 H5 Q3 21 3 19 V5 Q3 3 5 3 H9 M13 7 L18 12 13 17 M18 12 H7"),
        ["up"] = Shape("M18 15 L12 9 6 15"),
        ["previous"] = Shape("M19 20 L9 12 19 4 Z M5 19 V5"),
        ["next"] = Shape("M5 4 L15 12 5 20 Z M19 5 V19"),
        ["play"] = Shape("M5 3 L19 12 5 21 Z"),
        ["pause"] = Shape("M6 4 H10 V20 H6 Z M14 4 H18 V20 H14 Z"),
        ["music"] = Shape("M9 18 V5 L21 3 V16 M9 18 A3 3 0 1 1 3 18 A3 3 0 1 1 9 18 M21 16 A3 3 0 1 1 15 16 A3 3 0 1 1 21 16")
    };

    private readonly Path path = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    public string Glyph { set => path.Data = Shapes[value]; }

    public FeatherIcon()
    {
        Width = Height = 18;
        path.SetResourceReference(Path.StrokeProperty, "PillInk");
        Child = new Canvas { Width = 24, Height = 24, Children = { path } };
    }

    private static Geometry Shape(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
