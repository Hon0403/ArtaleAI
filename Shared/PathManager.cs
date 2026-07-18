namespace ArtaleAI.Shared
{
    /// <summary>
    /// 執行期路徑 SSOT：以專案根目錄（含 <c>ArtaleAI.csproj</c>）為內容根，
    /// 避免 <c>bin/Debug/netX.0</c> 隨 TFM 變動導致 MapData／模板／設定分裂。
    /// </summary>
    public static class PathManager
    {
        private static readonly Lazy<string> ContentRootLazy = new(ResolveContentRoot);

        /// <summary>專案根目錄；找不到 csproj 時退回 <c>%LocalAppData%/ArtaleAI</c>。</summary>
        public static string ContentRoot => ContentRootLazy.Value;

        /// <summary>地圖 JSON 目錄。</summary>
        public static string MapDataDirectory => Path.Combine(ContentRoot, "MapData");

        /// <summary>怪物模板根目錄（與 csproj <c>templates/monsters</c> 一致）。</summary>
        public static string MonstersDirectory => Path.Combine(ContentRoot, "templates", "monsters");

        /// <summary>YAML 組態檔完整路徑。</summary>
        public static string ConfigFilePath => Path.Combine(ContentRoot, "Data", "config.yaml");

        /// <summary>日誌輸出目錄。</summary>
        public static string LogsDirectory => Path.Combine(ContentRoot, "Logs");

        private static string ResolveContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ArtaleAI.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArtaleAI");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
