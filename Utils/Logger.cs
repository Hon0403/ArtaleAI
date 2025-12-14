using Serilog;
using Serilog.Events;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 統一日誌管理器 - 封裝 Serilog 提供簡單易用的日誌 API
    /// 使用方式：Logger.Info("訊息"), Logger.Error("錯誤", ex)
    /// </summary>
    public static class Logger
    {
        private static ILogger? _logger;
        private static bool _isInitialized = false;
        
        /// <summary>
        /// 初始化日誌系統
        /// </summary>
        /// <param name="logDirectory">日誌目錄（預設：Logs）</param>
        /// <param name="enableConsole">是否啟用主控台輸出（預設：true）</param>
        public static void Initialize(string logDirectory = "Logs", bool enableConsole = true)
        {
            if (_isInitialized) return;
            
            // 確保日誌目錄存在
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            
            var logPath = Path.Combine(logDirectory, "artale-.log");
            
            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Application", "ArtaleAI")
                .WriteTo.Async(a => a.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    retainedFileCountLimit: 7,  // 保留最近 7 天的日誌
                    fileSizeLimitBytes: 10 * 1024 * 1024,  // 單檔最大 10MB
                    rollOnFileSizeLimit: true
                ));
            
            // Debug/Release 模式控制
            #if DEBUG
            config = config.MinimumLevel.Debug();
            #else
            config = config.MinimumLevel.Information();  // Release 模式只輸出 Info 以上
            #endif
            
            // 主控台輸出（可選）
            if (enableConsole)
            {
                config = config.WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
            }
            
            _logger = config.CreateLogger();
            _isInitialized = true;
            
            Info("[系統] Logger 已初始化");
        }
        
        /// <summary>
        /// 記錄錯誤級別日誌（需要立即處理的問題）
        /// </summary>
        public static void Error(string message, Exception? exception = null)
        {
            EnsureInitialized();
            if (exception != null)
                _logger!.Error(exception, message);
            else
                _logger!.Error(message);
        }
        
        /// <summary>
        /// 記錄警告級別日誌（值得注意但不致命）
        /// </summary>
        public static void Warning(string message)
        {
            EnsureInitialized();
            _logger!.Warning(message);
        }
        
        /// <summary>
        /// 記錄資訊級別日誌（重要的狀態變化）
        /// </summary>
        public static void Info(string message)
        {
            EnsureInitialized();
            _logger!.Information(message);
        }
        
        /// <summary>
        /// 記錄調試級別日誌（詳細追蹤，Release 模式不輸出）
        /// </summary>
        public static void Debug(string message)
        {
            EnsureInitialized();
            _logger!.Debug(message);
        }
        
        /// <summary>
        /// 關閉日誌系統（確保所有日誌都已寫入檔案）
        /// </summary>
        public static void Shutdown()
        {
            if (_isInitialized && _logger is IDisposable disposable)
            {
                Info("[系統] Logger 正在關閉...");
                disposable.Dispose();
                _isInitialized = false;
                _logger = null;
            }
        }
        
        /// <summary>
        /// 確保 Logger 已初始化（自動初始化機制）
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }
    }
}
