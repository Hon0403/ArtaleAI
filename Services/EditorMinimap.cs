using ArtaleAI.Config;
using ArtaleAI.Minimap;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 編輯器小地圖服務 - 極簡版本
    /// </summary>
    public class EditorMinimap
    {
        private GraphicsCaptureItem? _selectedCaptureItem;

        /// <summary>
        /// 載入小地圖快照 - 直接返回 AnalyzeMap 的結果
        /// </summary>
        public async Task<MinimapSnapshotResult?> LoadSnapshotAsync(
            IntPtr windowHandle,
            AppConfig config,
            Action<string>? progressReporter = null)
        {
            var result = await AnalyzeMap.GetSnapshotAsync(
                windowHandle,
                config,
                _selectedCaptureItem,
                progressReporter);

            if (result?.MinimapImage != null && result.CaptureItem != null)
            {
                // 更新狀態
                _selectedCaptureItem = result.CaptureItem;

                // 配置更新邏輯（如果需要的話）
                if (config.General.LastSelectedWindowName != result.CaptureItem.DisplayName)
                {
                    config.General.LastSelectedWindowName = result.CaptureItem.DisplayName;
                    ConfigSaver.SaveConfig(config);
                    progressReporter?.Invoke($"提示：已將預設捕捉視窗更新為 '{result.CaptureItem.DisplayName}'。");
                }
            }
            else if (_selectedCaptureItem != null && result?.CaptureItem == null)
            {
                _selectedCaptureItem = null;
            }

            return result;
        }
    }
}
