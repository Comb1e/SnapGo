# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the main app
dotnet build src/SnapGo.App

# Build with Release config
dotnet build src/SnapGo.App -c Release

# Run the app (debug)
dotnet run --project src/SnapGo.App

# Convert PNG to ICO (re-run after changing icon)
dotnet run --project tools/ConvertIcon -- "src/SnapGo.App/icon/SnapGo.png" "src/SnapGo.App/icon/SnapGo.ico"

# If build fails with file-locked error, SnapGo.exe is still running in the tray — kill it first:
powershell -Command "Stop-Process -Name SnapGo -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1"
```

**Output:** `src/SnapGo.App/bin/Debug/net8.0-windows/SnapGo.exe` (assembly name is `SnapGo`, not `SnapGo.App`).

**.NET SDK:** 8.0 (targets `net8.0-windows`). Install via `winget install Microsoft.DotNet.SDK.8` if missing.

## Architecture

SnapGo is a **single-instance WPF tray app** for full-screen box-select screenshots with local Docker-based OCR.

### Screenshot Flow (the critical path)

```
Ctrl+Shift+S → MainWindow.OnHotkeyPressed()
  → ScreenshotService.CaptureFullScreenAsync()  (Graphics.CopyFromScreen)
  → ScreenshotOverlay.ShowDialog()              (full-screen transparent window, drag-to-select)
      → mouse-up: enters Selected state (handles visible, overlay stays open)
      → fires SelectionReady event
  → MainWindow shows FloatingToolbar near selection
  → User clicks toolbar button (Save/OCR/Annotate/Copy)
  → MainWindow.OnToolbarAction()
      → overlay.CropCurrentSelection()          (crops current rect without closing)
      → overlay.ConfirmAndClose()               (fires ScreenshotCaptured, closes overlay)
      → executes action (Save dialog, OCR, clipboard, etc.)
```

Key architectural decisions:
- **ScreenshotOverlay remains open** after mouse-up — user can resize/move the selection before acting.
- **FloatingToolbar is pure UI** — it only fires `ActionSelected` events. All business logic (save, clipboard, OCR orchestration) lives in `MainWindow`.
- **No ScreenshotOverlay reuse** — each hotkey press creates a fresh `ScreenshotOverlay` (Transient DI).

### DI Container (Generic Host)

`App.xaml.cs` wires a `Microsoft.Extensions.Hosting` generic host. Services registered as:
- **Singleton**: `IScreenshotService`, `IOcrService`, `IDockerService`, `IClipboardService`, `IHistoryService`, `IAnnotationService`, `HttpClient`
- **Transient**: All ViewModels, all Views (MainWindow, ScreenshotOverlay, FloatingToolbar, ResultPanel, SettingsWindow)

Access services from anywhere: `((App)Application.Current).Services.GetRequiredService<T>()`

### Single-Instance Detection

`App.OnStartup` creates a named `Mutex("Global\\SnapGo_SingleInstance")`. If already held, finds the existing window via `FindWindow("SnapGo")`, restores it, and shuts down the new instance.

### Hotkey

`Ctrl+Shift+S` via `RegisterHotKey` P/Invoke (`user32.dll`). Lifetime managed by `HotkeyHelper` which hooks the MainWindow's `HwndSource`. The hotkey is registered once in `MainWindow.OnLoaded` and only unregistered at shutdown. **Do NOT dispose `_hwndSource` in `HotkeyHelper.Dispose()` — it belongs to WPF**, only call `RemoveHook` and `UnregisterHotKey`.

Re-entrance is guarded by `_viewModel.IsCapturing`.

### Tray Icon

`TaskbarIcon` (H.NotifyIcon.Wpf 2.1.0) defined in `MainWindow.xaml` XAML for proper visual-tree integration. Icon loaded in `OnLoaded` from `icon/SnapGo.ico`. Right-click: Settings / Exit. Left-click: screenshot.

The main window starts with `ShowInTaskbar=False` and `WindowState=Minimized` when `StartMinimized=true`. The `Window_Closing` handler cancels close and hides to tray instead.

### ICO Format

`tools/ConvertIcon` generates the `.ico` from `SnapGo.png`. Uses **DIB format with AND mask** (not PNG-compressed) because `System.Drawing.Icon` has limited PNG-in-ICO support. `biHeight = 2× image height` (XOR + AND mask). Sizes: 256, 64, 48, 32, 16.

### Docker / OCR

**Two-container architecture:** The PaddleOCR-VL image (`paddleocr-vl:latest-nvidia-gpu-offline`) is a **pipeline orchestrator** — it does layout detection locally, then delegates text recognition to a model backend. The default config uses `vllm-server` backend (requires separate vLLM container), but `docker/pipeline_config.yaml` overrides this to `backend: native` for local GPU inference with no external dependencies.

- **Docker Compose** (`docker/docker-compose.yml`): Maps `8000:8080` (PaddleX listens on 8080), mounts `pipeline_config.yaml` with `backend: native`, starts with `paddlex --serve --pipeline /home/paddleocr/pipeline_config_native.yaml --port 8080`.
- **Health check**: `GET /health` → `{"errorCode":0,"errorMsg":"Healthy"}`
- **OCR endpoint**: `POST /layout-parsing` → `{file: "<base64>", fileType: 1}` → returns `{result: {layoutParsingResults: [{markdown: {text: "..."}}]}}`
- **`DockerService`** manages the `snapgo-ocr` container via `Process.Start("docker ...")` CLI. `EnsureRunningAsync()` polls `/health` (500ms interval, 30 retries max). Health check parses `errorCode == 0`.
- **`OcrService`** sends PaddleX-format requests, parses `layoutParsingResults[].markdown.text` from response. Exponential backoff retry (1s/2s/4s).

**Diagnostic script**: `bash test/test_ocr.sh` — tests container → health → logs → pipeline config → OCR endpoint (5 layers).

### Configuration

`appsettings.json` — reloadOnChange enabled. `AppSettings` model bound via `IOptions<T>`. Storage defaults to `.\save` (relative, resolved to `AppContext.BaseDirectory\save`).

### Known Pitfalls

- **Build lock**: The app stays alive in the tray after closing the window. Kill `SnapGo.exe` before rebuilding.
- **`Dispatcher.InvokeAsync(async () => ...)`**: The `async` lambda is inferred as `async void`. If any inner `await` yields, the outer `await` completes early. Prefer sync dispatcher calls.
- **H.NotifyIcon + hidden parent**: The `TaskbarIcon` must be in the XAML visual tree, not created in code-behind, or it disappears when the parent window is hidden.
- **`FloatingToolbar.Window_Deactivated`**: Guard with `_closedByAction` flag — otherwise the toolbar closes itself when any child dialog (e.g., SaveFileDialog) takes focus.
