using System.Windows;

namespace SnapGo.App.Services;

public class AnnotationService : IAnnotationService
{
    public bool CanUndo => false;
    public bool CanRedo => false;

    public void Undo()
    {
        // TODO: Implement in Step 6
        throw new NotImplementedException();
    }

    public void Redo()
    {
        // TODO: Implement in Step 6
        throw new NotImplementedException();
    }

    public Task<byte[]> ExportWithAnnotationsAsync(FrameworkElement annotatedCanvas, int width, int height)
    {
        // TODO: Implement in Step 6
        throw new NotImplementedException();
    }
}
