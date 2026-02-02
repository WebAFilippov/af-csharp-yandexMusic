using MediaControllerService.Models;
using Windows.Media.Control;
using WindowsMediaController;

namespace MediaControllerService.Services;

public class MediaWatcherService : IDisposable
{
    private static readonly string[] YANDEX_MUSIC_APP_IDS = { "Яндекс Музыка.exe", "Yandex Music.exe" };
    
    private MediaManager? _mediaManager;
    private readonly ThumbnailService _thumbnailService;
    private MediaManager.MediaSession? _yandexSession;
    private MediaSessionInfo? _sessionInfo;
    private string? _thumbnailCache;
    private readonly object _sessionLock = new object();
    private readonly AudioService _audioService;
    
    // Track previous values for change detection
    private MediaData? _lastMediaData;
    private VolumeData? _lastVolumeData;

    // New events - separated by type
    public event EventHandler<MediaData?>? OnMediaChanged;
    public event EventHandler<VolumeData>? OnVolumeChanged;
    public event EventHandler? OnSessionClosed;

    public MediaWatcherService(ThumbnailService thumbnailService, AudioService audioService)
    {
        _thumbnailService = thumbnailService;
        _audioService = audioService;

        // Listen to system volume changes from AudioService
        _audioService.OnVolumeChanged += (sender, info) =>
        {
            var newVolumeData = new VolumeData
            {
                Volume = (int)info.Volume,
                IsMuted = info.IsMuted
            };

            // Check if volume actually changed
            if (_lastVolumeData == null || 
                _lastVolumeData.Volume != newVolumeData.Volume || 
                _lastVolumeData.IsMuted != newVolumeData.IsMuted)
            {
                _lastVolumeData = newVolumeData;
                OnVolumeChanged?.Invoke(this, newVolumeData);
            }
        };
    }

    public void Start()
    {
        _mediaManager = new MediaManager();

        _mediaManager.OnAnySessionOpened += MediaManager_OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
        _mediaManager.OnAnyPlaybackStateChanged += MediaManager_OnAnyPlaybackStateChanged;
        _mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;

        _mediaManager.Start();

        // Check if Yandex Music is already running
        lock (_sessionLock)
        {
            var yandexSession = _mediaManager.CurrentMediaSessions.Values
                .FirstOrDefault(s => YANDEX_MUSIC_APP_IDS.Contains(s.Id));
            
            if (yandexSession != null)
            {
                RegisterYandexSession(yandexSession);
                // Send initial data
                _ = Task.Run(SendInitialDataAsync);
            }
        }
    }

    private async Task SendInitialDataAsync()
    {
        try
        {
            // Send current volume only - media data will be sent via OnAnyMediaPropertyChanged event
            var (volume, isMuted) = _audioService.GetVolumeInfo();
            var volumeData = new VolumeData
            {
                Volume = (int)volume,
                IsMuted = isMuted
            };
            _lastVolumeData = volumeData;
            OnVolumeChanged?.Invoke(this, volumeData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaWatcher] Error sending initial data: {ex.Message}");
        }
    }

    private bool IsYandexMusic(MediaManager.MediaSession session)
    {
        return YANDEX_MUSIC_APP_IDS.Contains(session.Id);
    }

    private void RegisterYandexSession(MediaManager.MediaSession session)
    {
        var guid = Guid.NewGuid().ToString("N");
        _yandexSession = session;
        _sessionInfo = new MediaSessionInfo
        {
            Guid = guid,
            AppId = session.Id,
            LastMediaProperties = null,
            LastPlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped,
            LastThumbnailBase64 = null
        };
    }

    private void MediaManager_OnAnySessionOpened(MediaManager.MediaSession session)
    {
        if (!IsYandexMusic(session))
            return;

        lock (_sessionLock)
        {
            RegisterYandexSession(session);
        }

        // Media data will be sent via OnAnyMediaPropertyChanged event when available
    }

    private void MediaManager_OnAnySessionClosed(MediaManager.MediaSession session)
    {
        if (!IsYandexMusic(session))
            return;

        lock (_sessionLock)
        {
            _yandexSession = null;
            _sessionInfo = null;
            _thumbnailCache = null;
            _lastMediaData = null;
        }

        OnSessionClosed?.Invoke(this, EventArgs.Empty);
    }

    private void MediaManager_OnAnyPlaybackStateChanged(MediaManager.MediaSession sender, GlobalSystemMediaTransportControlsSessionPlaybackInfo args)
    {
        if (!IsYandexMusic(sender))
            return;

        lock (_sessionLock)
        {
            if (_sessionInfo != null)
            {
                _sessionInfo.LastPlaybackStatus = args.PlaybackStatus;
            }
        }

        // Playback status changed - media data will be updated via OnAnyMediaPropertyChanged event
    }

    private void MediaManager_OnAnyMediaPropertyChanged(MediaManager.MediaSession sender, GlobalSystemMediaTransportControlsSessionMediaProperties args)
    {
        if (!IsYandexMusic(sender))
            return;

        // Only process if we have a valid title
        if (!IsValidSessionData(args.Title))
            return;

        _ = Task.Run(() => ProcessMediaPropertyChangedAsync(args));
    }

    private async Task ProcessMediaPropertyChangedAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        try
        {
            string? thumbnailBase64 = _thumbnailCache;

            // Only fetch new thumbnail if artist/album changed
            var cacheKey = $"{props.Artist ?? ""}|{props.AlbumTitle ?? ""}";
            if (thumbnailBase64 == null && props.Thumbnail != null)
            {
                thumbnailBase64 = await _thumbnailService.GetThumbnailBase64Async(
                    props.Thumbnail,
                    props.Artist ?? string.Empty,
                    props.AlbumTitle ?? string.Empty
                );

                if (thumbnailBase64 != null)
                {
                    _thumbnailCache = thumbnailBase64;
                }
            }

            var playbackInfo = _yandexSession?.ControlSession?.GetPlaybackInfo();

            // Create new media data
            var newMediaData = new MediaData
            {
                Id = _sessionInfo?.Guid ?? Guid.NewGuid().ToString("N"),
                AppId = YANDEX_MUSIC_APP_IDS[0],
                AppName = "Яндекс Музыка",
                Title = props.Title ?? string.Empty,
                Artist = props.Artist ?? string.Empty,
                Album = props.AlbumTitle ?? string.Empty,
                PlaybackStatus = playbackInfo?.PlaybackStatus.ToString() ?? "Unknown",
                ThumbnailBase64 = thumbnailBase64,
                IsFocused = true
            };

            // Check if media data actually changed
            if (_lastMediaData == null ||
                _lastMediaData.Title != newMediaData.Title ||
                _lastMediaData.Artist != newMediaData.Artist ||
                _lastMediaData.Album != newMediaData.Album ||
                _lastMediaData.PlaybackStatus != newMediaData.PlaybackStatus ||
                _lastMediaData.ThumbnailBase64 != newMediaData.ThumbnailBase64)
            {
                _lastMediaData = newMediaData;
                OnMediaChanged?.Invoke(this, newMediaData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaWatcher] Error processing media properties: {ex.Message}");
        }
    }

    private bool IsValidSessionData(string title)
    {
        return !string.IsNullOrWhiteSpace(title);
    }

    public async Task<bool> SendCommandAsync(string command, int? stepPercent = null, int? value = null)
    {
        if (_yandexSession?.ControlSession == null)
            return false;

        try
        {
            switch (command.ToLowerInvariant())
            {
                case "play":
                    await _yandexSession.ControlSession.TryPlayAsync();
                    return true;
                case "pause":
                    await _yandexSession.ControlSession.TryPauseAsync();
                    return true;
                case "playpause":
                case "toggle":
                    await _yandexSession.ControlSession.TryTogglePlayPauseAsync();
                    return true;
                case "next":
                case "nexttrack":
                    await _yandexSession.ControlSession.TrySkipNextAsync();
                    return true;
                case "previous":
                case "prev":
                case "prevtrack":
                    await _yandexSession.ControlSession.TrySkipPreviousAsync();
                    return true;
                case "volume_up":
                    _audioService.VolumeUp(stepPercent ?? 3);
                    return true;
                case "volume_down":
                    _audioService.VolumeDown(stepPercent ?? 3);
                    return true;
                case "set_volume":
                case "setvolume":
                    if (value.HasValue)
                    {
                        _audioService.SetVolume(value.Value);
                        Console.WriteLine($"[MediaWatcher] Volume set to {value.Value}%");
                    }
                    return true;
                case "toggle_mute":
                    _audioService.ToggleMute();
                    return true;
                default:
                    Console.WriteLine($"[MediaWatcher] Unknown command: {command}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaWatcher] Error executing command '{command}': {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _mediaManager?.Dispose();
        _thumbnailService.ClearCache();
    }

    private class MediaSessionInfo
    {
        public string Guid { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public GlobalSystemMediaTransportControlsSessionMediaProperties? LastMediaProperties { get; set; }
        public GlobalSystemMediaTransportControlsSessionPlaybackStatus LastPlaybackStatus { get; set; }
        public string? LastThumbnailBase64 { get; set; }
    }
}
