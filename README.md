# ArtaleAI - 遊戲視覺輔助工具

**ArtaleAI** 是一個專為特定遊戲設計的智慧輔助工具，它利用即時畫面擷取與電腦視覺技術，自動分析遊戲中的小地圖、玩家位置、怪物資訊等，旨在提供更智慧的遊戲體驗。

## ✨ 主要功能

- **即時畫面擷取**：自動尋找並擷取指定的遊戲視窗畫面，作為後續所有分析的基礎。

- **小地圖分析**：
  - 使用樣板匹配技術自動在遊戲畫面中定位小地圖的邊界
  - 即時辨識玩家在小地圖上的座標位置
  - 偵測小地圖上的其他玩家或特定標記

- **怪物偵測**：
  - 從預設的怪物圖片樣板中，即時在遊戲畫面上偵測指定的怪物
  - 在畫面上標示出偵測到的怪物位置

- **路徑編輯與管理**：
  - 提供地圖編輯器功能，允許使用者在地圖上定義路徑點
  - 支援路徑檔案的儲存與讀取（`.mappath` 格式）

- **高度可設定化**：
  - 透過 `Config/config.yaml` 檔案，使用者可以輕鬆設定遊戲視窗名稱、畫面更新率等參數

- **視覺化工具**：
  - 提供浮動放大鏡工具，方便使用者查看滑鼠周圍的像素細節
  - 可在遊戲畫面上繪製覆蓋圖層，視覺化分析結果（例如：標示怪物、路徑點）

## 🚀 如何開始

### 先決條件
- Windows 作業系統
- .NET Framework（建議 4.7.2 或更高版本）
- Visual Studio 2019 或更高版本

### 安裝與執行

1. **Clone 專案**
git clone [您的專案 Git URL]
cd ArtaleAI


2. **使用 Visual Studio 開啟**
- 直接使用 Visual Studio 開啟 `ArtaleAI.sln` 解決方案檔案
- Visual Studio 會自動還原所需的 NuGet 套件（例如 OpenCvSharp、YamlDotNet 等）

3. **設定組態**
- 開啟 `Config/config.yaml` 檔案
- 修改 `gameWindowTitle` 以符合您要擷取的遊戲視窗標題
- 可依需求調整其他設定

4. **建置與執行**
- 在 Visual Studio 中，點擊「啟動」按鈕（或按 F5）來建置並執行專案
- 程式啟動後，會自動嘗試尋找並掛鉤到指定的遊戲視窗

## 📁 專案結構
```
ArtaleAI/
├── Config/ # 設定檔讀取與管理（YAML）
│ ├── AppConfig.cs
│ ├── ConfigManager.cs
│ └── config.yaml
├── GameCapture/ # 遊戲畫面擷取與視窗控制
│ ├── LiveViewController.cs
│ ├── LiveViewService.cs
│ ├── ScreenCapture.cs
│ └── WindowFinder.cs
├── Minimap/ # 小地圖分析、辨識與路徑編輯
│ ├── MapAnalyzer.cs
│ ├── MapData.cs
│ ├── MapDetector.cs
│ ├── MapEditor.cs
│ ├── MapFileManager.cs
│ ├── MapObject.cs
│ └── MinimapEditor.cs
├── Monster/ # 怪物偵測相關服務
│ ├── MonsterImageFetcher.cs
│ └── MonsterService.cs
├── UI/ # 使用者介面（Windows Forms）
│ ├── Events/ # UI 事件介面
│ │ ├── IApplicationEventHandler.cs
│ │ ├── IConfigEventHandler.cs
│ │ ├── ILiveViewEventHandler.cs
│ │ └── IMapFileEventHandler.cs
│ ├── FloatingMagnifier.cs
│ ├── MainForm.cs
│ └── MainForm.Designer.cs
├── Utils/ # 共用工具程式
│ ├── FileUtils.cs
│ ├── MathUtils.cs
│ ├── OverlayRenderer.cs # 畫面覆蓋繪製
│ ├── PathUtils.cs
│ └── TemplateMatcher.cs # 核心樣板匹配功能
├── Templates/ # 用於樣板匹配的圖片資源
│ ├── MainScreen/
│ ├── Minimap/
│ └── Monsters/
├── MapData/ # 存放地圖路徑檔案
│ └── test.mappath
├── ArtaleAI.sln # Visual Studio 解決方案
├── Program.cs # 應用程式進入點
└── opencv_world480.dll # OpenCV 函式庫
```
## 📦 依賴套件

本專案主要依賴以下幾個 NuGet 套件：

- **OpenCvSharp4.Windows**：用於所有電腦視覺相關的操作，是專案的核心
- **YamlDotNet**：用於解析 `config.yaml` 設定檔
- **Newtonsoft.Json**：JSON 資料處理
- **SharpDX**：DirectX 相關功能支援

## 🎯 核心功能詳解

### 智慧怪物偵測
- 支援多種偵測模式：Basic、ContourOnly、Grayscale、Color、TemplateFree
- 自動遮擋處理和非極大值抑制
- 可配置的信心度閾值和辨識框樣式

### 小地圖系統
- 自動偵測小地圖邊界和玩家位置
- 支援路徑規劃和編輯
- 即時疊加層顯示

### 視覺化介面
- 浮動放大鏡功能
- 可自訂的覆蓋圖層樣式
- 即時狀態監控和日誌顯示

---
