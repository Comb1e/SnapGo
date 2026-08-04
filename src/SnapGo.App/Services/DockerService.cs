using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

public class DockerService : IDockerService
{
    private readonly DockerSettings _config;
    private readonly ILogger<DockerService> _logger;
    private readonly HttpClient _httpClient;
    private DateTime _lastOcrTime = DateTime.MinValue;
    private Timer? _idleTimer;

    public event EventHandler<DockerStatusChangedEventArgs>? StatusChanged;

    public DockerService(IOptions<AppSettings> options, ILogger<DockerService> logger, HttpClient httpClient)
    {
        _config = options.Value.Docker;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ContainerStatus> GetContainerStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await RunDockerCommandAsync(
                $"ps -a --filter \"name={_config.ContainerName}\" --format \"{{{{.Status}}}}\"",
                ct);

            if (string.IsNullOrWhiteSpace(result))
                return ContainerStatus.NotFound;

            if (result.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
                return ContainerStatus.Running;

            return ContainerStatus.Stopped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Docker container status");
            return ContainerStatus.NotFound;
        }
    }

    public async Task EnsureRunningAsync(CancellationToken ct = default)
    {
        var status = await GetContainerStatusAsync(ct);

        switch (status)
        {
            case ContainerStatus.Running:
                _lastOcrTime = DateTime.UtcNow;
                return;

            case ContainerStatus.Stopped:
                _logger.LogInformation("Starting Docker container {Name}...", _config.ContainerName);
                NotifyStatus("Starting container...");
                await StartContainerAsync(ct);
                await WaitForHealthyAsync(ct);
                _lastOcrTime = DateTime.UtcNow;
                NotifyStatus("Container ready");
                break;

            case ContainerStatus.NotFound:
                throw new DockerNotSetupException(
                    $"Docker container '{_config.ContainerName}' not found.\n\n" +
                    "Please create it from the docker/ directory:\n" +
                    "  cd docker\n" +
                    "  docker compose up -d\n\n" +
                    "See docker/docker-compose.yml for details.");
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var status = await GetContainerStatusAsync(ct);
        if (status != ContainerStatus.Running) return;

        _logger.LogInformation("Stopping Docker container {Name}...", _config.ContainerName);
        await RunDockerCommandAsync($"stop -t 10 {_config.ContainerName}", ct);
        NotifyStatus("Container stopped");
    }

    public void StartIdleMonitor()
    {
        if (_config.Mode != "OnDemand" && _config.Mode != "Smart") return;

        var interval = TimeSpan.FromSeconds(30);
        _idleTimer = new Timer(async _ =>
        {
            var idleTime = DateTime.UtcNow - _lastOcrTime;
            if (idleTime > TimeSpan.FromMinutes(_config.IdleTimeoutMinutes))
            {
                var status = await GetContainerStatusAsync();
                if (status == ContainerStatus.Running)
                {
                    _logger.LogInformation("Auto-stopping container after {Minutes}min idle",
                        _config.IdleTimeoutMinutes);
                    await StopAsync();
                }
            }
        }, null, interval, interval);
    }

    private async Task StartContainerAsync(CancellationToken ct)
    {
        await RunDockerCommandAsync($"start {_config.ContainerName}", ct);
        NotifyStatus("Container started, waiting for model...");
    }

    private async Task WaitForHealthyAsync(CancellationToken ct)
    {
        var maxRetries = _config.HealthCheckMaxRetries;
        var interval = _config.HealthCheckIntervalMs;

        for (var i = 0; i < maxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (await IsHealthyAsync(ct))
            {
                _logger.LogInformation("PaddleOCR-VL is healthy after {Attempts} checks", i + 1);
                return;
            }

            await Task.Delay(interval, ct);
        }

        throw new TimeoutException(
            $"PaddleOCR-VL did not become healthy within {maxRetries * interval / 1000}s");
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var response = await _httpClient.GetAsync(
                $"{_config.BaseUrl}{_config.HealthCheckEndpoint}", cts.Token);

            if (!response.IsSuccessStatusCode) return false;

            var body = await response.Content.ReadAsStringAsync(ct);
            // PaddleX health endpoint returns {"errorCode":0,"errorMsg":"Healthy"}
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("errorCode", out var code)
                && code.GetInt32() == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> RunDockerCommandAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException($"docker {arguments} failed: {error.Trim()}");
        }

        return output.Trim();
    }

    private void NotifyStatus(string message)
    {
        StatusChanged?.Invoke(this, new DockerStatusChangedEventArgs(message));
        _logger.LogInformation("Docker status: {Message}", message);
    }
}

public class DockerNotSetupException : Exception
{
    public DockerNotSetupException(string message) : base(message) { }
}

public class DockerStatusChangedEventArgs : EventArgs
{
    public string Message { get; }
    public DockerStatusChangedEventArgs(string message) => Message = message;
}
