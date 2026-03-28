using ArtaleAI.Utils;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ArtaleAI.Core.Vision
{
    /// <summary>
    /// 模板管理器 — 統一管理所有 OpenCV Mat 模板的載入、快取與生命週期
    /// </summary>
    /// <remarks>
    /// 架構考量：使用 ConcurrentDictionary 進行線程安全的緩存讀寫，解決主執行緒與背景更新之間的競態條件。
    /// 所有模板 Mat 由此類別統一負責 Dispose，呼叫者僅取得參考。
    /// </remarks>
    public sealed class TemplateManager : ITemplateManager
    {
        private readonly ConcurrentDictionary<string, Mat> _cache = new();

        private static readonly string[] DefaultExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        private readonly object _disposeLock = new();
        private bool _disposed = false;

        #region ITemplateManager 實作

        /// <inheritdoc/>
        public Mat? GetTemplate(string templateName)
        {
            ThrowIfDisposed();
            return _cache.TryGetValue(templateName, out var mat)
                ? mat
                : null;
        }

        /// <inheritdoc/>
        public async Task<bool> LoadTemplateAsync(string templateName, string filePath)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(templateName))
                throw new ArgumentNullException(nameof(templateName));

            if (!File.Exists(filePath))
            {
                Logger.Warning($"[TemplateManager] 找不到模板圖檔: {filePath}");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var mat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                    if (mat == null || mat.Empty())
                    {
                        Logger.Warning($"[TemplateManager] 讀取失敗或空圖: {filePath}");
                        return false;
                    }

                    if (_cache.TryRemove(templateName, out var oldMat))
                    {
                        oldMat?.Dispose();
                    }

                    _cache[templateName] = mat;
                    Logger.Debug($"[TemplateManager] 成功載入模板: {templateName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[TemplateManager] 載入模板例外: {templateName} — {ex.Message}");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> LoadTemplatesFromFolderAsync(
            string folderPath,
            IEnumerable<string>? extensions = null)
        {
            ThrowIfDisposed();

            if (!Directory.Exists(folderPath))
            {
                Logger.Warning($"[TemplateManager] 資料夾不存在: {folderPath}");
                return Array.Empty<string>();
            }

            var allowedExts = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                              ?? new HashSet<string>(DefaultExtensions, StringComparer.OrdinalIgnoreCase);

            var files = Directory.GetFiles(folderPath)
                .Where(f => allowedExts.Contains(Path.GetExtension(f)))
                .ToList();

            var loadedNames = new List<string>();

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                bool success = await LoadTemplateAsync(name, file);
                if (success)
                    loadedNames.Add(name);
            }

            Logger.Info($"[TemplateManager] 批次載入完成: {loadedNames.Count}/{files.Count} 個模板 from {folderPath}");
            return loadedNames;
        }

        /// <inheritdoc/>
        public void UnloadTemplate(string templateName)
        {
            if (_disposed) return;

            if (_cache.TryRemove(templateName, out var mat))
            {
                SafeDisposeMat(mat, templateName);
            }
        }

        /// <inheritdoc/>
        public void UnloadAll()
        {
            if (_disposed) return;

            var keys = _cache.Keys.ToList();
            foreach (var key in keys)
            {
                UnloadTemplate(key);
            }
        }

        /// <inheritdoc/>
        public bool IsLoaded(string templateName)
        {
            if (_disposed) return false;
            return _cache.ContainsKey(templateName);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetLoadedTemplateNames()
        {
            ThrowIfDisposed();
            return _cache.Keys.ToList();
        }

        #endregion

        #region IDisposable 實作

        /// <summary>釋放所有模板 Mat 資源</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (disposing)
            {
                foreach (var kvp in _cache)
                {
                    SafeDisposeMat(kvp.Value, kvp.Key);
                }
                _cache.Clear();
                Logger.Debug("[TemplateManager] 所有模板 Mat 已釋放");
            }
        }

        ~TemplateManager()
        {
            Dispose(false);
        }

        #endregion

        #region 私有輔助方法

        private static void SafeDisposeMat(Mat? mat, string name)
        {
            try
            {
                if (mat != null && !mat.IsDisposed)
                    mat.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warning($"[TemplateManager] 釋放 Mat '{name}' 時發生例外: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TemplateManager),
                    "TemplateManager 已被 Dispose，無法繼續存取模板。");
        }

        #endregion
    }
}
