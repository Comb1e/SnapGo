using System.Windows;

namespace SnapGo.App.Services;

public interface IAnnotationService
{
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }
    Task<byte[]> ExportWithAnnotationsAsync(FrameworkElement annotatedCanvas, int width, int height);
}
