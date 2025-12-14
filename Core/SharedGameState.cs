using System;
using System.Drawing;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRectangle = System.Drawing.Rectangle;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 共享遊戲狀態中心 - 執行緒安全的資料中心
    /// 負責在視覺核心、Bot邏輯和UI介面之間共享即時遊戲狀態
    /// 採用「公告板」模式：視覺核心貼資料，Bot和UI各自讀取
    /// </summary>
    public class SharedGameState
    {
        private readonly object _lock = new object();

        // --- 核心資料（給 Bot 用）---
        private SdPointF _playerPosition = SdPointF.Empty;
        private bool _isPlayerFound = false;

        // --- 視覺資料（給 UI 用）---
        private SdRectangle _minimapBounds = SdRectangle.Empty;
        private Bitmap? _latestFrame = null; // 僅用於顯示

        // --- 額外狀態資訊 ---
        private double _trackingConfidence = 0.0;
        private DateTime _lastUpdateTime = DateTime.MinValue;

        /// <summary>
        /// 更新核心遊戲狀態（由視覺核心呼叫）
        /// </summary>
        /// <param name="playerPos">玩家位置（相對於小地圖左上角的像素座標）</param>
        /// <param name="isFound">是否成功找到玩家</param>
        /// <param name="mapBounds">小地圖邊界（螢幕座標）</param>
        /// <param name="confidence">追蹤置信度（可選）</param>
        public void UpdateState(SdPointF playerPos, bool isFound, SdRectangle mapBounds, double confidence = 1.0)
        {
            lock (_lock)
            {
                _playerPosition = playerPos;
                _isPlayerFound = isFound;
                _minimapBounds = mapBounds;
                _trackingConfidence = confidence;
                _lastUpdateTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 更新畫面快取（由視覺核心呼叫，頻率可以較低）
        /// </summary>
        /// <param name="frame">最新影格圖像</param>
        public void UpdateFrame(Bitmap frame)
        {
            lock (_lock)
            {
                // 釋放舊圖，避免記憶體洩漏
                _latestFrame?.Dispose();
                _latestFrame = (Bitmap)frame.Clone();
            }
        }

        /// <summary>
        /// 取得玩家狀態（給 Bot 用）
        /// </summary>
        /// <returns>玩家位置和是否找到的元組</returns>
        public (SdPointF pos, bool found) GetPlayerState()
        {
            lock (_lock)
            {
                return (_playerPosition, _isPlayerFound);
            }
        }

        /// <summary>
        /// 取得渲染資料（給 UI 用）
        /// </summary>
        /// <returns>圖像、小地圖邊界、玩家位置的元組</returns>
        public (Bitmap? img, SdRectangle bounds, SdPointF pos) GetRenderData()
        {
            lock (_lock)
            {
                // 複製 Bitmap 避免 UI 執行緒和視覺執行緒衝突
                // 注意：這裡的成本較高，但為了執行緒安全是必要的
                return (
                    _latestFrame != null ? (Bitmap)_latestFrame.Clone() : null,
                    _minimapBounds,
                    _playerPosition
                );
            }
        }

        /// <summary>
        /// 取得完整狀態（包含所有資訊）
        /// </summary>
        /// <returns>完整狀態資訊</returns>
        public GameStateSnapshot GetFullState()
        {
            lock (_lock)
            {
                return new GameStateSnapshot
                {
                    PlayerPosition = _playerPosition,
                    IsPlayerFound = _isPlayerFound,
                    MinimapBounds = _minimapBounds,
                    TrackingConfidence = _trackingConfidence,
                    LastUpdateTime = _lastUpdateTime
                };
            }
        }

        /// <summary>
        /// 清空狀態
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _playerPosition = SdPointF.Empty;
                _isPlayerFound = false;
                _minimapBounds = SdRectangle.Empty;
                _trackingConfidence = 0.0;
                _lastUpdateTime = DateTime.MinValue;
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        /// <summary>
        /// 釋放資源
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }
    }

    /// <summary>
    /// 遊戲狀態快照
    /// 用於一次性取得完整狀態資訊
    /// </summary>
    public class GameStateSnapshot
    {
        public SdPointF PlayerPosition { get; set; }
        public bool IsPlayerFound { get; set; }
        public SdRectangle MinimapBounds { get; set; }
        public double TrackingConfidence { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
}

