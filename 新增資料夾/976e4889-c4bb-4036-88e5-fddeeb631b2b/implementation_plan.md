# Fix Independent Window Visibility Logic

## Goal
Ensure the independent minimap window (`_minimapViewer`) remains visible when "Auto Attack" (`ckB_Start`) is checked, regardless of which tab is active (especially the Main Console tab).

## Current Issue
`TabControl1_SelectedIndexChanged` unconditionally calls `_minimapViewer?.Hide()` when switching to `Tab 0` (Main Console). This hides the minimap window even if the user has started the bot.

## Proposed Changes

### `UI/MainForm.cs`

#### [MODIFY] `TabControl1_SelectedIndexChanged`
- In `case 0` (Main Console):
    - Check if `ckB_Start.Checked` is true.
    - If true, call `_minimapViewer?.Show()`.
    - If false, call `_minimapViewer?.Hide()`.

#### [MODIFY] `ckB_Start_CheckedChanged`
- When `Checked` is true:
    - Explicitly call `_minimapViewer?.Show()` after minimap is loaded.
- When `Checked` is false:
    - If current tab is Tab 0 (Main Console), call `_minimapViewer?.Hide()`.

## Verification Plan
1. Start application.
2. Go to Main Console (Tab 0).
3. Check "Auto Attack".
4. Verify independent minimap window appears.
5. Switch between tabs. Verify window remains visible.
6. Uncheck "Auto Attack" while in Tab 0. Verify window disappears.
