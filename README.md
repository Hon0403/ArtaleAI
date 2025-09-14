# 🎮 ArtaleAI - 遊戲電腦視覺輔助工具

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)  
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2+-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework)  
[![OpenCV](https://img.shields.io/badge/OpenCV-4.8.0-green.svg)](https://opencv.org/)

**基於電腦視覺技術的遊戲輔助系統**

</div>

---

## 📖 項目簡介

**ArtaleAI** 是專為 *MapleStory Worlds - Artale* 設計的遊戲視覺輔助工具。運用電腦視覺與機器學習技術，提供即時畫面分析、目標檢測與路徑規劃，提升自動化與輔助效率。

---

## 🌟 核心特色

- 🔍 **目標檢測**：多模式怪物識別（Basic / Color / Grayscale / ContourOnly / TemplateFree）  
- 🗺️ **小地圖分析**：自動檢測小地圖邊界、玩家位置與遊戲元素  
- 📍 **路徑規劃**：視覺化地圖編輯器，支援路徑點、安全區域、限制區域、繩索標記  
- 🩸 **血條檢測**：即時隊友血條識別與玩家追蹤  
- ⚔️ **攻擊範圍顯示**：動態可視化攻擊範圍  
- 🎯 **模板管理**：怪物模板自動下載、快取與管理  
- ⚡ **高效能優化**：BGR 色彩空間統一、Mat 域直接處理、NMS 去重算法  

---

## 🚀 快速開始

### 系統需求

- **OS**：Windows 10/11 (x64)  
- **運行環境**：.NET Framework 4.7.2+  
- **開發**：Visual Studio 2019+ 或 VS Code  
- **建議記憶體**：4GB+  
- **顯示卡**：支援 DirectX 11

### 安裝與執行

```bash
# 1. 下載專案
git clone https://github.com/[Your-Username]/ArtaleAI.git
cd ArtaleAI

# 2. 使用 Visual Studio 開啟
start ArtaleAI.sln

# 或以命令列建置
dotnet restore
dotnet build

# 3. 執行
dotnet run
```

### 設定

編輯 `Config/config.yaml`：
- `gameWindowTitle`：遊戲視窗標題
- 調整檢測閾值、頻率與效能參數

---

## ⚠️ 視窗陰影與解析度設定（必讀）

> 若模板與遊戲畫面像素未完全對齊，範例閾值 0.30–0.35 可能匹配不到。請先完成下列設定，再執行機器人。

### 1 . 關閉 Windows 視窗陰影

Windows 11／10 預設在非最大化視窗加 1 px 半透明陰影，會導致截圖內容實寬多 1 px。  
請依版本關閉：

| 系統 | 操作路徑 |
|------|----------|
| Windows 10 / Windows 11 (所有版本) | 按 **Win + R** → 輸入 `sysdm.cpl ,3` → Enter → 視覺效果 → 取消勾選 **「在視窗下顯示陰影」** (Shadows under windows) → 確定。 |
| Windows 11 22H2+ | 設定 → **輔助工具 → 視覺效果** → 關閉「視窗陰影」。 |

### 2 . 固定縮放與解析度

| 項目 | 建議值 |
|------|--------|
| Windows 顯示縮放 | 100 % |
| MapleStory UI Scale | 100 % |
| MapleStory 視窗大小 | 建議 **1600 × 900**（與模板 1 : 1 對應）或其它固定 16:9 解析度 |
| MapleStory.exe 高 DPI 設定 | 右鍵 exe → 內容 → 相容性 → 高 DPI → 勾選「**覆寫高 DPI 縮放行為：應用程式**」 |

> 完成後，以任何截圖工具擷取遊戲畫面，用 JS Paint 或 Image Measurement-Online 量測怪物尺寸，應與模板 PNG 完全一致（例如 65×70 px）。若仍差 1 px，請確認視窗未拖拉改尺寸，或直接使用全螢幕模式 (`Alt+Enter`)。

### 3 . 推薦閾值與多尺度設定

```yaml
# config/config_default.yaml（範例）
monster_detect:
  mode:        color      # 或 grayscale
  defaultThreshold: 0.35  # 解析度 1 : 1 時建議值
  multiScaleFactors: [1.0]  # 已對齊無需額外倍率
```

若因硬體限制無法完全 1 : 1，可暫時改用：

```yaml
# 容忍 ±3 % 尺寸誤差
multiScaleFactors: [0.97, 1.0, 1.03]
defaultThreshold: 0.30
```

### 範本匹配快速自檢

執行程式後，日誌應出現類似：

```
🎯 尺度 1.0x – 最高分數: 0.86
🎯 多尺度匹配完成，總共找到 8 個怪物
```

若最高分數 < 0.50 或僅偵測到 1 隻怪，請重新檢查本節設定。

### 作者當前測試環境

| 項目 | 數值 |
|------|------|
| 螢幕實體解析度 | 1,920 × 1,080 |
| MapleStory 視窗大小 | 1,600 × 900 |
| Windows 顯示縮放 | 100 % |
| Windows 視窗陰影 | 已關閉 |
| 偵測閾值 | 0.35 (color) / 0.33 (grayscale) |
| 多尺度列表 | [1.0] |

在此環境下，彩色與灰階模式皆可得到 0.80–0.90 的匹配分數，無需低於 0.30 的閾值。

---

## 🎯 功能詳解

### 怪物檢測（範例 config）

```yaml
templates:
  monsterDetection:
    detectionMode: Color   # Basic / Color / Grayscale / ContourOnly / TemplateFree
    defaultThreshold: 0.1
    maxDetectionResults: 1
```

**檢測模式說明**：
- **Basic**：最快速的模板匹配
- **Color**：彩色匹配（推薦）
- **Grayscale**：灰階匹配（平衡速度與準確度）
- **ContourOnly**：輪廓匹配（抗干擾）
- **TemplateFree**：無需模板的自由檢測（基於特徵/ML）

### 血條檢測（範例）

```yaml
partyRedBar:
  lowerRedHsv: [0, 100, 100]
  upperRedHsv: [10, 255, 255]
  minBarWidth: 1
  maxBarWidth: 60
  detectionBoxWidth: 550
  detectionBoxHeight: 300
```

### 地圖與路徑（格式範例）

地圖檔案（JSON）示例：
```json
{
  "waypointPaths": [
    {
      "points": [[100.5, 200.0], [150.0, 250.5]]
    }
  ],
  "safeZones": [],
  "ropes": [],
  "restrictedPoints": []
}
```

地圖編輯器支援：路徑點、繩索、限制區域、安全區域與標記刪除。

---

## 📁 專案架構

```
ArtaleAI/
├── API/                # 外部 API 與模型
│   ├── MonsterImageFetcher.cs
│   └── Models/
├── Config/             # 設定管理 (AppConfig, config.yaml)
│   ├── ConfigManager.cs
│   └── AppConfig.cs
├── Detection/          # 檢測核心 (MapDetector, TemplateMatcher, ...)
│   ├── TemplateMatcher.cs
│   ├── MonsterTemplateStore.cs
│   ├── MapDetector.cs
│   └── BloodBarDetector.cs
├── Display/            # 視覺化 / 覆蓋層
│   ├── SimpleRenderer.cs
│   └── FloatingMagnifier.cs
├── GameWindow/         # 視窗尋找與擷取
│   ├── WindowFinder.cs
│   └── ScreenCapture.cs
├── Minimap/            # 小地圖編輯與管理
│   ├── MapEditor.cs
│   └── MapFileManager.cs
├── Models/             # 內部資料模型
│   └── Models.cs
├── Utils/              # 工具函式
│   └── Utils.cs
└── Templates/          # 模板資源 (minimap/, monsters/)
```

---

## ⚙️ 常用配置（片段）

```yaml
general:
  gameWindowTitle: "MapleStory Worlds-Artale (繁體中文版)"
  zoomFactor: 15

windowCapture:
  captureFrameRate: 15

detectionPerformance:
  bloodBarDetectIntervalMs: 150
  monsterDetectIntervalMs: 200
```

**模板匹配 NMS 參數**：
```yaml
templateMatching:
  modeSpecificNms:
    Color:
      iouThreshold: 0.10
      confidenceThreshold: 0.2
      maxResults: 1
    Basic:
      iouThreshold: 0.15
      confidenceThreshold: 0.3
      maxResults: 1
```

**視覺化樣式**：
```yaml
overlayStyle:
  monster:
    frameColor: "255,255,0"   # 黃色
    textColor: "255,0,0"      # 紅色
    showConfidence: true
    textFormat: "{0} ({1:F2})"
```

---

## 🛠️ 技術棧

- **語言**：C# 6.0
- **UI**：Windows Forms
- **影像處理**：OpenCvSharp 4.8.0
- **設定**：YamlDotNet
- **JSON**：System.Text.Json
- **圖形 API**：SharpDX (DirectX)

**關鍵算法**：OpenCV MatchTemplate (多尺度)、IoU-based NMS、輪廓檢測、BGR/HSV/灰階轉換、Mat 域優化。

---

## 📊 性能優化

- **Mat 物件**即時釋放與資源管理
- **模板快取**（避免每幀重複轉換）
- **幀池復用**策略與垃圾回收優化
- 以**陣列/Span<T>** 取代大量 LINQ 呼叫以降低分配成本
- **非同步檢測管線**與 ROI 優化減少工作量

---

## 🎮 使用方式

1. **啟動遊戲**：確保 MapleStory Worlds-Artale 在運行
2. **選擇分頁**：
   - **路徑編輯**：靜態小地圖編輯路徑點
   - **即時顯示**：動態怪物檢測與血條追蹤
3. **設定怪物模板**：選擇或下載目標怪物模板
4. **調整檢測模式**：根據環境選擇最佳檢測模式

---
