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

SnapGo is a **single-instance WPF tray app** for full-screen box-select screenshots with in-process PP-OCRv5 OCR.

### Screenshot Flow (the critical path)

```
Ctrl+Shift+S → MainWindow.OnHotkeyPressed()
  → ScreenshotService.CaptureFullScreenAsync()  (physical pixels via per-monitor DPI + CopyFromScreen)
  → ScreenshotOverlay.ShowDialog()              (virtual-screen window sized 1:1, drag-to-select)
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
- **Singleton**: `IScreenshotService`, `IOcrService`, `IClipboardService`, `IHistoryService`, `IAnnotationService`
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

### OCR

`OcrService` runs PP-OCRv5 mobile in-process through RapidOcrNet. Models live in `src/SnapGo.App/ocr-models/` and are copied to `models/` beside the exe. The first OCR call loads the session; later calls reuse it. `Provider=Auto` tries DirectML and falls back to CPU. Screenshot text is upright, so angle classification is not run. Lines are ordered by `OcrReadingOrder` (overlap must be at least half the taller box).

The official DirectML package stops at ONNX Runtime 1.24.4, and newer RapidOcrNet releases require 1.27+. SnapGo pins RapidOcrNet 2.0.0 with `Microsoft.ML.OnnxRuntime.DirectML` 1.24.3 so the GPU provider actually ships. The CPU `Microsoft.ML.OnnxRuntime` package is referenced with `ExcludeAssets=runtime` so its `onnxruntime.dll` does not replace the DirectML build.

### Configuration

`appsettings.json` — reloadOnChange enabled. `AppSettings` model bound via `IOptions<T>`. Storage defaults to `.\save` (relative, resolved to `AppContext.BaseDirectory\save`).

### Known Pitfalls

- **Build lock**: The app stays alive in the tray after closing the window. Kill `SnapGo.exe` before rebuilding.
- **`Dispatcher.InvokeAsync(async () => ...)`**: The `async` lambda is inferred as `async void`. If any inner `await` yields, the outer `await` completes early. Prefer sync dispatcher calls.
- **H.NotifyIcon + hidden parent**: The `TaskbarIcon` must be in the XAML visual tree, not created in code-behind, or it disappears when the parent window is hidden.
- **`FloatingToolbar.Window_Deactivated`**: Guard with `_closedByAction` flag — otherwise the toolbar closes itself when any child dialog (e.g., SaveFileDialog) takes focus.
- **Screenshot resolution**: Do not use `WindowState=Maximized` on the transparent overlay. On a 2560×1600 screen that window is 2574×1614 at (-8, -8), and `Stretch=Fill` resamples the capture so it looks softer than the display. Size the overlay to the virtual screen and capture physical pixels (`GetSystemMetrics` under per-monitor DPI), not `SystemParameters` DIPs. Crop with `CopyPixels`, not `RenderTargetBitmap`.
- **OCR native libraries**: `PublishSingleFile` must not swallow `onnxruntime.dll`, `DirectML.dll`, or SkiaSharp. They are excluded from the bundle and have to sit beside the exe. The angle-classifier ONNX is loaded at init because RapidOcrNet requires the file; `DoAngle=false` is what skips the pass.
