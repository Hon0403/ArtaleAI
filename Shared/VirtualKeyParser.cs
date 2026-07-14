using System.Globalization;

namespace ArtaleAI.Shared
{
    /// <summary>
    /// 快捷鍵名稱 ↔ Virtual-Key；不依賴 WinForms。
    /// 設計目標：楓谷／Artale 熱鍵列幾乎任一鍵都可設定，未知鍵以 VK_XX 維持 round-trip。
    /// </summary>
    public static class VirtualKeyParser
    {
        private static readonly Dictionary<string, ushort> NamedKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // 導覽／編輯
                ["Backspace"] = 0x08,
                ["BS"] = 0x08,
                ["Tab"] = 0x09,
                ["Enter"] = 0x0D,
                ["Return"] = 0x0D,
                ["Pause"] = 0x13,
                ["CapsLock"] = 0x14,
                ["Escape"] = 0x1B,
                ["Esc"] = 0x1B,
                ["Space"] = 0x20,
                ["PageUp"] = 0x21,
                ["PgUp"] = 0x21,
                ["PageDown"] = 0x22,
                ["PgDn"] = 0x22,
                ["End"] = 0x23,
                ["Home"] = 0x24,
                ["Left"] = 0x25,
                ["Up"] = 0x26,
                ["Right"] = 0x27,
                ["Down"] = 0x28,
                ["PrintScreen"] = 0x2C,
                ["PrtSc"] = 0x2C,
                ["Insert"] = 0x2D,
                ["Ins"] = 0x2D,
                ["Delete"] = 0x2E,
                ["Del"] = 0x2E,

                // 修飾鍵（遊戲可把單一修飾鍵當熱鍵）
                ["Shift"] = 0x10,
                ["Ctrl"] = 0x11,
                ["Control"] = 0x11,
                ["Alt"] = 0x12,
                ["Win"] = 0x5B,
                ["LWin"] = 0x5B,
                ["RWin"] = 0x5C,
                ["Apps"] = 0x5D,
                ["Menu"] = 0x5D,

                // 小鍵盤運算子（數字見 Prefer／Format）
                ["NumMultiply"] = 0x6A,
                ["NumAdd"] = 0x6B,
                ["NumSubtract"] = 0x6D,
                ["NumDecimal"] = 0x6E,
                ["NumDivide"] = 0x6F,
                ["NumLock"] = 0x90,
                ["ScrollLock"] = 0x91,

                // US ANSI OEM（其他佈局 VK 相同、刻字可能不同）
                [";"] = 0xBA,
                [":"] = 0xBA,
                ["Oem1"] = 0xBA,
                ["="] = 0xBB,
                ["Plus"] = 0xBB,
                ["OemPlus"] = 0xBB,
                [","] = 0xBC,
                ["OemComma"] = 0xBC,
                ["-"] = 0xBD,
                ["Minus"] = 0xBD,
                ["OemMinus"] = 0xBD,
                ["."] = 0xBE,
                ["OemPeriod"] = 0xBE,
                ["/"] = 0xBF,
                ["Oem2"] = 0xBF,
                ["OemQuestion"] = 0xBF,
                ["`"] = 0xC0,
                ["~"] = 0xC0,
                ["Oem3"] = 0xC0,
                ["Oemtilde"] = 0xC0,
                ["["] = 0xDB,
                ["Oem4"] = 0xDB,
                ["OemOpenBrackets"] = 0xDB,
                ["\\"] = 0xDC,
                ["Oem5"] = 0xDC,
                ["OemPipe"] = 0xDC,
                ["]"] = 0xDD,
                ["Oem6"] = 0xDD,
                ["OemCloseBrackets"] = 0xDD,
                ["'"] = 0xDE,
                ["Oem7"] = 0xDE,
                ["OemQuotes"] = 0xDE,
                ["Oem102"] = 0xE2,
            };

        private static readonly Dictionary<ushort, string> PreferredNames =
            new()
            {
                [0x08] = "Backspace",
                [0x09] = "Tab",
                [0x0D] = "Enter",
                [0x10] = "Shift",
                [0x11] = "Ctrl",
                [0x12] = "Alt",
                [0x13] = "Pause",
                [0x14] = "CapsLock",
                [0x1B] = "Escape",
                [0x20] = "Space",
                [0x21] = "PageUp",
                [0x22] = "PageDown",
                [0x23] = "End",
                [0x24] = "Home",
                [0x25] = "Left",
                [0x26] = "Up",
                [0x27] = "Right",
                [0x28] = "Down",
                [0x2C] = "PrintScreen",
                [0x2D] = "Insert",
                [0x2E] = "Delete",
                [0x5B] = "Win",
                [0x5C] = "RWin",
                [0x5D] = "Apps",
                [0x6A] = "NumMultiply",
                [0x6B] = "NumAdd",
                [0x6C] = "NumSeparator",
                [0x6D] = "NumSubtract",
                [0x6E] = "NumDecimal",
                [0x6F] = "NumDivide",
                [0x90] = "NumLock",
                [0x91] = "ScrollLock",
                [0xA0] = "Shift",
                [0xA1] = "Shift",
                [0xA2] = "Ctrl",
                [0xA3] = "Ctrl",
                [0xA4] = "Alt",
                [0xA5] = "Alt",
                [0xBA] = ";",
                [0xBB] = "=",
                [0xBC] = ",",
                [0xBD] = "-",
                [0xBE] = ".",
                [0xBF] = "/",
                [0xC0] = "`",
                [0xDB] = "[",
                [0xDC] = "\\",
                [0xDD] = "]",
                [0xDE] = "'",
                [0xE2] = "Oem102",
            };

        public static bool TryParse(string? text, out ushort virtualKey)
        {
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string key = text.Trim();

            if (NamedKeys.TryGetValue(key, out virtualKey))
                return true;

            if (TryParseFunctionKey(key, out virtualKey))
                return true;

            if (TryParseNumPadDigit(key, out virtualKey))
                return true;

            if (TryParseSingleGlyph(key, out virtualKey))
                return true;

            if (TryParseHexOrDecimal(key, out virtualKey))
                return true;

            return false;
        }

        /// <summary>任意鍵盤 Virtual-Key 皆可格式化；未知鍵回 VK_XX 以保證可再解析。</summary>
        public static bool TryFormat(ushort virtualKey, out string displayName)
        {
            displayName = string.Empty;
            if (virtualKey == 0)
                return false;

            if (PreferredNames.TryGetValue(virtualKey, out string? preferred))
            {
                displayName = preferred;
                return true;
            }

            if (virtualKey is >= 0x70 and <= 0x87)
            {
                displayName = $"F{virtualKey - 0x70 + 1}";
                return true;
            }

            if (virtualKey is >= (ushort)'A' and <= (ushort)'Z')
            {
                displayName = ((char)virtualKey).ToString();
                return true;
            }

            if (virtualKey is >= (ushort)'0' and <= (ushort)'9')
            {
                displayName = ((char)virtualKey).ToString();
                return true;
            }

            // 與主鍵盤 0–9 區隔，避免小鍵盤被存成同名導致誤送
            if (virtualKey is >= 0x60 and <= 0x69)
            {
                displayName = $"Num{virtualKey - 0x60}";
                return true;
            }

            displayName = FormatAsVirtualKeyToken(virtualKey);
            return true;
        }

        private static bool TryParseFunctionKey(string key, out ushort virtualKey)
        {
            virtualKey = 0;
            if (key.Length is < 2 or > 3
                || !key.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!int.TryParse(key.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fNum))
                return false;

            // VK_F1..VK_F24
            if (fNum is < 1 or > 24)
                return false;

            virtualKey = (ushort)(0x70 + fNum - 1);
            return true;
        }

        private static bool TryParseNumPadDigit(string key, out ushort virtualKey)
        {
            virtualKey = 0;
            if (key.Length is < 4 or > 4)
                return false;

            if (!key.StartsWith("Num", StringComparison.OrdinalIgnoreCase))
                return false;

            char digit = key[3];
            if (digit is < '0' or > '9')
                return false;

            virtualKey = (ushort)(0x60 + (digit - '0'));
            return true;
        }

        private static bool TryParseSingleGlyph(string key, out ushort virtualKey)
        {
            virtualKey = 0;
            if (key.Length != 1)
                return false;

            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }

            return NamedKeys.TryGetValue(key, out virtualKey);
        }

        private static bool TryParseHexOrDecimal(string key, out ushort virtualKey)
        {
            virtualKey = 0;

            // VK_2D / vk_2d
            if (key.Length >= 4
                && key.StartsWith("VK_", StringComparison.OrdinalIgnoreCase)
                && ushort.TryParse(key.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey)
                && virtualKey != 0)
                return true;

            // 0x2D
            if (key.Length >= 3
                && key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ushort.TryParse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey)
                && virtualKey != 0)
                return true;

            // 純數字 Virtual-Key（進階／相容）
            if (ushort.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out virtualKey)
                && virtualKey != 0)
                return true;

            return false;
        }

        private static string FormatAsVirtualKeyToken(ushort virtualKey)
            => string.Create(CultureInfo.InvariantCulture, $"VK_{virtualKey:X2}");
    }
}
