using MediaControllerService.Services;

namespace MediaControllerService;

class Program
{
    private static StdioCommunicationService? _stdioService;
    private static MediaWatcherService? _mediaWatcher;
    private static ThumbnailService? _thumbnailService;
    private static AudioService? _audioService;
    private static readonly ManualResetEventSlim _shutdownEvent = new(false);

    private static AppConfig ParseArguments(string[] args)
    {
        var config = new AppConfig();
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();
            
            // Parse --thumbnail-size=150 or --thumbnail-size 150
            if (arg.StartsWith("--thumbnail-size="))
            {
                var value = arg.Substring("--thumbnail-size=".Length);
                if (int.TryParse(value, out int size) && size > 0 && size <= 1000)
                {
                    config.Thumbnail.Size = size;
                }
            }
            else if (arg == "--thumbnail-size" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int size) && size > 0 && size <= 1000)
                {
                    config.Thumbnail.Size = size;
                    i++; // Skip next argument
                }
            }
            // Parse --thumbnail-quality=85 or --thumbnail-quality 85
            else if (arg.StartsWith("--thumbnail-quality="))
            {
                var value = arg.Substring("--thumbnail-quality=".Length);
                if (int.TryParse(value, out int quality) && quality >= 1 && quality <= 100)
                {
                    config.Thumbnail.Quality = quality;
                }
            }
            else if (arg == "--thumbnail-quality" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int quality) && quality >= 1 && quality <= 100)
                {
                    config.Thumbnail.Quality = quality;
                    i++; // Skip next argument
                }
            }
            else if (arg == "--help" || arg == "-h")
            {
                PrintHelp();
                Environment.Exit(0);
            }
        }
        
        Console.WriteLine($"[Config] Thumbnail Size={config.Thumbnail.Size}, Quality={config.Thumbnail.Quality}");
        return config;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("YandexMusicController - Windows Media Controller for Yandex Music");
        Console.WriteLine();
        Console.WriteLine("Usage: YandexMusicController.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --thumbnail-size <pixels>     Thumbnail size in pixels (default: 150)");
        Console.WriteLine("  --thumbnail-quality <1-100>   JPEG quality (default: 85)");
        Console.WriteLine("  --help, -h                    Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  YandexMusicController.exe");
        Console.WriteLine("  YandexMusicController.exe --thumbnail-size 200 --thumbnail-quality 90");
        Console.WriteLine("  YandexMusicController.exe --thumbnail-size=200 --thumbnail-quality=90");
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MediaControllerService starting...");
        
        // Parse command-line arguments
        var config = ParseArguments(args);
        
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Unhandled exception: {e.ExceptionObject}");
                Environment.Exit(1);
            };

            _thumbnailService = new ThumbnailService(config.Thumbnail);
            _audioService = new AudioService();
            _mediaWatcher = new MediaWatcherService(_thumbnailService);
            _stdioService = new StdioCommunicationService(_mediaWatcher, _audioService);

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
