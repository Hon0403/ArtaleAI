using Serilog;
using Serilog.Events;

namespace ArtaleAI.Utils
{
    /// <summary>Serilog 薄包裝：檔案（每次啟動新檔）與可選主控台。</summary>
    public static class Logger
    {
        private static ILogger? _logger;
        private static bool _isInitialized = false;
        
        public static void Initialize(string logDirectory = "Logs", bool enableConsole = true)
        {
            if (_isInitialized) return;

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var logPath = Path.Combine(logDirectory, $"artale-{timestamp}.log");
            
            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Application", "ArtaleAI")
                .WriteTo.Async(a => a.File(
                    logPath,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    retainedFileCountLimit: 20,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true
                ));

            #if DEBUG
            config = config.MinimumLevel.Debug();
            #else
            config = config.MinimumLevel.Information();
            #endif

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
        
        public static void Error(string message, Exception? exception = null)
        {
            EnsureInitialized();
            if (exception != null)
                _logger!.Error(exception, message);
            else
                _logger!.Error(message);
        }
        
        public static void Warning(string message)
        {
            EnsureInitialized();
            _logger!.Warning(message);
        }
        
        public static void Info(string message)
        {
            EnsureInitialized();
            _logger!.Information(message);
        }
        
        public static void Debug(string message)
        {
            EnsureInitialized();
            _logger!.Debug(message);
        }
        
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
        
        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }
    }
}
