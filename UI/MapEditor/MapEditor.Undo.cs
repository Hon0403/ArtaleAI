namespace ArtaleAI.UI.MapEditor
{
    public partial class MapEditor
    {
        private readonly MapEditorUndoStack _undoStack = new();
        private bool _isApplyingHistory;

        public event Action? HistoryChanged;

        public bool CanUndo => _undoStack.CanUndo;
        public bool CanRedo => _undoStack.CanRedo;

        public void ClearHistory()
        {
            _undoStack.Clear();
            HistoryChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo)
                return;

            string current = MapEditorUndoStack.Serialize(_currentMapData);
            string? previous = _undoStack.Undo(current);
            if (previous == null)
                return;

            ApplyHistorySnapshot(previous);
        }

        public void Redo()
        {
            if (!CanRedo)
                return;

            string current = MapEditorUndoStack.Serialize(_currentMapData);
            string? next = _undoStack.Redo(current);
            if (next == null)
                return;

            ApplyHistorySnapshot(next);
        }

        private void RecordUndoSnapshot()
        {
            if (_isApplyingHistory)
                return;

            _undoStack.Push(_currentMapData);
            HistoryChanged?.Invoke();
        }

        private void ApplyHistorySnapshot(string json)
        {
            _isApplyingHistory = true;
            try
            {
                MapEditorUndoStack.Apply(_currentMapData, json);
                _manualEdgeStartAnchor = null;
                _startPoint = null;
                _previewPoint = null;
                _vertexDrag = null;
                RebuildTopology();
                MarkDirty();
                ClearSelection();
                MapMutated?.Invoke();
                HistoryChanged?.Invoke();
            }
            finally
            {
                _isApplyingHistory = false;
            }
        }
    }
}
