using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapGo.App.Helpers;
using SnapGo.App.Services;

namespace SnapGo.App.Views;

public partial class ScreenshotOverlay : Window
{
    private enum OverlayState { Idle, Dragging, Selected, Confirmed }

    private readonly IScreenshotService _screenshotService;
    private OverlayState _state = OverlayState.Idle;
    private Point _startPoint;
    private Point _currentPoint;
    private Rect _selectionRect;
    private bool _isResizing;
    private bool _isMoving;
    private Point _resizeStart;
    private Rect _resizeOriginalRect;
    private string _resizeHandle = "";

    // Visuals
    private readonly Brush _maskBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
    private readonly Brush _borderBrush = Brushes.DodgerBlue;
    private readonly Brush _handleBrush = Brushes.White;
    private readonly Pen _handleStroke = new(Brushes.DodgerBlue, 1.5);
    private readonly Pen _borderPen = new(Brushes.DodgerBlue, 2);
    private readonly DoubleCollection _dashStyle = new([6, 3]);
    private const double HandleSize = 8;

    private BitmapSource? _sourceImage;

    private Border? _toolbarUi;
    private bool _toolbarShown;

    /// <summary>Fired when the user finishes dragging a selection (mouse-up).
    /// The overlay stays open and shows the toolbar.</summary>
    public event EventHandler<SelectionReadyEventArgs>? SelectionReady;

    /// <summary>Fired when a toolbar button is clicked.</summary>
    public event EventHandler<ToolbarAction>? ToolbarActionRequested;

    public ScreenshotOverlay(IScreenshotService screenshotService)
    {
        _screenshotService = screenshotService;
        InitializeComponent();
        _borderPen.DashStyle = new DashStyle(_dashStyle, 0);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FitToVirtualScreen();
        Activate();
    }

    public void SetSourceImage(BitmapSource image)
    {
        _sourceImage = image;
        ScreenshotImage.Source = image;
        FitToVirtualScreen();
    }

    /// <summary>
    /// WindowState=Maximized inflates a borderless layered window by the resize frame
    /// (2574×1614 on a 2560×1600 screen). Stretch then resamples every pixel.
    /// Pin the client area to the virtual screen so one bitmap pixel lands on one device pixel.
    /// </summary>
    private void FitToVirtualScreen()
    {
        WindowState = WindowState.Normal;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>Physical pixels per DIP for the captured bitmap. 1 at 100% scaling.</summary>
    private double PixelScaleX =>
        _sourceImage == null || ActualWidth <= 0 ? 1 : _sourceImage.PixelWidth / ActualWidth;

    private double PixelScaleY =>
        _sourceImage == null || ActualHeight <= 0 ? 1 : _sourceImage.PixelHeight / ActualHeight;

    private PixelRect SelectionPixels => PixelRectMapper.FromDip(
        _selectionRect.X, _selectionRect.Y, _selectionRect.Width, _selectionRect.Height,
        PixelScaleX, PixelScaleY,
        _sourceImage!.PixelWidth, _sourceImage.PixelHeight);

    /// <summary>Crop the current selection and return it. Does NOT close the overlay.</summary>
    public BitmapSource CropCurrentSelection()
    {
        var px = SelectionPixels;
        return _screenshotService.CropRegion(_sourceImage!, px.X, px.Y, px.Width, px.Height);
    }

    /// <summary>Get the current selection rect in screen coordinates.</summary>
    public Rect CurrentSelectionRect => _selectionRect;

    // ============================================================
    // Drawing
    // ============================================================

    private void DrawAll()
    {
        // Preserve toolbar across redraws
        if (_toolbarUi != null)
            OverlayCanvas.Children.Remove(_toolbarUi);

        OverlayCanvas.Children.Clear();
        if (_sourceImage == null) return;

        var fullRect = new Rect(0, 0, Width, Height);
        DrawMaskWithHole(fullRect);

        if (_state is OverlayState.Dragging or OverlayState.Selected)
        {
            var r = _selectionRect;
            if (r.Width < 2 || r.Height < 2)
            {
                ReAddToolbar();
                return;
            }

            DrawGuideLines(r);

            var border = new Rectangle
            {
                Width = r.Width, Height = r.Height,
                Stroke = _borderBrush, StrokeThickness = 2,
                StrokeDashArray = _dashStyle, Fill = Brushes.Transparent
            };
            Canvas.SetLeft(border, r.X);
            Canvas.SetTop(border, r.Y);
            OverlayCanvas.Children.Add(border);

            var px = SelectionPixels;
            var sizeText = new TextBlock
            {
                Text = $"{px.Width} × {px.Height}",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                FontSize = 12, Padding = new Thickness(4, 2, 4, 2)
            };
            Canvas.SetLeft(sizeText, r.Right + 6);
            Canvas.SetTop(sizeText, r.Bottom + 4);
            OverlayCanvas.Children.Add(sizeText);

            if (_state == OverlayState.Selected)
                DrawResizeHandles(r);
        }

        ReAddToolbar();
    }

    private void ReAddToolbar()
    {
        if (_toolbarUi == null || _state != OverlayState.Selected) return;
        OverlayCanvas.Children.Add(_toolbarUi);
        PositionToolbar(_selectionRect);
    }

    private void DrawMaskWithHole(Rect fullRect)
    {
        if (_state is OverlayState.Dragging or OverlayState.Selected && _selectionRect.Width > 2)
        {
            var fullGeo = new RectangleGeometry(fullRect);
            var holeGeo = new RectangleGeometry(_selectionRect);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullGeo, holeGeo);
            OverlayCanvas.Children.Add(new System.Windows.Shapes.Path { Fill = _maskBrush, Data = combined });
        }
        else
        {
            OverlayCanvas.Children.Add(new Rectangle { Width = fullRect.Width, Height = fullRect.Height, Fill = _maskBrush });
        }
    }

    private void DrawGuideLines(Rect selRect)
    {
        var c = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        var p = new Pen(c, 1);
        AddLine(0, selRect.Top, Width, selRect.Top, p);
        AddLine(0, selRect.Bottom, Width, selRect.Bottom, p);
        AddLine(selRect.Left, 0, selRect.Left, Height, p);
        AddLine(selRect.Right, 0, selRect.Right, Height, p);
    }

    private void DrawResizeHandles(Rect r)
    {
        var positions = new (double x, double y)[]
        {
            (r.Left, r.Top), (r.Left + r.Width/2, r.Top), (r.Right, r.Top),
            (r.Left, r.Top + r.Height/2), (r.Right, r.Top + r.Height/2),
            (r.Left, r.Bottom), (r.Left + r.Width/2, r.Bottom), (r.Right, r.Bottom),
        };
        foreach (var (px, py) in positions)
        {
            var rect = new Rectangle
            {
                Width = HandleSize, Height = HandleSize,
                Fill = _handleBrush, Stroke = _handleStroke.Brush, StrokeThickness = _handleStroke.Thickness
            };
            Canvas.SetLeft(rect, px - HandleSize / 2);
            Canvas.SetTop(rect, py - HandleSize / 2);
            OverlayCanvas.Children.Add(rect);
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Pen pen)
    {
        OverlayCanvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = pen.Brush, StrokeThickness = pen.Thickness
        });
    }

    // ============================================================
    // Hit testing
    // ============================================================

    private string HitTestHandle(Point pos)
    {
        var r = _selectionRect;
        double hs = HandleSize / 2 + 4;
        if (Math.Abs(pos.X - r.Left) < hs && Math.Abs(pos.Y - r.Top) < hs) return "tl";
        if (Math.Abs(pos.X - (r.Left + r.Width / 2)) < hs && Math.Abs(pos.Y - r.Top) < hs) return "tc";
        if (Math.Abs(pos.X - r.Right) < hs && Math.Abs(pos.Y - r.Top) < hs) return "tr";
        if (Math.Abs(pos.X - r.Left) < hs && Math.Abs(pos.Y - (r.Top + r.Height / 2)) < hs) return "ml";
        if (Math.Abs(pos.X - r.Right) < hs && Math.Abs(pos.Y - (r.Top + r.Height / 2)) < hs) return "mr";
        if (Math.Abs(pos.X - r.Left) < hs && Math.Abs(pos.Y - r.Bottom) < hs) return "bl";
        if (Math.Abs(pos.X - (r.Left + r.Width / 2)) < hs && Math.Abs(pos.Y - r.Bottom) < hs) return "bc";
        if (Math.Abs(pos.X - r.Right) < hs && Math.Abs(pos.Y - r.Bottom) < hs) return "br";
        return "";
    }

    // ============================================================
    // Mouse events
    // ============================================================

    /// <summary>Check whether a WPF element is a descendant of the toolbar.</summary>
    private static bool IsInsideToolbar(DependencyObject? target, DependencyObject? current)
    {
        while (current != null)
        {
            if (current == target) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private bool IsClickOnToolbar(MouseButtonEventArgs e)
    {
        return _toolbarUi != null && IsInsideToolbar(_toolbarUi, e.OriginalSource as DependencyObject);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't intercept clicks on the inline toolbar (buttons)
        if (IsClickOnToolbar(e)) return;

        var pos = e.GetPosition(this);

        if (_state == OverlayState.Selected)
        {
            _resizeHandle = HitTestHandle(pos);
            if (_resizeHandle != "")
            {
                _isResizing = true;
                _resizeStart = pos;
                _resizeOriginalRect = _selectionRect;
                Cursor = _resizeHandle switch
                {
                    "tl" or "br" => Cursors.SizeNWSE,
                    "tr" or "bl" => Cursors.SizeNESW,
                    "tc" or "bc" => Cursors.SizeNS,
                    "ml" or "mr" => Cursors.SizeWE,
                    _ => Cursors.Cross
                };
                CaptureMouse();
                return;
            }
            if (_selectionRect.Contains(pos))
            {
                _isMoving = true;
                _resizeStart = pos;
                _resizeOriginalRect = _selectionRect;
                Cursor = Cursors.SizeAll;
                CaptureMouse();
                return;
            }
        }

        _state = OverlayState.Dragging;
        _startPoint = pos;
        _currentPoint = pos;
        _selectionRect = new Rect(pos, new Size(0, 0));
        CaptureMouse();
        DrawAll();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        // Skip if the window is already closing
        if (_state == OverlayState.Confirmed) return;

        var pos = e.GetPosition(this);

        if (_isResizing) { ResizeSelection(pos); DrawAll(); return; }
        if (_isMoving) { MoveSelection(pos); DrawAll(); return; }

        UpdateCursor(pos);

        if (_state != OverlayState.Dragging) return;

        _currentPoint = pos;
        _selectionRect = GetSelectionRect(_startPoint, _currentPoint);
        DrawAll();
    }

    private void UpdateCursor(Point pos)
    {
        if (_state == OverlayState.Selected)
        {
            var handle = HitTestHandle(pos);
            if (handle != "")
            {
                Cursor = handle switch
                {
                    "tl" or "br" => Cursors.SizeNWSE,
                    "tr" or "bl" => Cursors.SizeNESW,
                    "tc" or "bc" => Cursors.SizeNS,
                    "ml" or "mr" => Cursors.SizeWE,
                    _ => Cursors.Cross
                };
                return;
            }
            if (_selectionRect.Contains(pos))
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }
        Cursor = Cursors.Cross;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Skip if the window is already closing
        if (_state == OverlayState.Confirmed) return;

        // End resize/move operations (state stays Selected)
        if (_isResizing || _isMoving)
        {
            _isResizing = false;
            _isMoving = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Cross;
            return;
        }

        if (_state == OverlayState.Dragging)
        {
            ReleaseMouseCapture();
            var r = _selectionRect;
            if (r.Width < 5 || r.Height < 5)
            {
                _state = OverlayState.Idle;
                Close();
                return;
            }

            _state = OverlayState.Selected;
            EnsureToolbar(r);
            DrawAll();
            SelectionReady?.Invoke(this, new SelectionReadyEventArgs(r));
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_state == OverlayState.Selected)
        {
            ConfirmAndClose();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_state == OverlayState.Selected)
                    ConfirmAndClose();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    // ============================================================
    // Selection manipulation
    // ============================================================

    private static Rect GetSelectionRect(Point p1, Point p2)
    {
        var x = Math.Min(p1.X, p2.X);
        var y = Math.Min(p1.Y, p2.Y);
        return new Rect(x, y, Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));
    }

    private void MoveSelection(Point pos)
    {
        double dx = pos.X - _resizeStart.X;
        double dy = pos.Y - _resizeStart.Y;
        double newX = Math.Max(0, Math.Min(_resizeOriginalRect.X + dx, Width - _resizeOriginalRect.Width));
        double newY = Math.Max(0, Math.Min(_resizeOriginalRect.Y + dy, Height - _resizeOriginalRect.Height));
        _selectionRect = new Rect(newX, newY, _resizeOriginalRect.Width, _resizeOriginalRect.Height);
    }

    private void ResizeSelection(Point pos)
    {
        var orig = _resizeOriginalRect;
        double dx = pos.X - _resizeStart.X, dy = pos.Y - _resizeStart.Y;
        double l = orig.Left, t = orig.Top, r = orig.Right, b = orig.Bottom;
        switch (_resizeHandle)
        {
            case "tl": l += dx; t += dy; break;
            case "tc": t += dy; break;
            case "tr": r += dx; t += dy; break;
            case "ml": l += dx; break;
            case "mr": r += dx; break;
            case "bl": l += dx; b += dy; break;
            case "bc": b += dy; break;
            case "br": r += dx; b += dy; break;
        }
        if (r - l < 5) { l = orig.Left; r = orig.Right; }
        if (b - t < 5) { t = orig.Top; b = orig.Bottom; }
        l = Math.Max(0, l); t = Math.Max(0, t);
        r = Math.Min(Width, r); b = Math.Min(Height, b);
        _selectionRect = new Rect(l, t, r - l, b - t);
    }

    // ============================================================
    // Inline toolbar (inside the overlay canvas, no focus issues)
    // ============================================================

    private void EnsureToolbar(Rect selectionRect)
    {
        if (_toolbarShown) return;
        _toolbarShown = true;
        _toolbarUi = BuildToolbar();
        OverlayCanvas.Children.Add(_toolbarUi);
        PositionToolbar(selectionRect);
    }

    private Border BuildToolbar()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromArgb(0x40, 0, 0, 0),
                BlurRadius = 12,
                ShadowDepth = 3,
                Opacity = 0.5
            }
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };

        stack.Children.Add(MakeToolbarBtn("💾 Save", ToolbarAction.Save));
        stack.Children.Add(MakeToolbarBtn("📝 OCR", ToolbarAction.OCR));
        stack.Children.Add(MakeToolbarBtn("✏️ Annotate", ToolbarAction.Annotate));
        stack.Children.Add(MakeToolbarBtn("📋 Copy", ToolbarAction.Copy));

        border.Child = stack;
        return border;
    }

    private Button MakeToolbarBtn(string text, ToolbarAction action)
    {
        var btn = new Button
        {
            Content = text,
            Width = text.Contains("Annotate") ? 90 : 80,
            Height = 36,
            Margin = new Thickness(3),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) =>
        {
            // Defer so mouse event routing completes before we close the window
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ToolbarActionRequested?.Invoke(this, action);
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
        return btn;
    }

    private void PositionToolbar(Rect r)
    {
        if (_toolbarUi == null) return;

        // Measure first to get actual size
        _toolbarUi.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = _toolbarUi.DesiredSize.Width;
        var th = _toolbarUi.DesiredSize.Height;
        const double offset = 12;

        double x = r.Left + r.Width / 2 - tw / 2;
        double y = r.Bottom + offset;

        if (y + th > Height - 40)
            y = r.Top - th - offset;
        if (x < 8) x = 8;
        if (x + tw > Width - 8) x = Width - tw - 8;
        if (y < 8) y = 8;

        Canvas.SetLeft(_toolbarUi, x);
        Canvas.SetTop(_toolbarUi, y);
    }

    // ============================================================
    // Confirm
    // ============================================================

    /// <summary>Crop, notify, and close. Called by MainWindow when a toolbar action is selected.</summary>
    public void ConfirmAndClose()
    {
        if (_selectionRect.Width < 5 || _selectionRect.Height < 5) return;
        _state = OverlayState.Confirmed;

        var cropped = CropCurrentSelection();
        var px = SelectionPixels;
        _screenshotService.NotifyScreenshotCaptured(cropped, px.X, px.Y, px.Width, px.Height);
        Close();
    }
}

public class SelectionReadyEventArgs : EventArgs
{
    public Rect SelectionRect { get; }
    public SelectionReadyEventArgs(Rect rect) => SelectionRect = rect;
}
