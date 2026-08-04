using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnapGo.App.Helpers;
using SnapGo.App.Models;

namespace SnapGo.App.Services;

public class OcrService : IOcrService
{
    private readonly HttpClient _httpClient;
    private readonly IDockerService _dockerService;
    private readonly DockerSettings _dockerConfig;
    private readonly OcrSettings _ocrConfig;
    private readonly ILogger<OcrService> _logger;

    public OcrService(
        HttpClient httpClient,
        IDockerService dockerService,
        IOptions<AppSettings> options,
        ILogger<OcrService> logger)
    {
        _httpClient = httpClient;
        _dockerService = dockerService;
        _dockerConfig = options.Value.Docker;
        _ocrConfig = options.Value.Ocr;
        _logger = logger;
    }

    public async Task<OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Ensure Docker is running
        try
        {
            await _dockerService.EnsureRunningAsync(cancellationToken);
        }
        catch (DockerNotSetupException)
        {
            throw; // Re-throw with helpful message
        }

        // Encode image to raw base64 (no data URI prefix — PaddleX expects raw base64)
        var pngBytes = ImageHelper.ToPngBytes(image);
        var base64 = Convert.ToBase64String(pngBytes);

        // Build PaddleX layout-parsing request
        var requestBody = new
        {
            file = base64,
            fileType = 1 // 1 = image, 0 = PDF
        };

        var json = JsonSerializer.Serialize(requestBody);
        _logger.LogInformation("Sending OCR request ({Size}KB image)", pngBytes.Length / 1024);

        // Retry with exponential backoff
        Exception? lastException = null;
        for (var attempt = 0; attempt < _ocrConfig.MaxRetries; attempt++)
        {
            try
            {
                var text = await SendRequestAsync(json, cancellationToken);
                sw.Stop();

                return new OcrResult
                {
                    Text = text,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    ModelUsed = _dockerConfig.ModelName,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout — retry
                lastException = new TimeoutException("OCR request timed out");
                _logger.LogWarning("OCR attempt {Attempt} timed out", attempt + 1);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "OCR attempt {Attempt} failed", attempt + 1);
            }

            if (attempt < _ocrConfig.MaxRetries - 1)
            {
                var delay = (int)Math.Pow(2, attempt) * 1000; // 1s, 2s, 4s
                await Task.Delay(delay, cancellationToken);
            }
        }

        sw.Stop();
        throw new OcrException(
            $"OCR failed after {_ocrConfig.MaxRetries} attempts. " +
            $"Please ensure the Docker OCR service is running and responsive.",
            lastException);
    }

    private async Task<string> SendRequestAsync(string json, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_ocrConfig.TimeoutSeconds));

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            $"{_dockerConfig.BaseUrl}{_dockerConfig.OcrEndpoint}",
            content,
            cts.Token);

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(responseBody);

        // PaddleX layout-parsing response:
        // {result: {layoutParsingResults: [{markdown: {text: "..."}}]}, errorCode: 0}
        var results = doc.RootElement
            .GetProperty("result")
            .GetProperty("layoutParsingResults");

        var texts = new List<string>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.TryGetProperty("markdown", out var markdown)
                && markdown.TryGetProperty("text", out var text))
            {
                var t = text.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                    texts.Add(t);
            }
        }

        return string.Join("\n", texts).Trim();
    }
}

public class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}
