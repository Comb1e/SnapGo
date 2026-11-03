# SnapGo

Windows 桌面截图与 OCR 文字提取工具。框选截图 → 保存 / 标注 / 提取文字，一键完成。

## 功能特性

| 功能 | 说明 |
|------|------|
| 🔍 **框选截图** | 全局热键 `Ctrl+Shift+S` 唤起全屏遮罩，拖拽框选区域 |
| 📝 **OCR 文字提取** | 本机 PP-OCRv5，识别截图中的中文和英文，无需 Docker |
| ✏️ **截图标注** | 矩形 / 箭头 / 文字 / 自由画笔，可撤销重做 |
| 📋 **剪贴板** | 一键复制截图或识别文字到系统剪贴板 |
| 📂 **历史记录** | SQLite 持久化存储，缩略图浏览 + 全文搜索，自动清理 |
| ⚙️ **系统托盘** | 最小化到系统托盘，右键菜单快速操作，支持开机自启 |

## 系统要求

| 组件 | 版本 |
|------|------|
| Windows | 10 / 11 (x64) |
| .NET Desktop Runtime | 8.0+ |

## 安装

### 1. 构建项目

```powershell
git clone <repo-url>
cd SnapGo
dotnet build src/SnapGo.App -c Release
```

输出目录：`src/SnapGo.App/bin/Release/net8.0-windows/`

### 2. 启动 SnapGo

直接运行 `SnapGo.exe`，程序将最小化到系统托盘。

## 使用指南

### 截图流程

```
Ctrl+Shift+S → 框选区域 → 松手保留选区 → 调整位置/大小 → Enter 确认
                                                      ↓
                                              浮动工具栏
                                         ├── 💾 保存 PNG（同时复制到剪贴板）
                                         ├── 📝 OCR 提取文字
                                         ├── ✏️ 标注截图
                                         └── 📋 复制到剪贴板
```

1. 按下 `Ctrl+Shift+S` 唤起全屏截图模式
2. 鼠标拖拽框选区域，松手后 **选区保留**（不消失），显示尺寸和辅助线
3. 拖拽 **8 个控制手柄** 调整选区大小，拖拽 **选区内部** 移动位置
4. 按 **Enter** 或 **双击** 确认，按 **Esc** 取消
5. 确认后弹出浮动工具栏，选择后续操作

### OCR 文字提取

1. 截图后在工具栏点击 **OCR** 按钮
2. 首次识别会加载本机模型；之后的识别复用已加载的模型
3. 有可用的 DirectX 12 显卡时走 DirectML，否则使用 CPU
4. 识别结果展示在结果面板中，可复制或导出为文本

### 截图标注

- **矩形** — 拖拽绘制边框
- **箭头** — 拖拽绘制箭头指向
- **文字** — 点击位置输入文本标签
- **画笔** — 自由绘制线条
- 支持 **撤销 / 重做**，19 种预设颜色
- 导出时标注与截图合并

### 历史记录

- 左键托盘图标 → **History** 查看截图历史
- 网格缩略图浏览 + 搜索框关键词过滤
- 点击缩略图查看原图，右键删除
- 自动限制 500 条 / 200MB

### 保存截图

- 工具栏点击 **💾 Save** → 弹出保存对话框，选择路径和文件名
- 保存的图片为 PNG 格式，文件名默认为 `screenshot_YYYYMMDD_HHmmss.png`
- **保存时自动复制截图到剪贴板**，可直接粘贴到聊天窗口或文档中
- 可在 Settings → Storage → **Default save path** 设置默认保存文件夹（如 `C:\Users\...\Pictures\SnapGo`）
- 设置默认路径后，保存对话框将直接定位到该文件夹

### 系统托盘

程序启动后自动在系统托盘（通知区域）显示 SnapGo 图标。

右键托盘图标：

| 菜单项 | 功能 |
|--------|------|
| Settings | 打开设置窗口 |
| Exit | 退出程序 |

左键托盘图标：快速截图（同 `Ctrl+Shift+S`）。

> **注意：** 如果托盘图标被折叠到 Windows 通知溢出区域（`^` 箭头），可手动将其拖拽到可见区域。

## 配置

### 设置窗口

托盘右键 → **Settings** 可修改：

| 设置 | 说明 | 默认值 |
|------|------|--------|
| 开机自启 | 随 Windows 启动 | 关闭 |
| 最小化启动 | 启动时仅显示托盘图标 | 开启 |
| 保存路径 | 截图默认保存文件夹 | 空（打开系统默认位置） |

### 配置文件

配置文件位于 `appsettings.json`，关键配置项：

```jsonc
{
  "App": {
    "Ocr": {
      "TimeoutSeconds": 30,
      "Provider": "Auto"        // Auto | DirectML | CPU
    },
    "History": {
      "MaxRecords": 500,
      "MaxDatabaseSizeMb": 200
    }
  }
}
```

## 项目结构

```
SnapGo/
├── src/SnapGo.App/               # WPF 主程序
│   ├── App.xaml.cs               # 应用入口、DI 容器、开机自启
│   ├── MainWindow.xaml.cs        # 主窗口、热键、托盘、流程调度
│   ├── Models/                   # 数据模型
│   │   ├── AppSettings.cs        # 配置模型
│   │   ├── Annotation.cs         # 标注实体
│   │   ├── HistoryItem.cs        # 历史记录实体
│   │   └── OcrResult.cs          # OCR 结果
│   ├── ViewModels/               # MVVM ViewModel
│   ├── Views/                    # WPF 窗口/控件
│   │   ├── ScreenshotOverlay     # 全屏截图遮罩（拖拽框选）
│   │   ├── FloatingToolbar       # 截图后浮动操作栏
│   │   ├── AnnotationWindow      # 标注编辑器
│   │   ├── ResultPanel           # OCR 结果面板
│   │   ├── HistoryPanel          # 截图历史浏览
│   │   └── SettingsWindow        # 设置窗口
│   ├── Services/                 # 业务服务
│   │   ├── ScreenshotService     # 截图捕获（CopyFromScreen）
│   │   ├── OcrService            # 本机 PP-OCRv5（DirectML / CPU）
│   │   ├── ClipboardService      # 剪贴板操作
│   │   ├── HistoryService        # SQLite 历史 CRUD
│   │   └── AnnotationService     # 标注撤销/重做栈
│   ├── Helpers/                  # 工具类
│   │   ├── NativeMethods.cs      # P/Invoke 声明
│   │   ├── HotkeyHelper.cs       # 全局热键注册
│   │   ├── AutoStartHelper.cs    # 注册表开机自启
│   │   └── ImageHelper.cs        # 位图转换 / Base64
│   ├── Data/                     # 数据库上下文
│   └── icon/                     # 应用图标
└── tools/ConvertIcon/            # PNG → ICO 转换工具
```

## 技术栈

| 层 | 技术选型 |
|----|----------|
| UI 框架 | WPF (.NET 8.0-windows) |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| DI 容器 | Microsoft.Extensions.Hosting (Generic Host) |
| 数据库 | SQLite via Microsoft.Data.Sqlite (ADO.NET) |
| OCR 引擎 | PP-OCRv5 mobile，ONNX Runtime DirectML / CPU |
| 日志 | Serilog |
| 系统托盘 | H.NotifyIcon.Wpf |
| 全局热键 | P/Invoke RegisterHotKey (user32.dll) |
| 截图捕获 | System.Drawing.Common Graphics.CopyFromScreen |

## License

MIT
