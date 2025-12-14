using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 路徑錄製服務 - 參考 routeRecorder.py 的功能
    /// 支援動作識別和顏色標記
    /// </summary>
    public class RouteRecorderService : IDisposable
    {
        #region 動作類型枚舉

        /// <summary>
        /// 玩家動作類型（對應 routeRecorder.py 的 action）
        /// </summary>
        public enum ActionType
        {
            None,                    // 無動作
            Left,                    // 左
            Right,                   // 右
            Up,                      // 上
            Down,                    // 下
            LeftJump,                // 左+跳躍
            RightJump,               // 右+跳躍
            DownJump,                // 下+跳躍
            Jump,                    // 跳躍
            LeftTeleport,            // 左+傳送
            RightTeleport,           // 右+傳送
            UpTeleport,              // 上+傳送
            DownTeleport,            // 下+傳送
            Goal                     // 目標點
        }

        #endregion

        #region 路徑點結構

        /// <summary>
        /// 路徑點（包含位置和動作類型）
        /// </summary>
        public class RoutePoint
        {
            public SdPointF Position { get; set; }
            public ActionType Action { get; set; }
            public DateTime Timestamp { get; set; }

            public RoutePoint(SdPointF position, ActionType action)
            {
                Position = position;
                Action = action;
                Timestamp = DateTime.UtcNow;
            }
        }

        #endregion

        #region 私有欄位

        private bool _isDisposed = false;
        private bool _isRecording = false;
        private readonly List<RoutePoint> _recordedPoints = new();
        private readonly object _lockObject = new object();
        private DateTime _lastRecordTime = DateTime.MinValue;
        private const double MinRecordDistance = 0.5;   // 最小移動距離（像素）
        private const int MinRecordIntervalMs = 50;     // 最小間隔（毫秒）

        // 按鍵狀態監聽
        private CancellationTokenSource? _keyListenerCancellationToken;
        private Task? _keyListenerTask;

        // 動作到顏色的映射（參考 routeRecorder.py 的 color_code）
        private static readonly Dictionary<ActionType, Color> _actionColors = new()
        {
            { ActionType.None, Color.White },
            { ActionType.Left, Color.Blue },
            { ActionType.Right, Color.Red },
            { ActionType.Up, Color.Green },
            { ActionType.Down, Color.Yellow },
            { ActionType.LeftJump, Color.Cyan },
            { ActionType.RightJump, Color.Magenta },
            { ActionType.DownJump, Color.Orange },
            { ActionType.Jump, Color.Purple },
            { ActionType.LeftTeleport, Color.Pink },
            { ActionType.RightTeleport, Color.Lime },
            { ActionType.UpTeleport, Color.Aqua },
            { ActionType.DownTeleport, Color.Coral },
            { ActionType.Goal, Color.Gold }
        };

        #endregion

        #region Windows API（用於按鍵檢測）

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;
        private const int VK_LMENU = 0xA4;      // 左側 Alt（跳躍鍵）
        private const int VK_E = 0x45;          // 傳送（可配置）

        #endregion

        #region 公共屬性

        /// <summary>
        /// 是否正在錄製
        /// </summary>
        public bool IsRecording => _isRecording;

        /// <summary>
        /// 已錄製的點數
        /// </summary>
        public int RecordedPointCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _recordedPoints.Count;
                }
            }
        }

        /// <summary>
        /// 已錄製的路徑點（唯讀）
        /// </summary>
        public IReadOnlyList<RoutePoint> RecordedPoints
        {
            get
            {
                lock (_lockObject)
                {
                    return _recordedPoints.ToList();
                }
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 開始錄製路徑
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording) return;

            lock (_lockObject)
            {
                _isRecording = true;
                _recordedPoints.Clear();
                _lastRecordTime = DateTime.MinValue;

                // 啟動按鍵監聽（用於實時動作識別）
                _keyListenerCancellationToken = new CancellationTokenSource();
                _keyListenerTask = Task.Run(() => KeyListenerLoop(_keyListenerCancellationToken.Token));
            }
        }

        /// <summary>
        /// 停止錄製路徑
        /// </summary>
        public void StopRecording()
        {
            if (!_isRecording) return;

            lock (_lockObject)
            {
                _isRecording = false;
                _keyListenerCancellationToken?.Cancel();
                _keyListenerTask?.Wait(1000);
                _keyListenerCancellationToken?.Dispose();
                _keyListenerCancellationToken = null;
                _keyListenerTask = null;
            }
        }

        /// <summary>
        /// 錄製一個路徑點（由外部調用，例如從 MainForm）
        /// </summary>
        public void RecordPoint(SdPointF position)
        {
            if (!_isRecording) return;

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRecordTime).TotalMilliseconds;

            // 檢查時間間隔和位置變化
            if (elapsed >= MinRecordIntervalMs && ShouldAddNewPoint(position))
            {
                // 識別當前動作
                var action = DetectAction();

                lock (_lockObject)
                {
                    _recordedPoints.Add(new RoutePoint(position, action));
                    _lastRecordTime = now;
                }
            }
        }

        /// <summary>
        /// 添加目標點（手動標記）
        /// </summary>
        public void AddGoalPoint(SdPointF position)
        {
            if (!_isRecording) return;

            lock (_lockObject)
            {
                _recordedPoints.Add(new RoutePoint(position, ActionType.Goal));
            }
        }

        /// <summary>
        /// 清除所有錄製的點
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _recordedPoints.Clear();
                _lastRecordTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 取得動作對應的顏色
        /// </summary>
        public static Color GetActionColor(ActionType action)
        {
            return _actionColors.TryGetValue(action, out var color) ? color : Color.White;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 檢測當前按鍵狀態並識別動作（參考 routeRecorder.py 的邏輯）
        /// </summary>
        private ActionType DetectAction()
        {
            lock (_lockObject)
            {
                bool hasLeft = IsKeyPressed(VK_LEFT);
                bool hasRight = IsKeyPressed(VK_RIGHT);
                bool hasUp = IsKeyPressed(VK_UP);
                bool hasDown = IsKeyPressed(VK_DOWN);
                bool hasAlt = IsKeyPressed(VK_LMENU);  // 左側 Alt（跳躍鍵）
                bool hasE = IsKeyPressed(VK_E);

                // 參考 routeRecorder.py 的動作識別邏輯
                // 注意：上箭頭和下箭頭通常是在繩索上上下移動，不是跳躍
                if (hasAlt) // 跳躍（左側 Alt）
                {
                    if (hasLeft) return ActionType.LeftJump;
                    if (hasRight) return ActionType.RightJump;
                    if (hasDown) return ActionType.DownJump;
                    return ActionType.Jump;
                }
                else if (hasE) // 傳送
                {
                    if (hasLeft) return ActionType.LeftTeleport;
                    if (hasRight) return ActionType.RightTeleport;
                    if (hasUp) return ActionType.UpTeleport;
                    if (hasDown) return ActionType.DownTeleport;
                }
                else if (hasUp) return ActionType.Up;      // 上箭頭：繩索上移動
                else if (hasDown) return ActionType.Down;  // 下箭頭：繩索下移動
                else if (hasLeft) return ActionType.Left;
                else if (hasRight) return ActionType.Right;

                return ActionType.None;
            }
        }

        /// <summary>
        /// 檢查按鍵是否被按下
        /// </summary>
        private bool IsKeyPressed(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        /// <summary>
        /// 按鍵監聽循環（持續更新按鍵狀態）
        /// 注意：實際的按鍵檢測在 DetectAction() 中通過 GetAsyncKeyState 實時進行
        /// 這個循環主要用於保持服務運行狀態
        /// </summary>
        private void KeyListenerLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRecording)
            {
                // 按鍵狀態通過 GetAsyncKeyState 實時檢測，無需額外追蹤
                Thread.Sleep(10); // 10ms 更新一次
            }
        }

        /// <summary>
        /// 判斷是否應該添加新點（基於距離）
        /// </summary>
        private bool ShouldAddNewPoint(SdPointF newPos)
        {
            lock (_lockObject)
            {
                if (_recordedPoints.Count == 0) return true;

                var last = _recordedPoints[^1].Position;
                var dx = newPos.X - last.X;
                var dy = newPos.Y - last.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                return dist >= MinRecordDistance;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isDisposed) return;

            StopRecording();
            Clear();
            _isDisposed = true;
        }

        #endregion
    }
}

