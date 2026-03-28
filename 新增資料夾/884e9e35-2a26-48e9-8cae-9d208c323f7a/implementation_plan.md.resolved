# Optimization: Faster RTSP Startup and Rendering

用戶反映程式啟動後需要等待較長時間才能看到畫面。這主要是由於 OpenCV 在初始化 RTSP 連接時，FFmpeg 後端會進行預設的「串流分析 (Probing)」，這通常會耗費數秒。

## User Review Required

> [!TIP]
> **建議傳輸協定選擇**：
> *   如果你在區域網路內且頻寬充足，使用 `udp` 速度最快且延遲最低。
> *   若網路不穩定或有防火牆，建議使用 `tcp` 以確保影像完整性，但握手時間略長。
> 計畫預設使用 `udp` 並搭配極低的探測參數。

## Proposed Changes

### CameraMonitor Component

#### [MODIFY] [MainWindow.xaml.cs](file:///d:/Full_end/C%23/CameraMonitor/CameraMonitor/MainWindow.xaml.cs)
*   **全域設定**：在 `OnLoaded` 中加入 `Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;udp|probesize;32|analyzeduration;0")`。
*   **VideoCapture 優化**：
    *   明確指定使用 `VideoCaptureAPIs.FFMPEG`。
    *   設定 `BufferSize` 屬性為 1 以降低即時延遲。
*   **UI 反饋**：在初始化 `VideoCapture` 前，先確保介面有基本的佔位視覺（目前已有深色背景，但可考慮加入文字提示）。

## Open Questions
*   **網路環境**：您的攝影機是否支援 UDP 傳輸？如果不支援，我們需要將 `rtsp_transport` 改為 `tcp`。
*   **解析度**：是否需要對影像進行縮放 (Resize)？如果攝影機解析度極高 (4K)，在渲染前進行縮放也能提升感官上的流暢度。

## Verification Plan

### Automated Tests
*   執行 `dotnet run` 並記錄從「程式啟動」到「第一張張影像出現」的時間。
*   與修復前的日誌（若有）進行對比。

### Manual Verification
*   確認 8 路畫面是否能正常載入且無花屏現象。
*   確認調整 `probesize` 後，連線是否依然穩定。
