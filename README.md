# ArtaleAI - 遊戲電腦視覺輔助工具

**基於電腦視覺技術的 MapleStory Worlds - Artale 遊戲輔助系統**

[![Stars](https://img.shields.io/github/stars/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/stargazers)
[![Forks](https://img.shields.io/github/forks/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/network)
[![Issues](https://img.shields.io/github/issues/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/issues)
[![License](https://img.shields.io/github/license/Hon0403/ArtaleAI?style=flat-square)](https://github.com/Hon0403/ArtaleAI/blob/main/LICENSE)

## 項目簡介

**ArtaleAI** 是專為 *MapleStory Worlds - Artale* 設計的智能遊戲視覺輔助工具。運用先進的電腦視覺與機器學習技術，提供即時畫面分析、精準目標檢測與智能路徑規劃，大幅提升遊戲自動化效率與輔助體驗。

## 核心特色

### 多模式目標檢測系統
- **Basic Mode**：高速基礎模板匹配，適合簡單場景
- **Color Mode**：彩色精準匹配（推薦使用）
- **Grayscale Mode**：灰階匹配，平衡速度與準確度
- **ContourOnly Mode**：輪廓檢測，強抗干擾能力
- **TemplateFree Mode**：無需模板的智能檢測（基於 ML 特徵）

### 智能小地圖分析
- 自動檢測小地圖邊界與玩家位置
- 實時遊戲元素識別與追蹤
- 動態地圖狀態監控

### 智能路徑規劃系統
- **一鍵路徑錄製**：按 F1 自動啟動背景擷取並錄製座標
- **智能座標優化**：自動去重和排序，優化路徑點數量
- **拖拽式地圖編輯器**：直觀的路徑點設置與編輯
- **多層級區域管理**：安全區域、限制區域、繩索路徑標記
- **JSON 格式路徑存儲**：易於分享和管理
- **實時路徑預覽**：即時顯示規劃路線與座標
- **自動角色移動控制**：智能路徑規劃，自動控制角色移動到隨機目標點
- **背景運行模式**：路徑規劃和怪物辨識可在任何分頁運行，無需切換

### 高精度血條檢測
- 即時隊友血條識別與狀態監控
- 基於 HSV 色彩空間的紅色血條檢測
- 可調節的檢測範圍與敏感度設定

### 動態攻擊範圍顯示
- 實時可視化角色攻擊範圍
- 動態範圍調整與顯示優化

### 智能模板管理系統
- **自動模板下載**：支援線上模板庫
- **本地模板快取**：避免重複下載，提升效能
- **模板版本控制**：自動更新與同步

### 自動化控制系統
- **智能角色移動**：自動控制角色移動到路徑規劃目標點
- **方向鍵控制**：使用方向鍵（↑↓←→）模擬玩家操作
- **長按模式**：持續按住方向鍵直到接近目標，流暢移動
- **自動聚焦遊戲視窗**：確保按鍵發送到正確的遊戲視窗
- **智能停止機制**：到達目標後自動停止移動

### 高效能優化技術
- **BGR 色彩空間統一**：減少不必要的色彩轉換
- **Mat 物件直接處理**：最小化記憶體拷貝開銷
- **NMS 去重算法**：智能排除重複檢測結果
- **多尺度模板匹配**：適應不同解析度環境
- **ROI 區域限制**：專注關鍵檢測區域，提升效率
- **智能處理優先級**：優先處理路徑規劃和移動控制，減少延遲
- **可視化資源優化**：即時顯示分頁為純可視化工具，背景運行時節省資源

## 快速開始

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

## 重要設定（必讀）

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
尺度 1.0x – 最高分數: 0.86
多尺度匹配完成，總共找到 8 個怪物
```

若最高分數 < 0.50 或僅檢測到單一目標，請重新檢查上述設定。

## 專案架構

```
ArtaleAI/
├── API/                    # 外部 API 整合
│   ├── MonsterImageFetcher.cs
│   └── Models/
│       └── ArtaleMonster.cs
├── Config/                 # 組態管理
│   └── AppConfig.cs        # 應用程式設定（Singleton）
├── Core/                   # 核心業務邏輯
│   ├── GameVisionCore.cs   # 視覺處理核心
│   ├── PathPlanningTracker.cs  # 路徑追蹤器（含邊界防護）
│   └── SharedGameState.cs      # 執行緒安全狀態共享
├── Data/                   # 資料檔案
│   └── config.yaml             # YAML 組態檔
├── Models/                 # 資料模型（重構後分類）
│   ├── Detection/              # 檢測相關模型
│   │   ├── DetectionResult.cs
│   │   ├── DetectionBox.cs
│   │   ├── MonsterStyle.cs
│   │   └── DetectionStyles.cs
│   ├── PathPlanning/           # 路徑規劃模型
│   │   ├── PathPlanningState.cs
│   │   └── PlatformBounds.cs
│   ├── Minimap/                # 小地圖模型
│   │   ├── MinimapResult.cs
│   │   ├── MinimapTrackingResult.cs
│   │   └── MinimapStyles.cs
│   ├── Map/                    # 地圖資料模型
│   │   └── MapData.cs
│   └── JsonHelpers.cs          # JSON 序列化輔助
├── Services/               # 服務層模組
│   ├── CharacterMovementController.cs  # 角色移動控制（含三重邊界防護）
│   ├── FlexiblePathPlanner.cs          # 靈活路徑規劃
│   ├── MapFileManager.cs               # 地圖檔案管理
│   ├── PathPlanningManager.cs          # 路徑規劃管理器
│   ├── RouteRecorderService.cs         # 路徑錄製服務
│   ├── ScreenCapture.cs                # 螢幕擷取（GPU 加速）
│   └── WindowFinder.cs                 # 視窗搜尋器
├── UI/                     # 使用者介面
│   ├── MainForm.cs             # 主視窗
│   ├── MainForm.Designer.cs    # UI 設計器
│   ├── MapEditor.cs            # 地圖編輯器
│   └── LiveViewManager.cs      # 即時顯示管理器
├── Utils/                  # 工具函式庫
│   ├── Logger.cs               # Serilog 日誌整合
│   ├── MsgLog.cs               # UI 日誌輔助
│   ├── DrawingHelper.cs        # GDI+ 繪圖輔助
│   └── PathManager.cs          # 路徑管理器
├── templates/              # 模板資源庫
│   ├── minimap/                # 小地圖模板
│   └── monsters/               # 怪物模板
├── Program.cs              # 程式進入點
├── ArtaleAI.csproj         # 專案檔案
└── ArtaleAI.sln            # 解決方案檔案
```

#### 三重邊界防護系統
- **緊急停止**：角色超出邊界時立即停止
- **緩衝區預警**：接近邊界時觸發減速
- **目標驗證**：目標點超出邊界時自動選擇安全點

#### 執行緒安全優化
- `PathPlanningTracker` 使用 `volatile` 和 snapshot 模式
- `SharedGameState` 公告板模式，避免競爭條件

#### 架構重構
- Models 按功能分類到子資料夾
- AppConfig 移至 Config 資料夾
- 刪除舊的 DataModels.cs（已拆分）

#### Serilog 日誌系統
- 替換 Debug.WriteLine 為結構化日誌
- 檔案日誌 + 主控台輸出
- Debug/Info/Warning/Error 分級

## 功能詳細說明

### 路徑錄製系統

**特色功能**：
- **一鍵啟動**：按 F1 自動啟動背景擷取和錄製
- **背景運行**：不需要切換到即時顯示分頁
- **自動優化**：智能去重和排序，減少 50%+ 路徑點
- **精確記錄**：每秒採樣玩家座標，忽略靜止點

**錄製參數設置**：
```yaml
pathRecording:
  minRecordIntervalMs: 50        # 最小錄製間隔（毫秒）
  minMovementDistance: 2.0        # 最小移動距離（像素）
  autoDeduplication: true         # 自動去重
  autoSorting: true              # 自動排序
```

**座標優化演算法**：
1. **去重策略**：使用 HashSet 移除所有重複座標
2. **排序規則**：先按 X 座標升序，再按 Y 座標升序
3. **精度控制**：座標保留至小數點後一位（如 `61.5`）

**適用場景**：
- 隨機路徑規劃（隨機選擇目標點）
- 區域座標收集（記錄可行走區域）
- 地圖探索（記錄已探索位置）

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
| **Basic** | 5/5 | 3/5 | 2/5 | 簡單環境，追求速度 |
| **Color** | 4/5 | 5/5 | 4/5 | 一般使用，平衡效能（推薦） |
| **Grayscale** | 4/5 | 4/5 | 3/5 | 光線變化大的環境 |
| **ContourOnly** | 3/5 | 4/5 | 5/5 | 複雜背景，強抗干擾 |
| **TemplateFree** | 2/5 | 5/5 | 5/5 | 動態目標，機器學習檢測 |

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

**快捷鍵操作**：
- **F1**：開始/停止路徑錄製（自動啟動背景擷取）
- **F3**：儲存錄製的路徑（自動去重 + 排序）
- **F4**：刷新路徑編輯畫面
- **Ctrl+S**：儲存地圖檔案

**路徑錄製流程**：
1. 在任何分頁按 **F1** 開始錄製（自動啟動背景畫面擷取）
2. 在遊戲中移動角色，系統自動記錄座標
3. 按 **F1** 停止錄製
4. 按 **F3** 儲存路徑（自動去重和排序）
5. 按 **Ctrl+S** 儲存地圖檔案

**自動路徑規劃與移動控制**：
1. 載入路徑檔案（從下拉選單選擇）
2. 點擊「自動打怪」或「路徑規劃啟動」
3. 系統自動：
   - 啟動背景畫面擷取（不需要切換分頁）
   - 載入小地圖位置
   - 隨機選擇目標點
   - 自動控制角色移動到目標點
   - 到達後自動選擇下一個隨機目標點
4. 可在任何分頁運行，即時顯示分頁僅用於可視化除錯

**智能優化功能**：
- **自動去重**：移除所有重複座標，保留首次出現
- **智能排序**：按 X、Y 座標升序排列，JSON 整齊美觀
- **適合隨機路徑規劃**：優化後的座標可用於隨機目標點選擇

**JSON 格式路徑檔案範例**：
```json
{
  "WaypointPaths": [
    [61, 29],
    [62, 29],
    [65, 29],
    [67, 29],
    [69, 29]
  ],
  "Ropes": [],
  "RestrictedZones": []
}
```

**手動編輯路徑**：
- 在地圖編輯器中點擊小地圖設置路徑點
- 支援路徑點、安全區、限制區、繩索標記
- 拖拽編輯，實時預覽

## 進階組態設定

### 效能調整參數

```yaml
# 效能優化設定
detectionPerformance:
  monsterDetectIntervalMs: 200     # 怪物檢測間隔
  bloodBarDetectIntervalMs: 150    # 血條檢測間隔
  enableGPUAcceleration: true      # 啟用 GPU 加速
  maxConcurrentDetections: 3       # 最大併發檢測數

# 路徑規劃設定
pathPlanning:
  waypointReachDistance: 4.0       # 到達路徑點的距離閾值（像素）
  enableAutoMovement: true         # 啟用自動移動控制
  continuousDetectionIntervalMs: 100 # 連續檢測間隔（毫秒）

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

## 技術架構

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

## 性能優化策略

### 記憶體管理優化
-  **Mat 物件即時釋放**：避免記憶體洩漏
-  **模板快取機制**：避免重複載入和轉換
-  **物件池復用**：減少 GC 壓力
-  **Span/ArrayPool 優化**：取代 LINQ 大量記憶體分配

### 計算效能優化
-  **非同步檢測管線**：多執行緒並行處理
-  **ROI 區域限制**：專注關鍵檢測區域
-  **自適應檢測頻率**：根據場景動態調整
-  **GPU 硬體加速**：利用顯卡計算能力

### 預期性能指標

| 解析度 | 檢測延遲 | CPU 使用率 | 記憶體佔用 |
|--------|----------|------------|------------|
| **1600×900** | 50-100ms | < 15% | < 200MB |
| **1920×1080** | 80-150ms | < 25% | < 300MB |
| **2560×1440** | 150-250ms | < 35% | < 500MB |

## 使用指南

### 基本操作流程

1. **啟動遊戲**
   - 確保 MapleStory Worlds-Artale 正在運行
   - 設定遊戲為視窗化模式（推薦 1600×900）

2. **選擇功能模式**
   - **主控台模式**：系統狀態監控與日誌查看
   - **路徑編輯模式**：靜態小地圖編輯與路徑規劃
   - **即時顯示模式**：可視化除錯工具（顯示偵測結果疊加效果）

3. **設定檢測目標**
   - 從模板庫選擇或匯入怪物模板
   - 調整檢測閾值與敏感度參數
   - 設定檢測區域與排除區域

4. **啟動自動化功能**
   - **自動打怪**：選定怪物模板和辨識模式後，點擊「自動打怪」
   - **路徑規劃**：載入路徑檔案後，點擊「路徑規劃啟動」
   - **背景運行**：所有功能可在任何分頁運行，不需要切換到即時顯示分頁
   - **即時顯示**：需要查看視覺化效果時，切換到即時顯示分頁

5. **優化檢測模式**
   - 根據環境光線選擇最佳檢測模式
   - 調整 NMS 參數避免重複檢測
   - 監控效能指標並進行調整

### 進階功能使用

#### 自定義模板創建
1. 使用遊戲內截圖工具擷取目標怪物
2. 裁切為純淨的怪物影像（建議 64×64 像素）
3. 儲存為 PNG 格式到 `Templates/monsters/` 目錄
4. 在設定檔中註冊新模板

#### 路徑規劃與錄製

**方式一：自動路徑錄製**（推薦）
1. 在任何分頁按 **F1** 開始錄製
2. 系統自動啟動背景畫面擷取
3. 在遊戲中移動角色，系統自動記錄座標
4. 按 **F1** 停止錄製
5. 按 **F3** 儲存路徑（自動去重 + 排序）
6. 按 **Ctrl+S** 儲存地圖檔案

**方式二：手動地圖編輯**
1. 切換到「路徑編輯」分頁
2. 選擇編輯模式（路徑點/安全區/限制區/繩索）
3. 在小地圖上點擊設置路徑點
4. 按 **Ctrl+S** 儲存地圖檔案

**路徑優化說明**
- 錄製的路徑會自動去除所有重複座標
- 座標按 X、Y 順序排列，JSON 檔案整齊美觀
- 優化後的路徑適合隨機目標點路徑規劃
- 原始錄製可能 100+ 點，優化後約 30-50 個唯一座標

**自動路徑規劃與角色移動**
1. 載入路徑檔案：從「載入路徑檔」下拉選單選擇已錄製的路徑檔案
2. 啟動路徑規劃：
   - 點擊「自動打怪」CheckBox（會自動啟動背景擷取和路徑規劃）
   - 或點擊「路徑規劃啟動」RadioButton
3. 系統自動運行：
   - 背景畫面擷取（不需要切換分頁）
   - 小地圖位置定位
   - 隨機選擇目標點
   - 自動控制角色移動（使用方向鍵 ↑↓←→）
   - 到達目標後自動選擇下一個隨機目標點
4. 可視化監控：
   - 切換到「即時顯示」分頁查看視覺化效果
   - 顯示偵測結果、路徑線、玩家位置等疊加效果
   - 用於除錯和監控，不影響功能運行