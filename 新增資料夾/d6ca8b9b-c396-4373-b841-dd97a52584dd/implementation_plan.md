# Fix Rope Target Generation Logic

## Goal Description
Ensure the "Rope Lookahead" logic in `PathPlanningTracker.cs` is correctly applied when selecting the next target. Currently, the logic correctly identifies and merges consecutive rope nodes, but the final assignment of `_currentPathIndex` ignores this calculation and simply increments the index by 1, defeating the optimization.

## User Review Required
No critical user review required, this is a logic fix.

## Proposed Changes

### Core
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- In `SelectNextTarget` (or the logic block handling reached waypoints), inside the `if (originalRequiresPrecision)` block:
    - Change `_currentPathIndex = _currentPathIndex + 1;` to `_currentPathIndex = nextIndex;`.
    - This ensures that if `nextIndex` was advanced by the Rope Lookahead logic (e.g., from 26 to 31), the system respects it.

## Verification Plan
### Automated Tests
- None.

### Manual Verification
1.  **Re-run Path Execution**: Observe the character climbing ropes.
2.  **Check Logs**: Verify that when `[路徑優化] 合併繩索節點...` appears, the subsequent `[路徑規劃] 目標: Index...` matches the merged index (e.g., 31) and not the immediate next index (e.g., 27).
3.  **Visual Confirmation**: The character should climb continuously to the top of the rope (or the merged target) without stopping or stuttering at intermediate nodes.
