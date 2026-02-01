using System.Text.Json;
using MediaControllerService.Services;

namespace MediaControllerService;

class Program
{
    private static StdioCommunicationService? _stdioService;
    private static MediaWatcherService? _mediaWatcher;
    private static ThumbnailService? _thumbnailService;
    private static AudioService? _audioService;
    private static readonly ManualResetEventSlim _shutdownEvent = new(false);

    private static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists("config.json"))
            {
                var json = File.ReadAllText("config.json");
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    Console.WriteLine($"[Config] Loaded from config.json: Thumbnail Size={config.Thumbnail.Size}, Quality={config.Thumbnail.Quality}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Error loading config.json: {ex.Message}");
        }
        
        Console.WriteLine("[Config] Using default configuration");
        return new AppConfig();
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MediaControllerService starting...");
        
        // Load configuration
        var config = LoadConfig();
        
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Unhandled exception: {e.ExceptionObject}");
                Environment.Exit(1);
            };

            _thumbnailService = new ThumbnailService(config.Thumbnail);
            _audioService = new AudioService();
            _mediaWatcher = new MediaWatcherService(_thumbnailService, _audioService);
            _stdioService = new StdioCommunicationService(_mediaWatcher);

            _stdioService.OnCloseRequested += (sender, command) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Close command received");
                _shutdownEvent.Set();
            };

            _stdioService.OnClientDisconnected += (sender, e) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Parent disconnected, shutting down...");
                _shutdownEvent.Set();
            };

            // Start MediaWatcher
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Initializing MediaWatcher...");
            _mediaWatcher.Start();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MediaWatcher initialized");

            // Start stdin/stdout communication
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting stdio communication...");
            _stdioService.Start();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Ready! Waiting for commands...");

            _shutdownEvent.Wait();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Shutting down gracefully...");

            _stdioService?.Dispose();
            _mediaWatcher?.Dispose();
            _audioService?.Dispose();
            _thumbnailService?.ClearCache();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Service stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Fatal error: {ex}");
            Environment.Exit(1);
        }
    }
}
