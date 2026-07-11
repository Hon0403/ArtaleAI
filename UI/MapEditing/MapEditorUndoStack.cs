using ArtaleAI.Models.Map;
using Newtonsoft.Json;

namespace ArtaleAI.UI.MapEditing
{
    internal sealed class MapSsotSnapshot
    {
        public List<PolylinePlatformData> PolylinePlatforms { get; set; } = new();
        public List<float[]> Ropes { get; set; } = new();
        public List<ManualEdgeAnchor> ManualEdgeAnchors { get; set; } = new();
    }

    /// <summary>僅快照持久化 SSOT 欄位，不含 runtime Nodes/Edges。</summary>
    public sealed class MapEditorUndoStack
    {
        private const int MaxSteps = 20;
        private readonly List<string> _undo = new();
        private readonly List<string> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        public static string Serialize(MapData data)
        {
            var snapshot = new MapSsotSnapshot
            {
                PolylinePlatforms = data.PolylinePlatforms ?? new List<PolylinePlatformData>(),
                Ropes = data.Ropes ?? new List<float[]>(),
                ManualEdgeAnchors = data.ManualEdgeAnchors ?? new List<ManualEdgeAnchor>()
            };
            return JsonConvert.SerializeObject(snapshot);
        }

        public static void Apply(MapData target, string json)
        {
            var snapshot = JsonConvert.DeserializeObject<MapSsotSnapshot>(json) ?? new MapSsotSnapshot();
            target.PolylinePlatforms = snapshot.PolylinePlatforms ?? new List<PolylinePlatformData>();
            target.Ropes = snapshot.Ropes ?? new List<float[]>();
            target.ManualEdgeAnchors = snapshot.ManualEdgeAnchors ?? new List<ManualEdgeAnchor>();
        }

        public void Push(MapData data)
        {
            string json = Serialize(data);
            if (_undo.Count > 0 && _undo[^1] == json)
                return;

            _redo.Clear();
            _undo.Add(json);
            if (_undo.Count > MaxSteps)
                _undo.RemoveAt(0);
        }

        public string? Undo(string currentSnapshot)
        {
            if (_undo.Count == 0)
                return null;

            _redo.Add(currentSnapshot);
            string previous = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            return previous;
        }

        public string? Redo(string currentSnapshot)
        {
            if (_redo.Count == 0)
                return null;

            _undo.Add(currentSnapshot);
            string next = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            return next;
        }
    }
}
