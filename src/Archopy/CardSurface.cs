using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Archopy;

// The upper shoulder leaves transparent space around the detached caption.
internal sealed class CardSurface : Border
{
    protected override void OnRender(DrawingContext drawing)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 140 || h < 30) return;
        var r = Math.Min(28, (h - 19) / 2);
        var shape = new StreamGeometry();
        using (var path = shape.Open())
        {
            void Line(double x, double y) => path.LineTo(new Point(x, y), true, false);
            void Curve(double x, double y, double endX, double endY) =>
                path.QuadraticBezierTo(new Point(x, y), new Point(endX, endY), true, false);
            path.BeginFigure(new Point(r, 1), true, true);
            Line(w - 100, 1);
            Curve(w - 87, 1, w - 84, 13);
            Curve(w - 81, 24, w - 67, 24);
            Line(w - r, 24);
            Curve(w - 1, 24, w - 1, 24 + r);
            Line(w - 1, h - r);
            Curve(w - 1, h - 1, w - r, h - 1);
            Line(r, h - 1);
            Curve(1, h - 1, 1, h - r);
            Line(1, r);
            Curve(1, 1, r, 1);
        }
        drawing.DrawGeometry(Background, null, shape);
    }
}

public sealed class PillSurface : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo change)
    {
        base.OnRenderSizeChanged(change);
        CornerRadius = new CornerRadius(Math.Min(change.NewSize.Width, change.NewSize.Height) / 2);
    }
}
