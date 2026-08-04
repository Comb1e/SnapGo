namespace SnapGo.App.Models;

public class AppSettings
{
    public GeneralSettings General { get; init; } = new();
    public HotkeySettings Hotkey { get; init; } = new();
    public DockerSettings Docker { get; init; } = new();
    public OcrSettings Ocr { get; init; } = new();
    public HistorySettings History { get; init; } = new();
    public StorageSettings Storage { get; init; } = new();
    public LoggingSettings Logging { get; init; } = new();
}

public class GeneralSettings
{
    public bool AutoStart { get; init; } = false;
    public bool StartMinimized { get; init; } = true;
}

public class HotkeySettings
{
    public string Modifiers { get; init; } = "Control+Shift";
    public string Key { get; init; } = "S";
}

public class DockerSettings
{
    public string ContainerName { get; init; } = "snapgo-ocr";
    public string BaseUrl { get; init; } = "http://localhost:8000";
    public string HealthCheckEndpoint { get; init; } = "/health";
    public string OcrEndpoint { get; init; } = "/layout-parsing";
    public string ModelName { get; init; } = "PaddleOCR-VL-1.5";
    public string Mode { get; init; } = "Smart";
    public int IdleTimeoutMinutes { get; init; } = 30;
    public int HealthCheckIntervalMs { get; init; } = 500;
    public int HealthCheckMaxRetries { get; init; } = 30;
}

public class OcrSettings
{
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 3;
    public string Prompt { get; init; } = "请识别图片中的所有文字，保持原有排版结构，不要添加额外解释。";
}

public class HistorySettings
{
    public int MaxRecords { get; init; } = 500;
    public int MaxDatabaseSizeMb { get; init; } = 200;
    public int ThumbnailWidth { get; init; } = 200;
    public int ThumbnailHeight { get; init; } = 150;
}

public class StorageSettings
{
    public string DefaultSavePath { get; init; } = "";
}

public class LoggingSettings
{
    public string LogLevel { get; init; } = "Information";
    public string LogFilePath { get; init; } = "logs/snapgo-.log";
}
