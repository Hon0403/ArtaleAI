---
name: version-control-visionstream
description: "VisionStream 專案 Git 分支別名覆寫。執行 git 操作時，搭配全域 version-control skill 使用；本專案正式分支為 Pro、開發分支為 Dev。"
---

# VisionStream 分支別名

全域 `version-control` skill 的流程適用本專案，但分支名稱使用以下別名：

| 角色 | 通用名稱 | VisionStream 別名 |
|------|----------|-------------------|
| 正式環境 | `main` | **`Pro`** |
| 開發整合 | `develop` | **`Dev`** |

## 操作時替換規則

- `git checkout Dev` 取代 `git checkout develop`
- `git checkout Pro` 取代 `git checkout main`
- Feature 從 `Dev` 開出，合併回 `Dev`
- Hotfix 從 `Pro` 開出，合併回 `Pro` 與 `Dev`

完整流程與 Conventional Commits 規範見全域 `~/.cursor/skills/version-control/SKILL.md`。
