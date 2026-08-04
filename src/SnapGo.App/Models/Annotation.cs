using System.Windows.Media;

namespace SnapGo.App.Models;

public enum AnnotationType { Rectangle, Arrow, Text, Freehand }

public class Annotation
{
    public AnnotationType Type { get; init; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string? Text { get; set; }
    public double FontSize { get; set; } = 16;
    public List<System.Windows.Point>? Points { get; set; }
    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeThickness { get; set; } = 2;
}
