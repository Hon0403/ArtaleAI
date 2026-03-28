# [Fix] Navigation Timeout & Destination Oscillation

修正角色的導航超時導致的頻繁重定位（Ping-Pong）問題。

## User Review Required

> [!IMPORTANT]
> **移動速度參數調整**：將導航預期速度從 65 px/s 調降至 20 px/s。這會顯著增加超時容許範圍，減少在長距離移動時頻繁觸發救援流程。

## Proposed Changes

### Navigation Services

#### [MODIFY] [NavigationExecutor.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/NavigationExecutor.cs)
- 將 `DefaultWalkSpeed` 從 `65.0f` 下修至 `20.0f`。
- 將 `DefaultClimbSpeed` 從 `55.0f` 下修至 `15.0f`。
- 將 `BaseBufferMs` 從 `2500` 提高至 `4000`。
- 優化 `ExecuteWalkAsync`：若角色已極度接近目標（如 < 15px），超時後進入「Final Adjustment」階段而非直接失敗。

## Verification Plan

### Automated Tests
- 觀察 `artale-*.log` 中的 `moveTimeoutMs` 計算值。
- 對於 100 像素的移動，預期超時時間應從 `4.0s` 變更為 `100/20 + 4 = 9.0s`。

### Manual Verification
- 在地圖長距離走動，確認不會在路徑中途突然「更新終點」或「重置進度」。
