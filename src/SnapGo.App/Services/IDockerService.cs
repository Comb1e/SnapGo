namespace SnapGo.App.Services;

public enum ContainerStatus { NotFound, Running, Stopped }

public interface IDockerService
{
    /// <summary>Get the current status of the PaddleOCR-VL container.</summary>
    Task<ContainerStatus> GetContainerStatusAsync(CancellationToken ct = default);

    /// <summary>Ensure the container is running; start it if needed.</summary>
    Task EnsureRunningAsync(CancellationToken ct = default);

    /// <summary>Stop the container gracefully.</summary>
    Task StopAsync(CancellationToken ct = default);
}
