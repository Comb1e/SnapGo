namespace SnapGo.App.Models;

public class AppSettings
{
    public GeneralSettings General { get; init; } = new();
    public HotkeySettings Hotkey { get; init; } = new();
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

public class OcrSettings
{
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Auto tries DirectML, then CPU. CPU and DirectML force that provider.</summary>
    public string Provider { get; init; } = "Auto";

    public string DetModel { get; init; } = "models/ch_PP-OCRv5_det_mobile.onnx";
    public string ClsModel { get; init; } = "models/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    public string RecModel { get; init; } = "models/ch_PP-OCRv5_rec_mobile.onnx";
    public string Dict { get; init; } = "models/ppocrv5_dict.txt";

    /// <summary>Screenshot text is upright. The classifier is loaded because the engine requires it, and this skips running it.</summary>
    public bool DoAngle { get; init; } = false;
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
