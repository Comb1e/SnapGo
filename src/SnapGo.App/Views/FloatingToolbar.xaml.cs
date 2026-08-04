using System.Windows;

namespace SnapGo.App.Views;

public partial class FloatingToolbar : Window
{
    public event EventHandler<ToolbarAction>? ActionSelected;

    public FloatingToolbar()
    {
        InitializeComponent();
    }

    public void PositionNear(Rect selectionRect, double screenWidth, double screenHeight)
    {
        const double offset = 12;
        var toolbarWidth = ActualWidth > 0 ? ActualWidth : 360;
        var toolbarHeight = ActualHeight > 0 ? ActualHeight : 48;

        double x = selectionRect.Left + selectionRect.Width / 2 - toolbarWidth / 2;
        double y = selectionRect.Bottom + offset;

        if (y + toolbarHeight > screenHeight - 40)
            y = selectionRect.Top - toolbarHeight - offset;
        if (x < 8) x = 8;
        if (x + toolbarWidth > screenWidth - 8) x = screenWidth - toolbarWidth - 8;
        if (y < 8) y = 8;

        Left = x;
        Top = y;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ToolbarAction.Save);
        Close();
    }

    private void BtnOcr_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ToolbarAction.OCR);
        Close();
    }

    private void BtnAnnotate_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ToolbarAction.Annotate);
        Close();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ToolbarAction.Copy);
        Close();
    }

}

public enum ToolbarAction
{
    Save,
    OCR,
    Annotate,
    Copy,
    Cancel
}
