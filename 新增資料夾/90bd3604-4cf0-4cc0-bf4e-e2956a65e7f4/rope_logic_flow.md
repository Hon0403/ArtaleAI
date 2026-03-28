# 繩索攀爬完整機制流程圖

```mermaid
flowchart TD
    subgraph 主程式迴圈 [MainForm 主迴圈 每100ms]
        A1([開始]) --> A2[取得玩家位置]
        A2 --> A3[取得當前目標]
        A3 --> A4{目標動作類型?}
        
        A4 -->|ClimbUp 往上爬| B1[呼叫 ClimbRopeAsync]
        A4 -->|ClimbDown 往下爬| B1
        A4 -->|Idle 或 Move| C1[呼叫 MoveToTargetAsync]
    end

    subgraph 路徑規劃器 [PathPlanningTracker]
        P1[SelectNextTarget] --> P2{需要繩索嗎?}
        P2 -->|目標在不同樓層| P3[FindNearbyRope]
        P3 --> P4{找到繩索?}
        P4 -->|是| P5[設定臨時目標為繩索]
        P4 -->|否| P6[SmartRopeNavigator BFS搜尋]
        P6 --> P7{找到路徑?}
        P7 -->|是| P5
        P7 -->|否| P8[維持原目標]
        P2 -->|同一層| P8
    end

    B1 --> D1

    subgraph 爬繩控制器 [ClimbRopeAsync]
        D1[Phase 1: 對齊繩索] --> D2{X軸誤差小於1.5px?}
        D2 -->|否| D3[AlignWithRopeAsync 微調]
        D3 --> D4{對齊成功?}
        D4 -->|否 超時1.3秒| D5[Side Jump 側跳嘗試]
        D5 --> D2
        D4 -->|是| D6[Phase 2: 抓繩]
        D2 -->|是| D6
        
        D6 --> D7{爬繩方向?}
        D7 -->|往上| D8[Alt加上鍵 跳抓]
        D7 -->|往下| D9[按下鍵 穿透平台]
        
        D8 --> D10[Phase 3: 爬繩迴圈]
        D9 --> D10
        
        D10 --> E1
    end

    subgraph 爬繩迴圈 [PerformGradualMoveAsync]
        E1{檢查結束條件} --> E2{到達目標Y?}
        E2 -->|是| F1[結束爬繩]
        
        E2 -->|否| E3{接近目標 小於8px?}
        E3 -->|是 每10次| E4[Wiggle Test 搖擺測試]
        E4 --> E5{能水平移動嗎?}
        E5 -->|是 離開繩索了| F1
        E5 -->|否 還在繩索上| E6
        
        E3 -->|否| E6{X軸偏移大於5px?}
        E6 -->|是 被打或滑掉| F2[緊急停止]
        E6 -->|否| E7[繼續按住上或下鍵]
        E7 --> E8{距離大於5px?}
        E8 -->|是| E9[長按80ms]
        E8 -->|否| E10[短按30ms 微調]
        E9 --> E1
        E10 --> E1
    end

    F1 --> G1[釋放所有按鍵]
    F2 --> G1
    G1 --> G2[ForceAdvanceTarget 強制推進]
    G2 --> G3[清除臨時目標]
    G3 --> G4([返回主迴圈])
    
    G4 --> A1

    C1 --> H1[水平移動邏輯]
    H1 --> A1

    style F2 fill:#f96,stroke:#333
    style F1 fill:#9f9,stroke:#333
    style D5 fill:#ff9,stroke:#333
    style E4 fill:#9ff,stroke:#333
```

---

## 各機制說明

### 1. 主程式迴圈 (MainForm)
- 每 **100ms** 執行一次
- 根據 `PathActionType` 決定執行動作

### 2. 路徑規劃器 (PathPlanningTracker)
- **FindNearbyRope**: 找附近可用的繩索
- **SmartRopeNavigator**: BFS 搜尋多層繩索路徑

### 3. 對齊機制 (AlignWithRopeAsync)
- 用 **30ms 短按** 微調 X 軸位置
- 超時 **1.3秒** 觸發 **Side Jump** (跳躍+方向鍵)

### 4. 抓繩機制
- **往上**: `Alt + ↑` (跳躍抓繩)
- **往下**: `↓` (穿透平台)

### 5. 爬繩迴圈
- **長按 80ms**: 距離 > 5px
- **短按 30ms**: 距離 < 5px (微調)

### 6. 結束條件
| 條件 | 說明 |
|------|------|
| 到達目標 Y | 正常完成 |
| Wiggle Test 成功 | 確認已離開繩索站上平台 |
| X 軸偏移 > 5px | 被擊退或滑掉，緊急停止 |

### 7. 強制推進 (ForceAdvanceTarget)
- 標記當前目標完成
- **清除臨時目標** (防止鬼打牆)
- 推進到下一個路徑點
