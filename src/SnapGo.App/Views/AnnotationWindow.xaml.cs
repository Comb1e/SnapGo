using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapGo.App.Models;

namespace SnapGo.App.Views;

public partial class AnnotationWindow : Window
{
    private BitmapSource _screenshot = null!;
    private AnnotationType _currentTool = AnnotationType.Rectangle;
    private Color _currentColor = Colors.Red;

    private readonly List<Annotation> _annotations = [];
    private readonly Stack<Annotation> _undoStack = new();
    private readonly Stack<Annotation> _redoStack = new();

    private bool _isDrawing;
    private System.Windows.Point _startPoint;
    private System.Windows.Point _currentPoint;
    private readonly List<System.Windows.Point> _freehandPoints = [];

    public byte[]? ResultImage { get; private set; }
    public List<Annotation> Annotations => [.. _annotations];

    public AnnotationWindow()
    {
        InitializeComponent();
    }

    public void SetScreenshot(BitmapSource screenshot)
    {
        _screenshot = screenshot;
        ScreenshotImage.Source = screenshot;
        UpdateUndoRedoButtons();
    }

    private void BtnRect_Click(object s, RoutedEventArgs e) => SelectTool(AnnotationType.Rectangle);
    private void BtnArrow_Click(object s, RoutedEventArgs e) => SelectTool(AnnotationType.Arrow);
    private void BtnText_Click(object s, RoutedEventArgs e) => SelectTool(AnnotationType.Text);
    private void BtnFreehand_Click(object s, RoutedEventArgs e) => SelectTool(AnnotationType.Freehand);

    private void SelectTool(AnnotationType tool) => _currentTool = tool;

    private void BtnColor_Click(object s, RoutedEventArgs e)
    {
        // Simple color picker using a popup with predefined colors
        var popup = new ColorPickerPopup(_currentColor)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (popup.ShowDialog() == true)
        {
            _currentColor = popup.SelectedColor;
        }
    }

    private void BtnUndo_Click(object s, RoutedEventArgs e) => Undo();
    private void BtnRedo_Click(object s, RoutedEventArgs e) => Redo();

    private void BtnDone_Click(object s, RoutedEventArgs e)
    {
        ResultImage = ExportImage();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object s, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = true;
        _startPoint = e.GetPosition(AnnotationCanvas);

        if (_currentTool == AnnotationType.Text)
        {
            AddTextAnnotation(_startPoint);
            _isDrawing = false;
        }
        else if (_currentTool == AnnotationType.Freehand)
        {
            _freehandPoints.Clear();
            _freehandPoints.Add(_startPoint);
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        _currentPoint = e.GetPosition(AnnotationCanvas);

        if (_currentTool == AnnotationType.Freehand)
        {
            _freehandPoints.Add(_currentPoint);
            DrawPreview();
        }
        else
        {
            DrawPreview();
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        _currentPoint = e.GetPosition(AnnotationCanvas);

        if (_currentTool == AnnotationType.Freehand && _freehandPoints.Count > 1)
        {
            _freehandPoints.Add(_currentPoint);
            var annotation = new Annotation
            {
                Type = AnnotationType.Freehand,
                Points = new List<System.Windows.Point>(_freehandPoints),
                StrokeColor = _currentColor,
                StrokeThickness = 2
            };
            AddAnnotation(annotation);
        }
        else if (_currentTool != AnnotationType.Text)
        {
            var dx = Math.Abs(_currentPoint.X - _startPoint.X);
            var dy = Math.Abs(_currentPoint.Y - _startPoint.Y);
            if (dx < 3 && dy < 3) { DrawPreview(); return; }

            var annotation = new Annotation
            {
                Type = _currentTool,
                X1 = _startPoint.X, Y1 = _startPoint.Y,
                X2 = _currentPoint.X, Y2 = _currentPoint.Y,
                StrokeColor = _currentColor,
                StrokeThickness = 2
            };
            AddAnnotation(annotation);
        }

        DrawPreview();
    }

    private void AddTextAnnotation(System.Windows.Point pt)
    {
        var dialog = new TextInputDialog { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var annotation = new Annotation
            {
                Type = AnnotationType.Text,
                X1 = pt.X, Y1 = pt.Y,
                Text = dialog.InputText,
                FontSize = 16,
                StrokeColor = _currentColor
            };
            AddAnnotation(annotation);
        }
    }

    private void AddAnnotation(Annotation annotation)
    {
        _annotations.Add(annotation);
        _undoStack.Push(annotation);
        _redoStack.Clear();
        UpdateUndoRedoButtons();
        RenderAnnotation(annotation);
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var annotation = _undoStack.Pop();
        _redoStack.Push(annotation);
        _annotations.Remove(annotation);
        UpdateUndoRedoButtons();
        RedrawAll();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var annotation = _redoStack.Pop();
        _undoStack.Push(annotation);
        _annotations.Add(annotation);
        UpdateUndoRedoButtons();
        RenderAnnotation(annotation);
    }

    private void DrawPreview()
    {
        // Clear and redraw all committed annotations + current preview
        AnnotationCanvas.Children.Clear();
        foreach (var a in _annotations) RenderAnnotation(a);

        if (!_isDrawing) return;

        if (_currentTool == AnnotationType.Rectangle)
        {
            var r = GetRect();
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = r.Width, Height = r.Height,
                Stroke = new SolidColorBrush(_currentColor), StrokeThickness = 2,
                StrokeDashArray = [4, 4]
            };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            AnnotationCanvas.Children.Add(rect);
        }
        else if (_currentTool == AnnotationType.Arrow)
        {
            DrawArrow(_startPoint, _currentPoint, _currentColor, true);
        }
        else if (_currentTool == AnnotationType.Freehand && _freehandPoints.Count > 1)
        {
            var polyline = new Polyline
            {
                Points = new PointCollection(_freehandPoints),
                Stroke = new SolidColorBrush(_currentColor), StrokeThickness = 2
            };
            AnnotationCanvas.Children.Add(polyline);
        }
    }

    private void RenderAnnotation(Annotation a)
    {
        switch (a.Type)
        {
            case AnnotationType.Rectangle:
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Abs(a.X2 - a.X1), Height = Math.Abs(a.Y2 - a.Y1),
                    Stroke = new SolidColorBrush(a.StrokeColor), StrokeThickness = a.StrokeThickness
                };
                Canvas.SetLeft(rect, Math.Min(a.X1, a.X2));
                Canvas.SetTop(rect, Math.Min(a.Y1, a.Y2));
                AnnotationCanvas.Children.Add(rect);
                break;

            case AnnotationType.Arrow:
                DrawArrow(new System.Windows.Point(a.X1, a.Y1),
                         new System.Windows.Point(a.X2, a.Y2), a.StrokeColor, false);
                break;

            case AnnotationType.Text when !string.IsNullOrEmpty(a.Text):
                var tb = new TextBlock
                {
                    Text = a.Text, FontSize = a.FontSize,
                    Foreground = new SolidColorBrush(a.StrokeColor),
                    Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
                };
                Canvas.SetLeft(tb, a.X1);
                Canvas.SetTop(tb, a.Y1);
                AnnotationCanvas.Children.Add(tb);
                break;

            case AnnotationType.Freehand when a.Points is { Count: > 1 }:
                var polyline = new Polyline
                {
                    Points = new PointCollection(a.Points),
                    Stroke = new SolidColorBrush(a.StrokeColor),
                    StrokeThickness = a.StrokeThickness
                };
                AnnotationCanvas.Children.Add(polyline);
                break;
        }
    }

    private void DrawArrow(System.Windows.Point start, System.Windows.Point end, Color color, bool isPreview)
    {
        var line = new Line
        {
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = new SolidColorBrush(color), StrokeThickness = 2,
            StrokeDashArray = isPreview ? new DoubleCollection([4, 4]) : null
        };
        AnnotationCanvas.Children.Add(line);

        // Simple arrowhead using two small lines
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var arrowLen = 12;
        var arrowAngle = Math.PI / 6;
        var p1 = new System.Windows.Point(
            end.X - arrowLen * Math.Cos(angle - arrowAngle),
            end.Y - arrowLen * Math.Sin(angle - arrowAngle));
        var p2 = new System.Windows.Point(
            end.X - arrowLen * Math.Cos(angle + arrowAngle),
            end.Y - arrowLen * Math.Sin(angle + arrowAngle));

        AnnotationCanvas.Children.Add(new Line
        {
            X1 = end.X, Y1 = end.Y, X2 = p1.X, Y2 = p1.Y,
            Stroke = new SolidColorBrush(color), StrokeThickness = 2
        });
        AnnotationCanvas.Children.Add(new Line
        {
            X1 = end.X, Y1 = end.Y, X2 = p2.X, Y2 = p2.Y,
            Stroke = new SolidColorBrush(color), StrokeThickness = 2
        });
    }

    private void RedrawAll()
    {
        AnnotationCanvas.Children.Clear();
        foreach (var a in _annotations) RenderAnnotation(a);
    }

    private byte[] ExportImage()
    {
        var width = (int)ScreenshotImage.ActualWidth;
        var height = (int)ScreenshotImage.ActualHeight;
        if (width <= 0 || height <= 0) return [];

        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new Image { Source = _screenshot, Stretch = Stretch.Fill, Width = width, Height = height });

        // Render annotations relative to the image
        var scaleX = width / AnnotationCanvas.ActualWidth;
        var scaleY = height / AnnotationCanvas.ActualHeight;

        var canvas = new Canvas();
        foreach (var a in _annotations)
        {
            var scaled = new Annotation
            {
                Type = a.Type, Text = a.Text, FontSize = a.FontSize * scaleY,
                StrokeColor = a.StrokeColor, StrokeThickness = a.StrokeThickness,
                X1 = a.X1 * scaleX, Y1 = a.Y1 * scaleY,
                X2 = a.X2 * scaleX, Y2 = a.Y2 * scaleY,
                Points = a.Points?.Select(p => new System.Windows.Point(p.X * scaleX, p.Y * scaleY)).ToList()
            };
            RenderToCanvas(canvas, scaled);
        }
        grid.Children.Add(canvas);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        grid.Measure(new System.Windows.Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        rtb.Render(grid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void RenderToCanvas(Canvas canvas, Annotation a)
    {
        switch (a.Type)
        {
            case AnnotationType.Rectangle:
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Abs(a.X2 - a.X1), Height = Math.Abs(a.Y2 - a.Y1),
                    Stroke = new SolidColorBrush(a.StrokeColor), StrokeThickness = a.StrokeThickness
                };
                Canvas.SetLeft(rect, Math.Min(a.X1, a.X2));
                Canvas.SetTop(rect, Math.Min(a.Y1, a.Y2));
                canvas.Children.Add(rect);
                break;
            case AnnotationType.Text when !string.IsNullOrEmpty(a.Text):
                var tb = new TextBlock { Text = a.Text, FontSize = a.FontSize, Foreground = new SolidColorBrush(a.StrokeColor) };
                Canvas.SetLeft(tb, a.X1);
                Canvas.SetTop(tb, a.Y1);
                canvas.Children.Add(tb);
                break;
            case AnnotationType.Freehand when a.Points is { Count: > 1 }:
                canvas.Children.Add(new Polyline
                {
                    Points = new PointCollection(a.Points),
                    Stroke = new SolidColorBrush(a.StrokeColor), StrokeThickness = a.StrokeThickness
                });
                break;
        }
    }

    private void UpdateUndoRedoButtons()
    {
        BtnUndo.IsEnabled = _undoStack.Count > 0;
        BtnRedo.IsEnabled = _redoStack.Count > 0;
    }

    private Rect GetRect()
    {
        return new Rect(
            Math.Min(_startPoint.X, _currentPoint.X),
            Math.Min(_startPoint.Y, _currentPoint.Y),
            Math.Abs(_currentPoint.X - _startPoint.X),
            Math.Abs(_currentPoint.Y - _startPoint.Y));
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            Redo();
        else if (e.Key == Key.Escape)
            { DialogResult = false; Close(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            BtnDone_Click(sender, e);
    }

}

// Simple WPF color picker popup
public class ColorPickerPopup : Window
{
    public Color SelectedColor { get; private set; }

    private static readonly Color[] PresetColors =
    [
        Colors.Red, Colors.Orange, Colors.Gold, Colors.Lime, Colors.Cyan,
        Colors.DodgerBlue, Colors.Blue, Colors.Purple, Colors.Magenta, Colors.White,
        Colors.Yellow, Colors.OrangeRed, Colors.SpringGreen, Colors.DeepSkyBlue,
        Colors.RoyalBlue, Colors.Violet, Colors.DeepPink, Colors.Black, Colors.Gray
    ];

    public ColorPickerPopup(Color initialColor)
    {
        SelectedColor = initialColor;
        Title = "Pick a color";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        var wrapPanel = new WrapPanel { Width = 200, Margin = new Thickness(8) };
        foreach (var color in PresetColors)
        {
            var border = new Border
            {
                Width = 28, Height = 28, Margin = new Thickness(3),
                Background = new SolidColorBrush(color),
                BorderBrush = color == initialColor ? Brushes.Black : Brushes.LightGray,
                BorderThickness = new Thickness(color == initialColor ? 3 : 1),
                Cursor = Cursors.Hand
            };
            border.MouseLeftButtonDown += (_, _) =>
            {
                SelectedColor = color;
                DialogResult = true;
                Close();
            };
            wrapPanel.Children.Add(border);
        }

        var cancelBtn = new Button { Content = "Cancel", Width = 60, Margin = new Thickness(8), IsCancel = true };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        cancelBtn.HorizontalAlignment = HorizontalAlignment.Center;

        var stack = new StackPanel();
        stack.Children.Add(wrapPanel);
        stack.Children.Add(cancelBtn);
        Content = stack;
    }
}

// Simple text input dialog
public class TextInputDialog : Window
{
    public string? InputText { get; private set; }

    public TextInputDialog()
    {
        Title = "Enter text";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(12) };
        var textBox = new TextBox { Width = 250, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock { Text = "Text:", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(textBox);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "OK", Width = 60, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
        okBtn.Click += (_, _) => { InputText = textBox.Text; DialogResult = true; Close(); };
        var cancelBtn = new Button { Content = "Cancel", Width = 60, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);
        Content = stack;

        Loaded += (_, _) => textBox.Focus();
    }
}
