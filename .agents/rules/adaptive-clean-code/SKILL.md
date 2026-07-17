---
name: adaptive-clean-code
description: Use when writing, fixing, editing, reviewing, or refactoring code in any project language. Routes Clean Code rules by file extension—Python (.py), C# (.cs), JavaScript (.js/.html). General G-rules apply to all languages.
---

# Adaptive Clean Code

## Skill 分層（精簡後）

| 層級 | 位置 | 職責 |
|------|------|------|
| 全域 | `~/.cursor/skills/boy-scout`、`clean-*` | Boy Scout Rule 與通用 Clean Code 細則 |
| 專案 | 本檔案 | 依副檔名路由 + **ArtaleAI** C# 架構約束 |

專案內已移除與全域重複的 `boy-scout`、`clean-*` rules；深度審查時搭配全域同名 skill。

---

## Language Routing（必讀）

編輯或審查程式時，**只套用與目前檔案副檔名對應的章節**：

| 副檔名 | 套用章節 | 本專案路徑 |
|--------|----------|------------|
| `.cs` | C# | `Core/`、`Services/`、`UI/`、`Models/`（排除 `obj/`） |
| `.py` | Python | 本專案無；若新增則見下方 Python 章節 |
| `.js` / `.html` | JavaScript | 本專案無；若新增則見下方 JavaScript 章節 |

**禁止**將 Python 規則（PEP 8、`from x import *`）套用到 C# 或 JavaScript。

下方 **General (G*)** 規則適用所有語言。

---

## General（所有語言）

- G5: DRY — 不重複邏輯
- G9: 刪除死碼
- G16: 意圖清晰，不耍聰明
- G25: 具名常數，禁止魔術數字
- G30: 函式 / 方法只做一件事
- G36: Law of Demeter（避免 `a.b.c.d` 鏈式存取）

## AI Behavior

- 標註違規規則編號（例：`G5 violation`）
- 修正時說明改了什麼（例：`extracted SECONDS_PER_DAY (G25)`）
- 先確認副檔名，再選語言章節

---

## Python（僅 `.py`）

Robert C. Martin Clean Code 第 17 章，Python 適配版。

### Comments (C1-C5)
- C1: 註解不放 metadata（用 Git）
- C2: 刪除過時註解
- C3: 無冗餘註解
- C4: 值得寫的註解要寫好
- C5: 禁止提交註解掉的程式碼

### Functions (F1-F4)
- F1: 最多 3 個參數（更多用 dataclass）
- F2: 無輸出參數（用回傳值）
- F3: 無旗標參數（拆函式）
- F4: 刪除死函式

### Python-Specific (P1-P3)
- P1: 禁止 `from x import *`
- P2: 用 Enum，不用魔術常數
- P3: 公開介面加 type hints

### Names (N1-N7)
- N1: 描述性命名
- N5: 名稱長度匹配作用域
- N6: 無匈牙利命名

### Tests (T1-T9)
- T5: 測試邊界條件
- T9: 單元測試 < 100ms

### Environment
- E1: `pip install -e ".[dev]"`
- E2: `pytest`

---

## C#（僅 `.cs`）

本專案為 **WinForms 遊戲輔助 + 導航領域模型**，分層如下：

### 架構約束
- `Core/Domain/`：純領域邏輯（Navigation、ArrivalValidator 等），不依賴 UI 或 Win32
- `Services/`：編排與 I/O（GamePipeline、ScreenCapture、鍵盤輸入）
- `UI/`：WinForms 與視覺化，不內嵌導航演算法
- `Models/`：設定與 DTO，保持 POCO
- 不為未來需求預先抽象（YAGNI）；新抽象需有現有呼叫點

### 慣例
- 遵循 .NET 命名慣例（PascalCase 公開成員、camelCase 區域變數）
- `Nullable` 已啟用，避免不必要的 `!` 抑制
- 常數用 `const` 或 `static readonly`，不用魔術字串 / 數字
- 一個檔案一個主要職責；超過 3 參數的方法考慮參數物件（搭配全域 `clean-functions`）

### 禁止
- 在 `Core/Domain` 引用 `System.Windows.Forms` 或螢幕擷取 API
- 在 UI 事件處理器直接寫複雜路徑規劃邏輯（應委派 Services）

---

## JavaScript（僅 `.js` / `.html`）

本專案目前無前端程式碼。若未來新增，套用 `const`/`let`、`async/await` 與全域 `clean-*` skills。

---

## Quick Reference

| 語言 | 關鍵規則 |
|------|----------|
| 全部 | G5 DRY、G25 常數、G30 單一職責 |
| C# | Domain/UI 分離、YAGNI、Services 編排 |
| Python | F1 參數 ≤3、P1 禁 wildcard import（僅當專案含 `.py`） |
| JavaScript | const/let、async/await（僅當專案含 `.js`） |
