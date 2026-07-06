using System;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>從虛擬節點 ID 解析平台 ID（過渡期；長期應由建圖寫入 PlatformId）。</summary>
    public static class NavigationNodeIdParser
    {
        private const string PlatformPrefix = "n_v_plat_";

        public static bool TryParsePlatformId(string nodeId, out string platformId)
        {
            platformId = string.Empty;
            if (string.IsNullOrEmpty(nodeId) || !nodeId.StartsWith(PlatformPrefix, StringComparison.Ordinal))
                return false;

            var rest = nodeId.Substring(PlatformPrefix.Length);
            var parts = rest.Split('_');
            if (parts.Length < 3)
                return false;

            platformId = string.Join("_", parts, 0, parts.Length - 2);
            return !string.IsNullOrEmpty(platformId);
        }
    }
}
