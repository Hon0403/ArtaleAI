# MapleStoryAutoLevelUp Player Localization Search

## Checklist
- [x] Navigate to KenYu910645/MapleStoryAutoLevelUp repo
- [x] Identify player localization function: `get_player_location_on_minimap`
- [x] Locate file: `src/utils/common.py`
- [x] Extract logic of `get_player_location_on_minimap`
- [x] Check for `matchTemplate` or `InRange` usage

## Findings
- **Implementation in `src/utils/common.py`**:
  ```python
  def get_player_location_on_minimap(img_minimap, minimap_player_color=(136, 255, 255)):
      mask = cv2.inRange(img_minimap, minimap_player_color, minimap_player_color)
      coords = cv2.findNonZero(mask)
      if coords is None or len(coords) < 4:
          return None
      avg = coords.mean(axis=0)[0]
      loc_player_minimap = (int(round(avg[0])), int(round(avg[1])))
      return loc_player_minimap
  ```
- **Key Logic**:
  - Uses `cv2.inRange` for exact color filtering (Moments-like/Centroid calculation).
  - Calculates the **mean** (average) of matching pixels to get the center.
  - Does **not** use `matchTemplate` for minimap player localization.
  - `matchTemplate` (`TM_SQDIFF_NORMED`) is used for finding the minimap within the global map and for nametag detection.
- **Nametag Detection (`MapleStoryAutoLevelUp.py`)**:
  - Uses `cv2.matchTemplate` with split nametag (left/right halves) for robustness.
- **Comparison with User's Problem**:
  - The repo's method returns the **geometric center** of the player marker.
  - If the user's "Moments" method has a Y-offset (like `avgY + 3.5f`), it might be trying to find the "feet" of the marker.
  - 3/21 worked because it precisely matched this InRange + Centroid logic without deviation, or the Y-offset was correctly calibrated then.
