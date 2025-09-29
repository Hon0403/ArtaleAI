# 🎮 ArtaleAI - 遊戲電腦視覺輔助工具

**基於電腦視覺技術的 MapleStory Worlds - Artale 遊戲輔助系統**

[![Stars](https://img.shields.io/github/stars/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/stargazers)
[![Forks](https://img.shields.io/github/forks/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/network)
[![Issues](https://img.shields.io/github/issues/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/issues)
[![License](https://img.shields.io/github/license/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/blob/main/LICENSE)

## 📖 項目簡介

**ArtaleAI** 是專為 *MapleStory Worlds - Artale* 設計的智能遊戲視覺輔助工具。運用先進的電腦視覺與機器學習技術，提供即時畫面分析、精準目標檢測與智能路徑規劃，大幅提升遊戲自動化效率與輔助體驗。

## 🌟 核心特色

### 🔍 多模式目標檢測系統
- **Basic Mode**：高速基礎模板匹配，適合簡單場景
- **Color Mode**：彩色精準匹配（推薦使用）
- **Grayscale Mode**：灰階匹配，平衡速度與準確度
- **ContourOnly Mode**：輪廓檢測，強抗干擾能力
- **TemplateFree Mode**：無需模板的智能檢測（基於 ML 特徵）

### 🗺️ 智能小地圖分析
- 自動檢測小地圖邊界與玩家位置
- 實時遊戲元素識別與追蹤
- 動態地圖狀態監控

### 📍 可視化路徑規劃系統
- **拖拽式地圖編輯器**：直觀的路徑點設置
- **多層級區域管理**：安全區域、限制區域、繩索路徑標記
- **JSON 格式路徑存儲**：易於分享和管理
- **實時路徑預覽**：即時顯示規劃路線

### 🩸 高精度血條檢測
- 即時隊友血條識別與狀態監控
- 基於 HSV 色彩空間的紅色血條檢測
- 可調節的檢測範圍與敏感度設定

### ⚔️ 動態攻擊範圍顯示
- 實時可視化角色攻擊範圍
- 動態範圍調整與顯示優化

### 🎯 智能模板管理系統
- **自動模板下載**：支援線上模板庫
- **本地模板快取**：避免重複下載，提升效能
- **模板版本控制**：自動更新與同步

### ⚡ 高效能優化技術
- **BGR 色彩空間統一**：減少不必要的色彩轉換
- **Mat 物件直接處理**：最小化記憶體拷貝開銷
- **NMS 去重算法**：智能排除重複檢測結果
- **多尺度模板匹配**：適應不同解析度環境
- **ROI 區域限制**：專注關鍵檢測區域，提升效率

## 🚀 快速開始

### 系統需求

| 項目 | 需求 |
|------|------|
| **作業系統** | Windows 10/11 (x64) |
| **運行環境** | .NET Framework 4.7.2+ |
| **開發工具** | Visual Studio 2019+ 或 VS Code |
| **記憶體** | 4GB+ (建議 8GB+) |
| **顯示卡** | 支援 DirectX 11 |
| **硬碟空間** | 500MB+ |

### 安裝與執行

```bash
# 1. 複製專案
git clone https://github.com/Hon0403/ArtaleAI.git
cd ArtaleAI

# 2. 使用 Visual Studio 開啟
start ArtaleAI.sln

# 或使用命令列建置
dotnet restore
dotnet build

# 3. 執行程式
dotnet run
```

### 初始設定

1. **編輯組態檔案** `Config/config.yaml`：
   ```yaml
   general:
     gameWindowTitle: "MapleStory Worlds-Artale (繁體中文版)"
     zoomFactor: 15
   
   windowCapture:
     captureFrameRate: 15
   
   detectionPerformance:
     monsterDetectIntervalMs: 200
     bloodBarDetectIntervalMs: 150
   ```

2. **設定檢測參數**：
   - 調整檢測閾值
   - 設定檢測頻率
   - 配置效能參數

## ⚠️ 重要設定（必讀）

為確保模板匹配的準確性，請完成以下系統設定：

### 1. 關閉 Windows 視窗陰影

Windows 11/10 預設會在非最大化視窗加入 1px 半透明陰影，導致截圖尺寸偏差。

| 系統版本 | 設定路徑 |
|----------|----------|
| **Windows 10/11 (通用)** | `Win + R` → 輸入 `sysdm.cpl ,3` → Enter → 視覺效果 → 取消勾選「**在視窗下顯示陰影**」 |
| **Windows 11 22H2+** | 設定 → 輔助工具 → 視覺效果 → 關閉「視窗陰影」 |

### 2. 解析度與縮放設定

| 設定項目 | 建議值 |
|----------|--------|
| **Windows 顯示縮放** | 100% |
| **遊戲 UI 縮放** | 100% |
| **遊戲視窗大小** | **1600 × 900** (與模板 1:1 對應) |
| **DPI 設定** | 右鍵遊戲 exe → 內容 → 相容性 → 高 DPI → 勾選「覆寫高 DPI 縮放行為：應用程式」 |

### 3. 模板匹配參數優化

```yaml
# 解析度完全對齊時的推薦設定
monster_detect:
  mode: color
  defaultThreshold: 0.35
  multiScaleFactors: [1.0]  # 單一尺度，最佳效能

# 如需容錯的設定（±3% 尺寸誤差）
monster_detect:
  mode: color
  defaultThreshold: 0.30
  multiScaleFactors: [0.97, 1.0, 1.03]
```

### 4. 效能驗證

執行程式後，控制台應顯示類似輸出：
```
🎯 尺度 1.0x – 最高分數: 0.86
🎯 多尺度匹配完成，總共找到 8 個怪物
```

若最高分數 < 0.50 或僅檢測到單一目標，請重新檢查上述設定。

## 📁 專案架構

```
ArtaleAI/
├── 📂 API/                    # 外部 API 與資料模型
│   ├── 📄 MonsterImageFetcher.cs
│   └── 📂 Models/
│       └── 📄 ArtaleMonster.cs
├── 📂 Config/                 # 組態管理系統
│   ├── 📄 ConfigManager.cs
│   ├── 📄 AppConfig.cs
│   └── 📄 config.yaml
├── 📂 Detection/              # 核心檢測演算法
│   ├── 📄 TemplateMatcher.cs      # 模板匹配引擎
│   ├── 📄 MonsterTemplateStore.cs # 模板管理器
│   ├── 📄 MapDetector.cs          # 地圖檢測器
│   └── 📄 BloodBarDetector.cs     # 血條檢測器
├── 📂 Display/                # 視覺化與覆蓋層
│   ├── 📄 SimpleRenderer.cs       # 簡易渲染器
│   └── 📄 FloatingMagnifier.cs    # 浮動放大鏡
├── 📂 GameCapture/            # 遊戲擷取系統 (不是 GameWindow/)
│   ├── 📄 WindowFinder.cs         # 視窗搜尋器
│   └── 📄 ScreenCapture.cs        # 螢幕截圖模組
├── 📂 MapEditor/              # 地圖編輯器 (不是 Minimap/)
│   ├── 📄 MapEditor.cs            # 地圖編輯器
│   └── 📄 MapFileManager.cs       # 地圖檔案管理
├── 📂 Models/                 # 內部資料結構 (已分離多個檔案)
│   ├── 📄 DataModels.cs           # 資料模型
│   ├── 📄 Enums.cs               # 列舉定義
│   ├── 📄 Interfaces.cs          # 介面定義
│   └── 📄 RenderModels.cs        # 渲染模型
├── 📂 UI/                     # 使用者介面 (新增)
│   ├── 📄 MainForm.cs
│   ├── 📄 MainForm.Designer.cs
│   └── 📄 MainForm.resx
├── 📂 Utils/                  # 工具函式庫 (大幅擴展)
│   ├── 📄 CacheManager.cs         # 快取管理器
│   ├── 📄 GeometryCalculator.cs   # 幾何計算器
│   ├── 📄 OpenCvProcessor.cs      # OpenCV 處理器
│   ├── 📄 PathManager.cs          # 路徑管理器
│   └── 📄 ResourceManager.cs      # 資源管理器
├── 📂 templates/              # 模板資源庫 (小寫)
│   ├── 📂 MainScreen/             # 主畫面模板
│   ├── 📂 minimap/               # 小地圖模板
│   └── 📂 monsters/octopus/      # 怪物模板
├── 📄 Program.cs              # 程式進入點
├── 📄 ArtaleAI.csproj         # 專案檔案
└── 📄 ArtaleAI.sln            # 解決方案檔案
```

## 🎯 功能詳細說明

### 怪物檢測系統

```yaml
# 組態範例
templates:
  monsterDetection:
    detectionMode: Color        # 檢測模式選擇
    defaultThreshold: 0.35      # 預設匹配閾值
    maxDetectionResults: 5      # 最大檢測結果數
    enableMultiScale: true      # 啟用多尺度匹配
```

**檢測模式特性對比**：

| 模式 | 速度 | 準確度 | 抗干擾 | 適用場景 |
|------|------|--------|--------|----------|
| **Basic** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | 簡單環境，追求速度 |
| **Color** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | 一般使用，平衡效能（推薦） |
| **Grayscale** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | 光線變化大的環境 |
| **ContourOnly** | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | 複雜背景，強抗干擾 |
| **TemplateFree** | ⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | 動態目標，機器學習檢測 |

### 血條檢測系統

```yaml
# HSV 色彩空間血條檢測配置
partyRedBar:
  lowerRedHsv: [0, 100, 100]    # 紅色下界
  upperRedHsv: [10, 255, 255]   # 紅色上界
  minBarWidth: 1                # 最小血條寬度
  maxBarWidth: 60               # 最大血條寬度
  detectionBoxWidth: 550        # 檢測區域寬度
  detectionBoxHeight: 300       # 檢測區域高度
```

### 路徑規劃系統

**JSON 格式路徑檔案範例**：
```json
{
  "waypointPaths": [
    {
      "name": "主要路線",
      "points": [
        [100.5, 200.0],
        [150.0, 250.5],
        [200.2, 300.8]
      ]
    }
  ],
  "safeZones": [
    {
      "name": "安全區域1",
      "bounds": [50, 50, 100, 100]
    }
  ],
  "restrictedPoints": [
    [75.5, 125.0]
  ],
  "ropes": [
    {
      "start": [10, 20],
      "end": [30, 40]
    }
  ]
}
```

## ⚙️ 進階組態設定

### 效能調整參數

```yaml
# 效能優化設定
detectionPerformance:
  monsterDetectIntervalMs: 200     # 怪物檢測間隔
  bloodBarDetectIntervalMs: 150    # 血條檢測間隔
  enableGPUAcceleration: true      # 啟用 GPU 加速
  maxConcurrentDetections: 3       # 最大併發檢測數

# 模板匹配 NMS (非最大抑制) 參數
templateMatching:
  modeSpecificNms:
    Color:
      iouThreshold: 0.15           # IoU 閾值
      confidenceThreshold: 0.35    # 信心度閾值
      maxResults: 5                # 最大結果數
    Grayscale:
      iouThreshold: 0.12
      confidenceThreshold: 0.30
      maxResults: 3
```

### 視覺化樣式設定

```yaml
# 覆蓋層顯示樣式
overlayStyle:
  monster:
    frameColor: "255,255,0"        # 邊框顏色 (黃色)
    textColor: "255,0,0"           # 文字顏色 (紅色)
    frameThickness: 2              # 邊框粗細
    showConfidence: true           # 顯示信心度
    textFormat: "{0} ({1:F2})"     # 文字格式
  
  bloodBar:
    frameColor: "0,255,0"          # 血條邊框 (綠色)
    warningColor: "255,165,0"      # 警告顏色 (橙色)
    criticalColor: "255,0,0"       # 危險顏色 (紅色)
```

## 🛠️ 技術架構

### 核心技術棧

| 類別 | 技術 | 版本 | 用途 |
|------|------|------|------|
| **程式語言** | C# | 8.0+ | 主要開發語言 |
| **UI 框架** | Windows Forms | .NET Framework 4.7.2+ | 使用者介面 |
| **電腦視覺** | OpenCvSharp | 4.8.0+ | 影像處理與分析 |
| **圖形 API** | SharpDX | 4.2.0 | DirectX 螢幕截圖 |
| **組態管理** | YamlDotNet | 最新穩定版 | YAML 配置解析 |
| **JSON 處理** | System.Text.Json | .NET 內建 | JSON 序列化/反序列化 |

### 核心演算法

- **OpenCV MatchTemplate**：多尺度模板匹配引擎
- **IoU-based NMS**：智能去重演算法
- **輪廓檢測**：基於形狀的目標識別
- **色彩空間轉換**：BGR/HSV/灰階最佳化處理
- **Mat 記憶體優化**：高效能影像處理

## 📊 性能優化策略

### 記憶體管理優化
- ✅ **Mat 物件即時釋放**：避免記憶體洩漏
- ✅ **模板快取機制**：避免重複載入和轉換
- ✅ **物件池復用**：減少 GC 壓力
- ✅ **Span/ArrayPool 優化**：取代 LINQ 大量記憶體分配

### 計算效能優化
- ✅ **非同步檢測管線**：多執行緒並行處理
- ✅ **ROI 區域限制**：專注關鍵檢測區域
- ✅ **自適應檢測頻率**：根據場景動態調整
- ✅ **GPU 硬體加速**：利用顯卡計算能力

### 預期性能指標

| 解析度 | 檢測延遲 | CPU 使用率 | 記憶體佔用 |
|--------|----------|------------|------------|
| **1600×900** | 50-100ms | < 15% | < 200MB |
| **1920×1080** | 80-150ms | < 25% | < 300MB |
| **2560×1440** | 150-250ms | < 35% | < 500MB |

## 🎮 使用指南

### 基本操作流程

1. **🎯 啟動遊戲**
   - 確保 MapleStory Worlds-Artale 正在運行
   - 設定遊戲為視窗化模式（推薦 1600×900）

2. **⚙️ 選擇功能模式**
   - **路徑編輯模式**：靜態小地圖編輯與路徑規劃
   - **即時檢測模式**：動態怪物檢測與血條監控
   - **調試模式**：檢測參數調整與效能監控

3. **📋 設定檢測目標**
   - 從模板庫選擇或匯入怪物模板
   - 調整檢測閾值與敏感度參數
   - 設定檢測區域與排除區域

4. **🔧 優化檢測模式**
   - 根據環境光線選擇最佳檢測模式
   - 調整 NMS 參數避免重複檢測
   - 監控效能指標並進行調整

### 進階功能使用

#### 自定義模板創建
1. 使用遊戲內截圖工具擷取目標怪物
2. 裁切為純淨的怪物影像（建議 64×64 像素）
3. 儲存為 PNG 格式到 `Templates/monsters/` 目錄
4. 在設定檔中註冊新模板

#### 路徑規劃編輯
1. 開啟地圖編輯器模式
2. 拖拽設置路徑點與連接線
3. 標記安全區域與危險區域
4. 匯出路徑檔案供自動導航使用