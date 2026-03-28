using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.Services;
using ArtaleAI.UI.MapEditing;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using YamlDotNet.Serialization;


namespace ArtaleAI.UI
{
    /// <summary>
    /// 負責管理所有地圖編輯的核心邏輯、數據狀態和繪製。
    /// </summary>
    public class MapEditor
    {
        private const int SmartSideJumpActionCode = 13;

        private MapData _currentMapData = new MapData();
        private EditMode _currentEditMode = EditMode.None;
        private readonly AppConfig _settings;

        private PointF? _startPoint = null;
        private PointF? _previewPoint = null;
        private Rectangle minimapBounds = Rectangle.Empty;

        /// <summary>
        /// 初始化地圖編輯器
        /// </summary>
        /// <param name="settings">應用程式設定</param>
        public MapEditor(AppConfig settings)
        {
            _settings = settings;
        }

        // 視覺化常數
        private const float PointRadius = 4.0f;           // 節點半徑
        private const float SelectionRadius = 2.0f;       // 選取判定半徑（需精準指向節點）

        // 狀態欄位
        private int _selectedNodeIndex = -1;              // 當前選取的節點索引
        private int _hoveredNodeIndex = -1;               // 當前滑鼠懸停的節點索引
        private int _currentActionType = 0;               // 當前預設的動作類型 (0=Walk)
        private int _waypointAnchorIndex = -1;             // Waypoint 模式的錨點節點（下個新節點將從此連出）

        public event Action<int>? OnNodeSelected;         // 當節點被選取時的事件 (參數: actionType)

        /// <summary>
        /// 將舊版 Editor 連線 actionCode 轉為新版 NavigationActionType。
        /// A2：不再允許 LegacyMapMigrator 進行 runtime 遷移，但編輯器仍需解析舊 action code 建立邊。
        /// </summary>
        private static ArtaleAI.Core.Domain.Navigation.NavigationActionType ConvertActionCode(int oldActionCode)
        {
            return oldActionCode switch
            {
                1 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.Walk,
                2 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.Walk,
                3 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbUp,
                4 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbDown,
                5 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpLeft,
                6 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpRight,
                7 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpDown,
                8 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.Jump,
                9 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpLeft,
                10 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpRight,
                11 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbUp,
                12 => ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbDown,
                _ => ArtaleAI.Core.Domain.Navigation.NavigationActionType.Walk
            };
        }

        private static int ResolveActionCodeByGeometry(PointF fromPoint, PointF toPoint, int actionCode)
        {
            if (actionCode != SmartSideJumpActionCode)
            {
                return actionCode;
            }

            float dx = toPoint.X - fromPoint.X;
            return dx < 0 ? 9 : 10;
        }

        /// <summary>
        /// 設定當前使用的動作類型 (新增節點時使用)
        /// </summary>
        public void SetCurrentActionType(int actionType)
        {
            _currentActionType = actionType;
            Logger.Info($"[編輯器] SetCurrentActionType 被呼叫: actionType={actionType}");
            // 如果有選取節點，同時更新該節點的動作
            if (_selectedNodeIndex != -1 && _currentMapData.WaypointPaths != null)
            {
                UpdateSelectedNodeAction(actionType);
                ApplyActionToSelectedNodeConnections();
            }
        }

        /// <summary>
        /// 更新選取節點的動作
        /// </summary>
        private void UpdateSelectedNodeAction(int actionType)
        {
            if (_selectedNodeIndex < 0 || _currentMapData.WaypointPaths == null ||
                _selectedNodeIndex >= _currentMapData.WaypointPaths.Count) return;

            var node = _currentMapData.WaypointPaths[_selectedNodeIndex];
            // 節點格式: [x, y, action]
            // 🛡️ 方案 C: 嚴禁將動作設定在「分隔標記」上 (X < 0)
            if (node.Length >= 2 && node[0] < 0)
            {
                Logger.Warning($"[編輯器] 試圖對分隔標記設定動作，已攔截。Index: {_selectedNodeIndex}");
                return;
            }

            if (node.Length >= 2)
            {
                // 智慧側跳是邊語意，節點本身不直接存 13，避免污染資料檔。
                if (actionType == SmartSideJumpActionCode)
                {
                    return;
                }

                if (node.Length < 3)
                {
                    // 擴展陣列以包含動作
                    var newNode = new float[] { node[0], node[1], (float)actionType };
                    _currentMapData.WaypointPaths[_selectedNodeIndex] = newNode;
                }
                else
                {
                    // 更新現有動作
                    node[2] = (float)actionType;
                }
                Logger.Info($"[編輯器] 更新節點 {_selectedNodeIndex} 動作為 {actionType}");
            }
        }

        /// <summary>
        /// 在選取模式下，將目前下拉動作套用到該節點連線。
        /// - 一般動作：維持既有行為，套用到該節點所有出邊
        /// - 智慧側跳：只處理「單一邊界跳邊目標」，避免一次污染整段路徑
        /// </summary>
        private void ApplyActionToSelectedNodeConnections()
        {
            if (_selectedNodeIndex < 0 || _currentMapData.WaypointPaths == null || _currentMapData.Connections == null)
            {
                return;
            }

            var fromNode = _currentMapData.WaypointPaths[_selectedNodeIndex];
            if (fromNode.Length < 2 || fromNode[0] < 0) return;

            // 使用者操作語意收斂：
            // Select + SmartSideJump 一次只設定一個「邊界跳邊」，不批次改整個節點所有出邊。
            if (_currentActionType == SmartSideJumpActionCode)
            {
                bool updated = false;
                bool reverseUpdated = false;

                if (TryResolveSmartSideJumpTarget(_selectedNodeIndex, out int toIdx))
                {
                    int resolvedAction = ResolveActionCodeByGeometry(
                        new PointF(fromNode[0], fromNode[1]),
                        new PointF(_currentMapData.WaypointPaths[toIdx][0], _currentMapData.WaypointPaths[toIdx][1]),
                        SmartSideJumpActionCode);

                    bool forwardUpdated = UpsertConnectionAction(_selectedNodeIndex, toIdx, resolvedAction);
                    if (forwardUpdated)
                    {
                        updated = true;
                        reverseUpdated = EnsureReverseJumpConnection(_selectedNodeIndex, toIdx, resolvedAction);
                        Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 已套用 SmartSideJump 到邊界目標節點 {toIdx}。");
                    }
                }

                if (updated)
                {
                    Logger.Info($"[編輯器] 套用節點 {_selectedNodeIndex} 的出邊動作為 {GetActionName(_currentActionType)}");
                    if (reverseUpdated)
                    {
                        Logger.Info($"[編輯器] 已自動補齊節點 {_selectedNodeIndex} 相關反向跳邊。");
                    }
                }
                else
                {
                    Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 沒有可更新的邊界跳邊（未變更）。");
                }
                return;
            }

            bool regularUpdated = false;
            int originalConnectionCount = _currentMapData.Connections.Count;

            for (int i = 0; i < originalConnectionCount; i++)
            {
                var conn = _currentMapData.Connections[i];
                if (conn.Length < 4 || conn[0] != _selectedNodeIndex) continue;
                int toIdx = conn[1];
                if (toIdx < 0 || toIdx >= _currentMapData.WaypointPaths.Count) continue;

                var toNode = _currentMapData.WaypointPaths[toIdx];
                // 智慧側跳只針對跨分段邊生效；同段連線維持原動作，避免整段被誤轉為跳躍。
                if (_currentActionType == SmartSideJumpActionCode && !HasSeparatorBetween(_selectedNodeIndex, toIdx))
                {
                    continue;
                }

                int resolvedAction = ResolveActionCodeByGeometry(
                    new PointF(fromNode[0], fromNode[1]),
                    new PointF(toNode[0], toNode[1]),
                    _currentActionType);

                conn[3] = resolvedAction;
                regularUpdated = true;
            }

            if (regularUpdated)
            {
                Logger.Info($"[編輯器] 套用節點 {_selectedNodeIndex} 的出邊動作為 {GetActionName(_currentActionType)}");
            }
            else
            {
                Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 沒有可更新的出邊（未變更）。");
            }
        }

        private bool TryResolveSmartSideJumpTarget(int fromIdx, out int bestToIdx)
        {
            bestToIdx = -1;
            if (_currentMapData.WaypointPaths == null || _currentMapData.Connections == null)
            {
                return false;
            }

            var fromNode = _currentMapData.WaypointPaths[fromIdx];
            float bestDistanceSq = float.MaxValue;

            foreach (var conn in _currentMapData.Connections)
            {
                if (conn.Length < 2 || conn[0] != fromIdx) continue;
                int candidateTo = conn[1];
                if (candidateTo < 0 || candidateTo >= _currentMapData.WaypointPaths.Count) continue;
                if (!HasSeparatorBetween(fromIdx, candidateTo)) continue;

                var toNode = _currentMapData.WaypointPaths[candidateTo];
                float dx = toNode[0] - fromNode[0];
                float dy = toNode[1] - fromNode[1];
                float distanceSq = dx * dx + dy * dy;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestToIdx = candidateTo;
                }
            }

            return bestToIdx >= 0;
        }

        private bool UpsertConnectionAction(int fromIdx, int toIdx, int actionCode)
        {
            if (_currentMapData.Connections == null)
            {
                return false;
            }

            var existing = _currentMapData.Connections.FirstOrDefault(c =>
                c.Length >= 2 && c[0] == fromIdx && c[1] == toIdx);

            if (existing == null)
            {
                _currentMapData.Connections.Add(new[] { fromIdx, toIdx, 1, actionCode });
                return true;
            }

            if (existing.Length >= 4)
            {
                if (existing[3] == actionCode) return false;
                existing[3] = actionCode;
                return true;
            }

            int idx = _currentMapData.Connections.IndexOf(existing);
            if (idx >= 0)
            {
                _currentMapData.Connections[idx] = new[] { fromIdx, toIdx, 1, actionCode };
                return true;
            }

            return false;
        }

        /// <summary>
        /// 為何集中單一路徑：Link / 路線標記 過去各自複製「解析動作 + 寫 Connections + 同步節點 [2]」，
        /// 且舊 Link 用雙向 FirstOrDefault 可能改錯到反向邊的 action。此處一律以「使用者指定的 from→to 有向邊」為準。
        /// </summary>
        private void ApplyDirectedConnection(
            int fromIdx,
            int toIdx,
            int uiActionCode,
            bool ensureReverseJumpIfSideJump,
            out int resolvedAction)
        {
            resolvedAction = 0;
            _currentMapData.WaypointPaths ??= new List<float[]>();
            _currentMapData.Connections ??= new List<int[]>();

            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx) return;
            if (fromIdx >= _currentMapData.WaypointPaths.Count || toIdx >= _currentMapData.WaypointPaths.Count) return;

            var fromW = _currentMapData.WaypointPaths[fromIdx];
            var toW = _currentMapData.WaypointPaths[toIdx];
            if (fromW.Length < 2 || fromW[0] < 0 || toW.Length < 2 || toW[0] < 0) return;

            resolvedAction = ResolveActionCodeByGeometry(
                new PointF(fromW[0], fromW[1]),
                new PointF(toW[0], toW[1]),
                uiActionCode);

            UpsertConnectionAction(fromIdx, toIdx, resolvedAction);

            if (resolvedAction != 0)
            {
                var fn = _currentMapData.WaypointPaths[fromIdx];
                if (fn.Length >= 3)
                {
                    fn[2] = resolvedAction;
                }
                else if (fn.Length == 2)
                {
                    _currentMapData.WaypointPaths[fromIdx] = new[] { fn[0], fn[1], (float)resolvedAction };
                }
            }

            if (ensureReverseJumpIfSideJump && (resolvedAction == 9 || resolvedAction == 10))
            {
                EnsureReverseJumpConnection(fromIdx, toIdx, resolvedAction);
            }

            PruneSelfLoopConnections();
        }

        private static void PruneSelfLoopConnections(MapData data)
        {
            if (data.Connections == null) return;
            for (int i = data.Connections.Count - 1; i >= 0; i--)
            {
                var conn = data.Connections[i];
                if (conn.Length >= 2 && conn[0] == conn[1])
                {
                    data.Connections.RemoveAt(i);
                    Logger.Info($"[編輯器] 自動清理自我連結: {conn[0]} -> {conn[0]}");
                }
            }
        }

        private void PruneSelfLoopConnections() => PruneSelfLoopConnections(_currentMapData);

        /// <summary>
        /// 方案 B + 智慧側跳輔助：
        /// 當使用者套用智慧側跳時，自動確保反向跳邊存在，避免存檔被完整性檢查阻擋。
        /// </summary>
        private bool EnsureReverseJumpConnection(int fromIdx, int toIdx, int resolvedForwardAction)
        {
            if (_currentMapData.Connections == null) return false;
            if (resolvedForwardAction != 9 && resolvedForwardAction != 10) return false;

            int expectedReverseAction = resolvedForwardAction == 9 ? 10 : 9;

            var reverseConn = _currentMapData.Connections.FirstOrDefault(c =>
                c.Length >= 2 &&
                c[0] == toIdx &&
                c[1] == fromIdx);

            if (reverseConn == null)
            {
                _currentMapData.Connections.Add(new[] { toIdx, fromIdx, 1, expectedReverseAction });

                if (_currentMapData.WaypointPaths != null && toIdx >= 0 && toIdx < _currentMapData.WaypointPaths.Count)
                {
                    var reverseNode = _currentMapData.WaypointPaths[toIdx];
                    if (reverseNode.Length >= 3)
                    {
                        reverseNode[2] = expectedReverseAction;
                    }
                }

                return true;
            }

            if (reverseConn.Length < 4)
            {
                int reverseIdx = _currentMapData.Connections.IndexOf(reverseConn);
                if (reverseIdx != -1)
                {
                    _currentMapData.Connections[reverseIdx] = new[] { toIdx, fromIdx, 1, expectedReverseAction };
                    return true;
                }
                return false;
            }

            if (reverseConn[3] != expectedReverseAction)
            {
                reverseConn[3] = expectedReverseAction;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判斷兩個 waypoint 索引之間是否跨越分隔標記（[-1, -1, -1]）。
        /// 用來避免把同段 Walk 邊誤改成智慧側跳。
        /// </summary>
        private bool HasSeparatorBetween(int fromIdx, int toIdx)
        {
            if (_currentMapData.WaypointPaths == null || _currentMapData.WaypointPaths.Count == 0)
            {
                return false;
            }

            int start = Math.Min(fromIdx, toIdx) + 1;
            int end = Math.Max(fromIdx, toIdx) - 1;
            for (int i = start; i <= end; i++)
            {
                var mid = _currentMapData.WaypointPaths[i];
                if (mid.Length >= 2 && mid[0] < 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 設定小地圖的邊界範圍（螢幕座標）
        /// </summary>
        /// <param name="bounds">小地圖在螢幕上的矩形區域</param>
        public void SetMinimapBounds(Rectangle bounds)
        {
            minimapBounds = bounds;
        }

        /// <summary>
        /// 載入地圖資料到編輯器
        /// </summary>
        /// <param name="data">要載入的地圖資料（null 時會建立空地圖）</param>
        public void LoadMapData(MapData data)
        {
            _currentMapData = data ?? new MapData();
            _selectedNodeIndex = -1;
            _waypointAnchorIndex = -1;
            _startPoint = null;
            _previewPoint = null;

            // A 作法：暫時保留舊欄位以支援 UI 互動，但在載入新版 Nodes/Edges 時自動生成 WaypointPaths/Connections，
            // 讓編輯器在「舊欄位缺失」的過渡階段仍能工作。
            EnsureLegacyWaypointDataForEditor();
        }

        /// <summary>
        /// A：過渡期保證編輯器 UI 可用
        /// - 若載入資料已含 Nodes/Edges 但缺少 WaypointPaths/Connections，則根據 Nodes/Edges 生成臨時用的舊欄位
        /// - Runtime 仍不依賴這些欄位（SSOT/導航皆走 Nodes/Edges），這只為了讓編輯器互動不中斷
        /// </summary>
        private void EnsureLegacyWaypointDataForEditor()
        {
            if (_currentMapData == null) return;

            bool hasLegacyNodes = _currentMapData.WaypointPaths != null && _currentMapData.WaypointPaths.Count > 0;
            if (hasLegacyNodes) return;

            if (_currentMapData.Nodes == null || _currentMapData.Nodes.Count == 0) return;

            // 建立 WaypointPaths：每個節點對應一個舊 waypoint 條目 [x, y, actionCode]
            _currentMapData.WaypointPaths = new List<float[]>(_currentMapData.Nodes.Count);
            foreach (var node in _currentMapData.Nodes)
            {
                // 編輯器目前用 (actionCode==11||12) 判斷 Rope/Platform
                float nodeActionCode = node.Type == "Rope" ? 11f : 0f;
                _currentMapData.WaypointPaths.Add(new[] { node.X, node.Y, nodeActionCode });
            }

            // 建立 Connections：每個 edge 對應一個舊連線 [fromIndex, toIndex, type, actionCode]
            var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var node = _currentMapData.Nodes[i];
                idToIndex[node.Id] = i;
            }

            _currentMapData.Connections = new List<int[]>();
            if (_currentMapData.Edges != null)
            {
                foreach (var edge in _currentMapData.Edges)
                {
                    if (!idToIndex.TryGetValue(edge.FromNodeId, out int fromIdx)) continue;
                    if (!idToIndex.TryGetValue(edge.ToNodeId, out int toIdx)) continue;

                    _currentMapData.Connections.Add(new[]
                    {
                        fromIdx,
                        toIdx,
                        1, // type 目前 GetCurrentMapData 不實際使用，保留為 1
                        (int)NavigationActionTypeToLegacyActionCode(edge.ActionType)
                    });
                }
            }
        }

        private static int NavigationActionTypeToLegacyActionCode(ArtaleAI.Core.Domain.Navigation.NavigationActionType actionType)
        {
            // 反推給 MapEditor 的 actionCode 解析/成本計算用（GetCurrentMapData）
            return actionType switch
            {
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.Walk => 0,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbUp => 3,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.ClimbDown => 4,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpLeft => 9,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpRight => 10,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.JumpDown => 7,
                ArtaleAI.Core.Domain.Navigation.NavigationActionType.Jump => 8,
                _ => 0
            };
        }

        /// <summary>
        /// 取得當前正在編輯的地圖資料
        /// </summary>
        /// <returns>目前的地圖資料物件</returns>
        public MapData GetCurrentMapData()
        {
            // 動態產生 Nodes
            if (_currentMapData.WaypointPaths != null)
            {
                _currentMapData.Nodes = new List<NavNodeData>();
                for (int i = 0; i < _currentMapData.WaypointPaths.Count; i++)
                {
                    var wp = _currentMapData.WaypointPaths[i];
                    if (wp.Length >= 2 && wp[0] >= 0)
                    {
                        var wpActionCode = wp.Length >= 3 ? (int)wp[2] : 0;
                        _currentMapData.Nodes.Add(new NavNodeData
                        {
                            Id = $"n{i}",
                            X = wp[0],
                            Y = wp[1],
                            Type = (wpActionCode == 11 || wpActionCode == 12) ? "Rope" : "Platform"
                        });
                    }
                }
            }

            // 動態產生 Edges
            if (_currentMapData.Connections != null)
            {
                _currentMapData.Edges = new List<NavEdgeData>();
                foreach (var conn in _currentMapData.Connections)
                {
                    if (conn.Length >= 4)
                    {
                        int fromIdx = conn[0];
                        int toIdx = conn[1];
                        int actionCode = conn[3];

                        if (fromIdx >= 0 && toIdx >= 0 &&
                            _currentMapData.WaypointPaths != null &&
                            fromIdx < _currentMapData.WaypointPaths.Count &&
                            toIdx < _currentMapData.WaypointPaths.Count)
                        {
                            var fromPt = _currentMapData.WaypointPaths[fromIdx];
                            var toPt = _currentMapData.WaypointPaths[toIdx];

                            int resolvedActionCode = ResolveActionCodeByGeometry(
                                new PointF(fromPt[0], fromPt[1]),
                                new PointF(toPt[0], toPt[1]),
                                actionCode);
                            var baseActionType = ConvertActionCode(resolvedActionCode);

                            float dist = (float)Math.Sqrt(Math.Pow(toPt[0] - fromPt[0], 2) + Math.Pow(toPt[1] - fromPt[1], 2));
                            float cost = resolvedActionCode switch
                            {
                                0 => dist,
                                11 => 5.0f,
                                12 => 3.0f,
                                9 or 10 => 8.0f,
                                4 => 2.0f,
                                _ => 6.0f
                            };

                            var edge = new NavEdgeData
                            {
                                FromNodeId = $"n{fromIdx}",
                                ToNodeId = $"n{toIdx}",
                                ActionType = baseActionType,
                                Cost = cost
                            };
                            _currentMapData.Edges.Add(edge);

                            // 方案 B（資料驅動）：不自動補反向邊，由地圖編輯者明確建立雙向關係。
                        }
                    }
                }
            }

            MapGenerationService.BuildHTopology(_currentMapData);

            return _currentMapData;
        }

        /// <summary>
        /// 設定當前的編輯模式（路徑點、安全區、限制區、繩索、刪除）
        /// </summary>
        /// <param name="mode">要切換的編輯模式</param>
        public void SetEditMode(EditMode mode)
        {
            if ((_currentEditMode == EditMode.Waypoint ||
                 _currentEditMode == EditMode.Rope) &&
                _startPoint.HasValue)
            {
                Console.WriteLine($"放棄未完成的繪製: {_currentEditMode}");
                _startPoint = null;
                _previewPoint = null;
            }

            // 離開兩點連線模式時清錨點，避免帶入下一模式誤觸發預覽線。
            if (_currentEditMode == EditMode.Link && mode != EditMode.Link)
            {
                _linkStartIndex = -1;
                _startPoint = null;
                _previewPoint = null;
            }

            _currentEditMode = mode;

            if (mode == EditMode.Link)
            {
                _linkStartIndex = -1;
                _startPoint = null;
                _previewPoint = null;
            }
        }

        /// <summary>
        /// 更新滑鼠懸停位置（用於預覽線條）
        /// </summary>
        /// <param name="screenPoint">滑鼠位置的螢幕座標</param>
        public void UpdateMousePosition(PointF screenPoint)
        {
            if (!_startPoint.HasValue || minimapBounds.IsEmpty)
            {
                _previewPoint = null;
                return;
            }

            // ✅ 修正：轉換為相對座標（與 _startPoint 一致）
            _previewPoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);
        }

        // 連結模式狀態
        private int _linkStartIndex = -1;                 // 連結線的起點索引

        /// <summary>
        /// 處理使用者在小地圖上的點擊事件
        /// 根據當前編輯模式執行對應操作：設定路徑點、安全區、限制區、繩索或刪除標記
        /// </summary>
        /// <param name="screenPoint">點擊位置的螢幕座標</param>
        /// <param name="button">滑鼠按鍵 (左鍵=路徑/標記, 右鍵=分段)</param>
        public void HandleClick(PointF screenPoint, MouseButtons button = MouseButtons.Left)
        {
            if (minimapBounds.IsEmpty) return;

            // ✅ 修正：將螢幕座標轉換為小地圖相對座標（與錄製路徑格式統一）
            var relativePoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);

            if (_currentEditMode == EditMode.Select)
            {
                // 選取模式：查找最近的節點
                int nearestIndex = FindNearestNodeIndex(relativePoint);

                _selectedNodeIndex = nearestIndex;
                if (_selectedNodeIndex != -1)
                {
                    var node = _currentMapData.WaypointPaths[_selectedNodeIndex];
                    int action = node.Length > 2 ? (int)node[2] : 0;
                    ApplyActionToSelectedNodeConnections();
                    int actionForUi = _currentActionType == SmartSideJumpActionCode ? SmartSideJumpActionCode : action;
                    Logger.Info($"[編輯器] 選取節點 {_selectedNodeIndex} (Action={action})");
                    OnNodeSelected?.Invoke(actionForUi);
                }
                else
                {
                    // 點擊空白處取消選取
                    OnNodeSelected?.Invoke(-1);
                }
            }
            else if (_currentEditMode == EditMode.Link)
            {
                _currentMapData.WaypointPaths ??= new List<float[]>();
                _currentMapData.Connections ??= new List<int[]>();

                int clickedIndex = FindNearestNodeIndex(relativePoint);

                if (clickedIndex != -1)
                {
                    if (_linkStartIndex == -1)
                    {
                        _linkStartIndex = clickedIndex;
                        var node = _currentMapData.WaypointPaths[clickedIndex];
                        _startPoint = new PointF(node[0], node[1]);
                        Logger.Info($"[編輯器] 連結起點: {clickedIndex}");
                    }
                    else
                    {
                        if (clickedIndex != _linkStartIndex)
                        {
                            ApplyDirectedConnection(
                                _linkStartIndex,
                                clickedIndex,
                                _currentActionType,
                                ensureReverseJumpIfSideJump: true,
                                out int resolved);
                            Logger.Info(
                                $"[編輯器] 連結完成: {_linkStartIndex} -> {clickedIndex} (Action={GetActionName(resolved)})");
                        }
                        else
                        {
                            Logger.Info("[編輯器] 取消連結起點（點擊同一起點）");
                        }

                        _linkStartIndex = -1;
                        _startPoint = null;
                        _previewPoint = null;
                    }
                }
                else
                {
                    _linkStartIndex = -1;
                    _startPoint = null;
                    _previewPoint = null;
                }
            }
            else if (_currentEditMode == EditMode.Waypoint)
            {
                _currentMapData.WaypointPaths ??= new List<float[]>();
                _currentMapData.Connections ??= new List<int[]>();

                // 右鍵點擊：加入分段標記
                if (button == MouseButtons.Right)
                {
                    // 只有在最後一個點不是分段標記時才加入
                    if (_currentMapData.WaypointPaths.Count > 0)
                    {
                        var last = _currentMapData.WaypointPaths[^1];
                        if (last.Length >= 2 && last[0] >= 0)
                        {
                            _currentMapData.WaypointPaths.Add(new[] { -1f, -1f, -1f });
                            Logger.Info("[編輯器] 插入路徑分段標記並重置錨點");
                        }
                    }
                    _waypointAnchorIndex = -1; // 🎯 關鍵：重置錨點以截斷連線
                    _startPoint = null;
                }
                else // 左鍵點擊
                {
                    // 🔧 檢查是否點到了已存在的節點
                    int clickedExisting = FindNearestNodeIndex(relativePoint);

                    if (clickedExisting != -1)
                    {
                        // ✅ 點到已存在的節點 → 選為錨點（下個新節點從此連出）
                        _waypointAnchorIndex = clickedExisting;
                        _selectedNodeIndex = clickedExisting;
                        var node = _currentMapData.WaypointPaths[clickedExisting];
                        _startPoint = new PointF(node[0], node[1]); // 🎯 同步預覽起點
                        int action = node.Length > 2 ? (int)node[2] : 0;
                        OnNodeSelected?.Invoke(action);
                        Logger.Info($"[編輯器] 選中節點 {clickedExisting} 為錨點 (從此連出新節點)");
                    }
                    else
                    {
                        // ✅ 點到空白處 → 建立新節點
                        _currentMapData.WaypointPaths.Add(new[] {
                            (float)Math.Round(relativePoint.X, 1),
                            (float)Math.Round(relativePoint.Y, 1),
                            0f
                        });

                        int newIndex = _currentMapData.WaypointPaths.Count - 1;

                        // 🧠 Auto-Guess：決定連接到哪個前一個節點
                        int prevIndex;
                        if (_waypointAnchorIndex != -1)
                        {
                            // 優先使用手動選擇的錨點
                            prevIndex = _waypointAnchorIndex;
                        }
                        else
                        {
                            // 否則自動連接到上一個有效節點
                            prevIndex = FindPreviousValidNodeIndex(newIndex);
                        }

                        if (prevIndex != -1)
                        {
                            ApplyDirectedConnection(
                                prevIndex,
                                newIndex,
                                _currentActionType,
                                ensureReverseJumpIfSideJump: true,
                                out int actionToUse);
                            string actionName = GetActionName(actionToUse);
                            Logger.Info($"[手動選取] 新增節點 {newIndex} ({relativePoint.X:F1}, {relativePoint.Y:F1}) ← {actionName} ← 節點 {prevIndex}");
                        }
                        else
                        {
                            Logger.Info($"[編輯器] 新增起始節點 {newIndex} ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                        }

                        // 新節點自動成為下一個錨點（連續畫線的直覺）
                        _waypointAnchorIndex = newIndex;
                        _startPoint = relativePoint; // 🎯 同步預覽起點
                    }
                }
            }
            else if (_currentEditMode == EditMode.Rope)
            {
                _currentMapData.Ropes ??= new List<float[]>();

                if (!_startPoint.HasValue)
                {
                    // 第一次點擊：記錄起點
                    _startPoint = relativePoint;
                    Logger.Info($"[編輯器] 繩索起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                }
                else
                {
                    // 第二次點擊：記錄終點並建立繩索
                    var start = _startPoint.Value;
                    var end = relativePoint;

                    // 自動判斷頂端和底端
                    float topY = Math.Min(start.Y, end.Y);
                    float bottomY = Math.Max(start.Y, end.Y);
                    float x = start.X; // 以第一次點擊的 X 為準

                    // 儲存格式: [x, topY, bottomY]
                    _currentMapData.Ropes.Add(new[] {
                        (float)Math.Round(x, 1),
                        (float)Math.Round(topY, 1),
                        (float)Math.Round(bottomY, 1)
                    });

                    Logger.Info($"[編輯器] 建立繩索: X={x:F1}, Y={topY:F1}~{bottomY:F1}");

                    _startPoint = null;
                    _previewPoint = null;
                }
            }
            else if (_currentEditMode == EditMode.Delete)
            {
                HandleDeleteAction(relativePoint);
            }
        }

        private int FindNearestNodeIndex(PointF relativePoint)
        {
            if (_currentMapData.WaypointPaths == null) return -1;

            int nearestIndex = -1;
            float minDistance = float.MaxValue;

            for (int i = 0; i < _currentMapData.WaypointPaths.Count; i++)
            {
                var node = _currentMapData.WaypointPaths[i];
                if (node.Length < 2 || node[0] < 0) continue; // 跳過無效點或分隔標記

                float dx = node[0] - relativePoint.X;
                float dy = node[1] - relativePoint.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < SelectionRadius && dist < minDistance)
                {
                    minDistance = dist;
                    nearestIndex = i;
                }
            }
            return nearestIndex;
        }


        /// <summary>
        /// 渲染地圖編輯器的所有視覺元素
        /// </summary>
        public void Render(Graphics g, Func<PointF, PointF> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        /// <summary>
        /// 繪製完成的路徑形狀
        /// </summary>
        private void DrawCompletedShapes(Graphics g, Func<PointF, PointF> convert)
        {
            if (minimapBounds.IsEmpty) return;

            // 1. 繪製路徑點 (Waypoints)
            if (_currentMapData.WaypointPaths?.Any() == true)
            {
                var paths = _currentMapData.WaypointPaths;

                // A. 繪製連線 (原有順序)
                using (var pen = new Pen(Color.FromArgb(180, Color.Cyan), 1.5f))
                {
                    for (int i = 0; i < paths.Count - 1; i++)
                    {
                        var p1Data = paths[i];
                        var p2Data = paths[i + 1];

                        // 跳過分段標記
                        if (p1Data[0] < 0 || p2Data[0] < 0) continue;

                        var p1 = convert(new PointF(minimapBounds.X + p1Data[0], minimapBounds.Y + p1Data[1]));
                        var p2 = convert(new PointF(minimapBounds.X + p2Data[0], minimapBounds.Y + p2Data[1]));

                        g.DrawLine(pen, p1, p2);

                        // 繪製箭頭 (每段線的中點)
                        if (i % 2 == 0) // 不要太密集
                        {
                            float midX = (p1.X + p2.X) / 2;
                            float midY = (p1.Y + p2.Y) / 2;
                            DrawArrow(g, p1, p2, new PointF(midX, midY));
                        }
                    }
                }

                // A-2. 繪製自定義連結 (Connections)
                if (_currentMapData.Connections != null)
                {
                    foreach (var conn in _currentMapData.Connections)
                    {
                        if (conn.Length < 2) continue;
                        int from = conn[0];
                        int to = conn[1];
                        // Type = conn[2], Action = conn[3]

                        int action = conn.Length > 3 ? conn[3] : 0;
                        Color lineColor = GetNodeColor(action); // 重用節點顏色邏輯

                        // 如果是普通走路(0)，用預設顏色區分
                        if (action == 0) lineColor = Color.Magenta;

                        if (from >= 0 && from < paths.Count && to >= 0 && to < paths.Count)
                        {
                            var p1Data = paths[from];
                            var p2Data = paths[to];
                            if (p1Data[0] < 0 || p2Data[0] < 0) continue;

                            var p1 = convert(new PointF(minimapBounds.X + p1Data[0], minimapBounds.Y + p1Data[1]));
                            var p2 = convert(new PointF(minimapBounds.X + p2Data[0], minimapBounds.Y + p2Data[1]));

                            using (var pen = new Pen(lineColor, 2.0f))
                            {
                                // 畫虛線表示特殊動作? 不，實線顏色區分即可
                                g.DrawLine(pen, p1, p2);

                                // 箭頭
                                float midX = (p1.X + p2.X) / 2;
                                float midY = (p1.Y + p2.Y) / 2;
                                DrawArrow(g, p1, p2, new PointF(midX, midY));
                            }
                        }
                    }
                }

                // B. 繪製節點圓點
                for (int i = 0; i < paths.Count; i++)
                {
                    var pData = paths[i];
                    if (pData[0] < 0) continue; // 跳過分段標記

                    var pos = convert(new PointF(minimapBounds.X + pData[0], minimapBounds.Y + pData[1]));
                    int action = pData.Length > 2 ? (int)pData[2] : 0;

                    // 根據狀態決定顏色
                    Color nodeColor = GetNodeColor(action);
                    float radius = PointRadius;

                    // 高亮選取和懸停狀態
                    if (i == _selectedNodeIndex)
                    {
                        g.FillEllipse(Brushes.Yellow, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                        g.DrawEllipse(Pens.Black, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                    }
                    else if (i == _hoveredNodeIndex)
                    {
                        g.DrawEllipse(Pens.White, pos.X - radius - 1, pos.Y - radius - 1, (radius + 1) * 2, (radius + 1) * 2);
                    }

                    // 繪製節點本體
                    using (var brush = new SolidBrush(nodeColor))
                    {
                        g.FillEllipse(brush, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
                    }

                    // 起點和終點若有特殊標記可在此繪製
                    if (i == 0) DrawLabel(g, pos, "Start", Color.Lime);
                    else if (i == paths.Count - 1) DrawLabel(g, pos, "End", Color.Red);

                    // 選取時顯示詳細資訊
                    if (i == _selectedNodeIndex)
                    {
                        // SmartSideJump 是操作語意（UI），實際資料仍落地為左右跳。
                        // 這裡優先顯示目前選單語意，避免使用者誤以為未更新。
                        int displayAction = (_currentEditMode == EditMode.Select && _currentActionType == SmartSideJumpActionCode)
                            ? SmartSideJumpActionCode
                            : action;
                        string actionName = GetActionName(displayAction);
                        DrawLabel(g, new PointF(pos.X, pos.Y - 15), $"{i}: {actionName}", Color.Yellow);
                    }
                }
            }
            // 2. 繪製繩索 (Ropes) - 新格式 [x, topY, bottomY]
            if (_currentMapData.Ropes?.Any() == true)
            {
                foreach (var rope in _currentMapData.Ropes)
                {
                    if (rope.Length < 3) continue; // 舊格式或無效資料

                    float x = rope[0];
                    float topY = rope[1];
                    float bottomY = rope[2];

                    var pTop = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + topY));
                    var pBottom = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + bottomY));

                    using var pen = new Pen(Color.Yellow, 3f);
                    g.DrawLine(pen, pTop, pBottom);

                    // 繪製端點
                    g.FillRectangle(Brushes.Red, pTop.X - 3, pTop.Y - 3, 6, 6);
                    g.FillRectangle(Brushes.Green, pBottom.X - 3, pBottom.Y - 3, 6, 6);
                }
            }

        }

        /// <summary>
        /// 繪製預覽形狀（編輯過程中的即時視覺回饋）
        /// 包括預覽線條（虛線）和起點標記（紅色圓點）
        /// </summary>
        /// <param name="g">GDI+ 繪圖物件</param>
        /// <param name="convert">座標轉換函式</param>
        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            bool isLineMode = _currentEditMode == EditMode.Waypoint ||
                              _currentEditMode == EditMode.Rope ||
                              _currentEditMode == EditMode.Link;

            // 顯示預覽線
            if (_startPoint.HasValue && _previewPoint.HasValue && isLineMode)
            {
                // ✅ 修正：_startPoint 和 _previewPoint 現在都是相對座標，需要加回小地圖偏移
                var startScreen = new PointF(
                    minimapBounds.X + _startPoint.Value.X,
                    minimapBounds.Y + _startPoint.Value.Y);
                var previewScreen = new PointF(
                    minimapBounds.X + _previewPoint.Value.X,
                    minimapBounds.Y + _previewPoint.Value.Y);

                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(startScreen), convert(previewScreen));
                }
            }

            // 顯示起點
            if (_startPoint.HasValue && isLineMode)
            {
                var startScreen = new PointF(
                    minimapBounds.X + _startPoint.Value.X,
                    minimapBounds.Y + _startPoint.Value.Y);
                var converted = convert(startScreen);
                using (var brush = new SolidBrush(Color.Red))
                {
                    // 🔧 還原：改回 4x4
                    g.FillEllipse(brush, converted.X - 2, converted.Y - 2, 4, 4);
                }
            }
        }

        /// <summary>
        /// 處理刪除操作
        /// 在點擊位置的指定半徑範圍內搜尋並刪除最近的路徑點
        /// </summary>
        /// <param name="clickPosition">點擊位置的座標</param>
        private void HandleDeleteAction(PointF clickPosition)
        {
            // 🔧 使用與高亮相同的判定半徑，確保「看到高亮的就是刪除的」
            float deletionRadius = SelectionRadius;

            var pathGroups = new[]
            {
                ("Waypoint", _currentMapData.WaypointPaths),
                ("Rope", _currentMapData.Ropes)
            };

            // 先找出所有類型中「距離最近」的那個節點
            string? bestGroupName = null;
            IList<float[]>? bestGroupList = null;
            int bestIndex = -1;
            float bestDistance = float.MaxValue;

            foreach (var (name, pathList) in pathGroups)
            {
                if (pathList?.Any() != true) continue;

                for (int i = 0; i < pathList.Count; i++)
                {
                    var coord = pathList[i];
                    if (coord.Length < 2 || coord[0] < 0) continue;

                    float dx = coord[0] - clickPosition.X;
                    float dy = coord[1] - clickPosition.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist <= deletionRadius && dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestGroupName = name;
                        bestGroupList = pathList;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex == -1 || bestGroupList == null || bestGroupName == null) return;

            // 刪除找到的最近節點
            if (bestGroupName == "Waypoint" && _currentMapData.Connections != null)
            {
                // 1. 同步刪除關聯連結與處理索引偏移 (Shift Indices)
                for (int k = _currentMapData.Connections.Count - 1; k >= 0; k--)
                {
                    int[] conn = _currentMapData.Connections[k];
                    bool remove = false;

                    if (conn[0] == bestIndex || conn[1] == bestIndex)
                    {
                        remove = true;
                    }
                    else
                    {
                        // 🧠 位移邏輯：被刪除點之後的所有點，索引都要減 1
                        if (conn[0] > bestIndex) conn[0]--;
                        if (conn[1] > bestIndex) conn[1]--;
                    }

                    if (remove)
                    {
                        _currentMapData.Connections.RemoveAt(k);
                        Logger.Info($"同步刪除關聯連結: Index {k}");
                    }
                }

                // 2. 🛠️ 方案 C: 同步更新編輯器內部狀態 (避免雜訊產生的元兇)
                if (_selectedNodeIndex == bestIndex) _selectedNodeIndex = -1;
                else if (_selectedNodeIndex > bestIndex) _selectedNodeIndex--;

                if (_waypointAnchorIndex == bestIndex) _waypointAnchorIndex = -1;
                else if (_waypointAnchorIndex > bestIndex) _waypointAnchorIndex--;

                if (_linkStartIndex == bestIndex) _linkStartIndex = -1;
                else if (_linkStartIndex > bestIndex) _linkStartIndex--;

                if (_hoveredNodeIndex == bestIndex) _hoveredNodeIndex = -1;
                else if (_hoveredNodeIndex > bestIndex) _hoveredNodeIndex--;
            }

            var deletedCoord = bestGroupList[bestIndex];
            bestGroupList.RemoveAt(bestIndex);
            Logger.Info($"刪除 {bestGroupName} (Index {bestIndex}): ({deletedCoord[0]:F1}, {deletedCoord[1]:F1})");
        }

        /// <summary>
        /// 更新滑鼠懸停節點
        /// </summary>
        public void UpdateHoveredNode(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty || _currentMapData.WaypointPaths == null) return;

            var relativePoint = new PointF(
               screenPoint.X - minimapBounds.X,
               screenPoint.Y - minimapBounds.Y);

            int nearestIndex = -1;
            float minDistance = float.MaxValue;

            for (int i = 0; i < _currentMapData.WaypointPaths.Count; i++)
            {
                var node = _currentMapData.WaypointPaths[i];
                if (node.Length < 2 || node[0] < 0) continue;

                float dx = node[0] - relativePoint.X;
                float dy = node[1] - relativePoint.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < SelectionRadius && dist < minDistance)
                {
                    minDistance = dist;
                    nearestIndex = i;
                }
            }

            _hoveredNodeIndex = nearestIndex;
        }

        /// <summary>
        /// 從指定索引往前找第一個有效節點（非分段標記 [-1,-1,-1]）
        /// </summary>
        private int FindPreviousValidNodeIndex(int currentIndex)
        {
            if (_currentMapData.WaypointPaths == null) return -1;

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                var node = _currentMapData.WaypointPaths[i];
                // 跳過分段標記
                if (node.Length >= 2 && node[0] >= 0 && node[1] >= 0)
                {
                    // 如果遇到分段標記（距離 currentIndex 中間有 [-1,-1,-1]），
                    // 代表使用者刻意分段，不自動連接
                    // 檢查 i+1 到 currentIndex-1 之間是否有分段標記
                    bool hasSeparator = false;
                    for (int j = i + 1; j < currentIndex; j++)
                    {
                        var mid = _currentMapData.WaypointPaths[j];
                        if (mid.Length >= 2 && mid[0] < 0)
                        {
                            hasSeparator = true;
                            break;
                        }
                    }

                    if (!hasSeparator)
                        return i;
                    else
                        return -1; // 有分段標記，不自動連接
                }
            }
            return -1;
        }

        private Color GetNodeColor(int action)
        {
            return action switch
            {
                9 => Color.MediumPurple,   // JumpLeft
                10 => Color.Purple,        // JumpRight
                13 => Color.MediumPurple,  // SmartSideJump（僅 UI 選單，不應落地）
                11 => Color.Cyan,          // ClimbUp
                12 => Color.Cyan,          // ClimbDown
                4 => Color.Yellow,         // JumpDown
                8 => Color.DeepSkyBlue,    // 舊版 Jump 保留
                _ => Color.White           // Walk/Idle
            };
        }

        private string GetActionName(int action)
        {
            return action switch
            {
                9 => "JumpLeft",
                10 => "JumpRight",
                13 => "SmartSideJump",
                11 => "ClimbUp",
                12 => "ClimbDn",
                4 => "DownJump",
                8 => "Jump",
                0 => "Walk",
                _ => $"Act:{action}"
            };
        }

        private void DrawArrow(Graphics g, PointF p1, PointF p2, PointF mid)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1) return;

            dx /= length;
            dy /= length;

            float arrowSize = 3;
            PointF[] arrow = new PointF[]
            {
                new PointF(mid.X + dx * arrowSize, mid.Y + dy * arrowSize),
                new PointF(mid.X - dx * arrowSize - dy * arrowSize, mid.Y - dy * arrowSize + dx * arrowSize),
                new PointF(mid.X - dx * arrowSize + dy * arrowSize, mid.Y - dy * arrowSize - dx * arrowSize)
            };

            g.FillPolygon(Brushes.Cyan, arrow);
        }

        private void DrawLabel(Graphics g, PointF pos, string text, Color color)
        {
            using (var font = new Font("Arial", 8))
            using (var brush = new SolidBrush(color))
            {
                var size = g.MeasureString(text, font);
                g.FillRectangle(Brushes.Black, pos.X, pos.Y - size.Height, size.Width, size.Height);
                g.DrawString(text, font, brush, pos.X, pos.Y - size.Height);
            }
        }
    }
}
