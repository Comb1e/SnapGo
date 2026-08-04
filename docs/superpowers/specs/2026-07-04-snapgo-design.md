# SnapGo — Design Specification

**Date:** 2026-07-04  
**Status:** Approved  
**Stack:** C# WPF (.NET 9) + Docker PaddleOCR-VL + vLLM

---

## 1. Overview

SnapGo is a Windows desktop tool for region-screenshot of the foreground window, with optional OCR text extraction via a local Docker-deployed PaddleOCR-VL service.

### Core Features (v1)

1. **Box-select screenshot** — global hotkey `Ctrl+Shift+S` triggers full-screen overlay, drag to select region on the foreground window
2. **Save image** — save cropped screenshot as PNG to user-chosen path
3. **Extract text** — send screenshot to local PaddleOCR-VL Docker service via OpenAI-compatible API
4. **Annotate** — draw rectangles, arrows, text, freehand on screenshot before save/copy
5. **Clipboard** — copy image or recognized text to system clipboard
6. **History** — browse/search past screenshots with thumbnails, stored in SQLite

### Future Extensibility

- Plugin architecture for additional post-processing actions
- Auto-upload to cloud storage
- Batch OCR processing

---

## 2. Architecture

```
┌─────────────────────────────────────────────────┐
│                  WPF Desktop App                  │
│                                                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │ Screenshot│ │Annotate  │ │ OCR / Docker Mgr │ │
│  │ Hotkey    │ │ Rect/Arrow│ │ HttpClient      │ │
│  │ Overlay   │ │ Text/Pen  │ │ Container Mgmt  │ │
│  └──────────┘ └──────────┘ └──────────────────┘ │
│                                                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │ History  │ │Clipboard │ │ Settings          │ │
│  │ SQLite   │ │ Image/Text│ │ Docker mode, path│ │
│  │ Thumbnail│ │          │ │ Hotkey config     │ │
│  └──────────┘ └──────────┘ └──────────────────┘ │
└──────────────────────┬──────────────────────────┘
                       │ HTTP (OpenAI-compatible)
┌──────────────────────┴──────────────────────────┐
│      Docker: PaddleOCR-VL + vLLM (GPU)          │
│      localhost:8000/v1/chat/completions          │
└─────────────────────────────────────────────────┘
```

### Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| UI Framework | WPF | Native Windows, best perf, lowest memory (~50MB) |
| MVVM Toolkit | CommunityToolkit.Mvvm 8.x | Source generators reduce boilerplate |
| Database | SQLite via Microsoft.Data.Sqlite | Zero-config, single file, ADO.NET for minimal overhead |
| OCR Backend | PaddleOCR-VL-0.9B via vLLM | User already installed offline; 109-language VLM with layout understanding |
| Docker Interface | `Process.Start("docker ...")` + CLI | Avoids Docker.DotNet SDK (~15 transitive deps, ~8MB) |
| HTTP Client | `System.Net.Http.HttpClient` | Built-in, no extra dependency |
| Global Hotkey | P/Invoke `user32.dll` `RegisterHotKey` | ~30 lines, no third-party lib needed |

---

## 3. PaddleOCR-VL Docker Service

### Model Profile

| Metric | Value |
|--------|-------|
| Parameters | 0.9B |
| Architecture | NaViT vision encoder + ERNIE-4.5-0.3B decoder |
| Model size | ~936MB (BF16) |
| API protocol | OpenAI-compatible `/v1/chat/completions` |
| GPU VRAM (FP16) | ~2.4GB |
| GPU VRAM (INT8+TRT) | ~1.6GB |
| Inference latency (GPU) | 0.3–1.5s per screenshot |
| Cold start | 3–8s |

### API Contract

**Request:**
```http
POST /v1/chat/completions
Content-Type: application/json

{
  "model": "PaddleOCR-VL-1.5",
  "messages": [{
    "role": "user",
    "content": [
      {"type": "text", "text": "请识别图片中的所有文字，保持原有排版结构，不要添加额外解释。"},
      {"type": "image_url", "image_url": {"url": "data:image/png;base64,..."}}
    ]
  }],
  "temperature": 0.1,
  "max_tokens": 4096
}
```

**Response (relevant subset):**
```json
{
  "choices": [{
    "message": {
      "content": "识别出的完整文本..."
    }
  }]
}
```

**Health Check:** `GET /v1/models` → confirm model id contains "PaddleOCR-VL"

### Docker Management Modes

| Mode | Behavior | Hot Latency | Cold Latency | Memory |
|------|----------|------------|--------------|--------|
| Always-on | Container runs continuously | 0.3–1.5s | — | GPU VRAM always occupied |
| On-demand | Start on first OCR, stop after N idle minutes | 0.3–1.5s | +3–8s | Released when idle |
| Smart (default) | Always-on, auto-stop after 30min idle | 0.3–1.5s | +3–8s (rare) | Released after idle |

---

## 4. WPF Application Design

### Solution Structure

```
SnapGo/
├── SnapGo.sln
├── src/
│   ├── SnapGo.App/               # Main WPF application
│   │   ├── App.xaml/.cs           # Entry, DI, global exception handler
│   │   ├── appsettings.json       # Docker URL, hotkey, paths
│   │   ├── Views/                 # 6 windows/controls
│   │   ├── ViewModels/            # 6 ViewModels
│   │   ├── Services/              # 6 service interfaces + impls
│   │   ├── Models/                # 5 entity types
│   │   ├── Data/AppDbContext.cs   # SQLite context
│   │   ├── Helpers/               # P/Invoke, image, JSON
│   │   └── Converters/            # Value converters
│   └── docker/
│       └── docker-compose.yml     # PaddleOCR-VL service definition
└── tests/
    └── SnapGo.Tests/
```

### NuGet Dependencies (8 packages)

| Package | Purpose |
|---------|---------|
| `CommunityToolkit.Mvvm` 8.x | MVVM source generators |
| `Microsoft.Data.Sqlite` 9.x | SQLite ADO.NET |
| `Microsoft.Extensions.DependencyInjection` 9.x | DI container |
| `Microsoft.Extensions.Hosting` 9.x | HostedService lifecycle |
| `System.Drawing.Common` 9.x | Screenshot capture (`CopyFromScreen`) |
| `Serilog.Extensions.Hosting` 9.x | Structured logging |
| `Serilog.Sinks.File` 6.x | Log to file |
| `Hardcodet.NotifyIcon.Wpf` 2.x | System tray icon |

### Core Modules

#### 4.1 ScreenshotService

`ScreenshotService` handles the full capture pipeline:
- **Global hotkey**: `RegisterHotKey` via `user32.dll`, listen `WM_HOTKEY` (0x0312) on `HwndSource`
- **Capture**: `Graphics.CopyFromScreen` → `Bitmap` → WPF `BitmapSource` via `MemoryStream(PNG)`
- **Overlay**: Full-screen `Window` (Topmost, transparent, semi-opaque black mask), "hole punch" effect via `CombinedGeometry(Exclude)` on selected region
- **Interaction state machine**: `Idle → Dragging → Selected → Confirmed`

#### 4.2 OcrService

- **Encode**: `BitmapSource` → `PngBitmapEncoder` → `byte[]` → Base64 string
- **Request**: OpenAI-compatible JSON with image data URI + Chinese OCR prompt
- **Timeout**: 30s per request, exponential backoff retry (1s, 2s) max 3 attempts
- **Error handling**: Connection refused → auto-start Docker; timeout → suggest retry; 5xx → show error

#### 4.3 DockerService

- **Status check**: `docker ps -a --filter "name=snapgo-ocr" --format "{{.Status}}"`
- **Start**: `docker start snapgo-ocr`, poll `/v1/models` every 500ms (max 15s)
- **Stop**: `docker stop -t 10 snapgo-ocr` (graceful, 10s timeout)
- **Idle auto-stop**: Background timer checks `_lastOcrTime`, stops if idle > threshold
- **First-run guidance**: If container doesn't exist, show setup instructions in UI

#### 4.4 AnnotationService

Four annotation types rendered on a `Canvas` overlay above the screenshot:
- **Rectangle**: Drag to draw, `Border` control with stroke/color
- **Arrow**: Drag endpoint, `Polyline` with arrowhead at terminal
- **Text**: Click to place, inline `TextBox` for input
- **Freehand**: Mouse-down drag, `Polyline` from collected `Point` list

Undo/redo via dual-stack `Stack<AnnotationAction>` (Command pattern). Export: `RenderTargetBitmap` merges canvas + image.

#### 4.5 HistoryService

- **Schema**: Single table `HistoryItems` (Id, ImageData BLOB, Thumbnail BLOB, OcrText, Annotations JSON, SourceWindow, CreatedAt, FilePath)
- **Thumbnail**: `TransformedBitmap` scale to 200×150, ~5–15KB PNG
- **Storage cap**: Max 500 records / 200MB; auto-prune oldest on startup
- **Search**: `LIKE '%keyword%'` on OcrText and SourceWindow columns

#### 4.6 ClipboardService

- `CopyImage`: PNG via `Clipboard.SetData("PNG", stream)` + `Clipboard.SetImage()` for DIB fallback
- `CopyText`: `Clipboard.SetText(text, TextDataFormat.UnicodeText)`
- Must run on STA thread; catch `ExternalException` for clipboard contention

### Data Flow: Screenshot → OCR

```
Ctrl+Shift+S
  → HotkeyHelper (WM_HOTKEY)
  → ScreenshotService.CaptureForegroundWindow()
  → ScreenshotOverlay.Show() with full-screen mask
  → User drag-selects region
  → Floating toolbar: [Save] [OCR] [Annotate] [Copy]
  → [OCR] clicked:
      → DockerService.EnsureRunningAsync()  // check + start if needed
      → OcrService.RecognizeAsync(bitmap)
           → PNG encode → Base64
           → HttpClient.PostAsync("localhost:8000/v1/chat/completions", json)
           → Parse choices[0].message.content
      → ResultPanel shows text
      → HistoryService.AddAsync(item)  // auto-save to history
```

### Error Handling Strategy

| Error Scenario | Handling |
|---------------|----------|
| Docker container not found | Show first-run setup dialog with `docker run` command |
| Docker not running | Auto `docker start`, show loading spinner during cold start |
| OCR timeout (30s) | Show error toast with retry button |
| OCR returns garbage/noise | Show result but flag low confidence; let user judge |
| Clipboard busy | Catch `ExternalException`, show "retry" toast |
| Disk full (history save) | Log warning, continue; history record skipped |
| Hotkey conflict | Show settings dialog on first conflict detection |

### Database Schema

```sql
CREATE TABLE HistoryItems (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    ImageData     BLOB    NOT NULL,
    Thumbnail     BLOB,
    OcrText       TEXT,
    Annotations   TEXT,       -- JSON
    SourceWindow  TEXT,
    CreatedAt     TEXT    NOT NULL,  -- ISO 8601
    FilePath      TEXT
);
CREATE INDEX IX_HistoryItems_CreatedAt ON HistoryItems(CreatedAt DESC);
```

---

## 5. Performance Summary

| Operation | Latency | Notes |
|-----------|---------|-------|
| Screenshot capture | <50ms | `CopyFromScreen` on single monitor |
| Overlay render | <10ms | `CombinedGeometry` compositing |
| Annotation render | <10ms | WPF retained-mode Canvas |
| OCR (hot, GPU) | 0.3–1.5s | PaddleOCR-VL via vLLM |
| OCR (cold start) | +3–8s | Container start + model load |
| History search | <50ms | Indexed SQLite, 500-row dataset |
| Thumbnail generation | <20ms | `TransformedBitmap` scaling |

| Resource | Idle | Active OCR |
|----------|------|-----------|
| WPF memory | ~50MB | ~80MB |
| Docker GPU VRAM | 0 (on-demand) / ~2.4GB (always-on) | ~2.4GB |
| Docker CPU | 0% | Spike during inference |
| Disk (app) | ~50MB | + SQLite (max ~200MB) |
| Disk (Docker image) | ~3.5GB | — |

---

## 6. Verification Plan

### Manual Tests

1. **Screenshot flow**: Press `Ctrl+Shift+S` → verify overlay appears → drag region → confirm → toolbar appears
2. **Save**: Click Save → verify PNG saved at correct resolution, correct crop area
3. **OCR**: Click OCR → verify text appears in result panel → verify accuracy on Chinese/English/mixed content
4. **Annotate**: Draw rectangle, arrow, text, freehand → undo/redo → export with annotations baked in
5. **Clipboard**: Copy image → paste into Paint → verify; Copy text → paste into Notepad → verify
6. **History**: Take 3+ screenshots → verify thumbnails visible → search by keyword → delete one → verify pruned
7. **Docker lifecycle**: Stop container → click OCR → verify auto-start → wait idle timeout → verify auto-stop
8. **Error handling**: Stop Docker entirely → click OCR → verify user-friendly error message with setup instructions
9. **Settings**: Change hotkey → verify new hotkey works; change Docker mode → verify behavior matches
10. **Tray**: Minimize to tray → hotkey still works → right-click tray → restore/exit
