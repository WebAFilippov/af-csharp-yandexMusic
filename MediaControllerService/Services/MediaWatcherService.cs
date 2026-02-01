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
    private (float Volume, bool IsMuted) _currentVolumeInfo;

    public event EventHandler<MediaSessionDto?>? OnSessionChanged;
    public event EventHandler<MediaSessionDto?>? OnSessionUpdated;

    public MediaWatcherService(ThumbnailService thumbnailService, AudioService audioService)
    {
        _thumbnailService = thumbnailService;
        _audioService = audioService;

        _audioService.OnVolumeChanged += (sender, info) =>
        {
            _currentVolumeInfo = info;
            // Trigger session update with new volume
            var dto = CreateSessionDtoFromExistingData();
            if (dto != null && IsValidSessionData(dto.Title))
            {
                OnSessionUpdated?.Invoke(this, dto);
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
                .FirstOrDefault(s => s.Id == YANDEX_MUSIC_APP_IDS[0]);
            
            if (yandexSession != null)
            {
                RegisterYandexSession(yandexSession);
            }
        }

        NotifySessionChanged();
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

        NotifySessionChanged();
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
        }

        NotifySessionChanged();
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

        var dto = CreateSessionDtoFromExistingData();
        if (dto != null && IsValidSessionData(dto.Title))
        {
            OnSessionUpdated?.Invoke(this, dto);
        }
    }

    private void MediaManager_OnAnyMediaPropertyChanged(MediaManager.MediaSession sender, GlobalSystemMediaTransportControlsSessionMediaProperties args)
    {
        if (!IsYandexMusic(sender))
            return;

        // Only process if we have a valid title
        if (!IsValidSessionData(args.Title))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                string? thumbnailBase64 = _thumbnailCache;

                if (thumbnailBase64 == null && args.Thumbnail != null)
                {
                    thumbnailBase64 = await _thumbnailService.GetThumbnailBase64Async(
                        args.Thumbnail,
                        args.Artist ?? string.Empty,
                        args.AlbumTitle ?? string.Empty
                    );

                    if (thumbnailBase64 != null)
                    {
                        _thumbnailCache = thumbnailBase64;
                    }
                }

                lock (_sessionLock)
                {
                    if (_sessionInfo != null)
                    {
                        _sessionInfo.LastMediaProperties = args;
                        _sessionInfo.LastThumbnailBase64 = thumbnailBase64;
                    }
                }

                var dto = CreateSessionDto(args, thumbnailBase64);
                if (dto != null && IsValidSessionData(dto.Title))
                {
                    OnSessionUpdated?.Invoke(this, dto);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaWatcher] Error processing media properties: {ex.Message}");
            }
        });
    }

    private bool IsValidSessionData(string title)
    {
        return !string.IsNullOrWhiteSpace(title);
    }

    private void NotifySessionChanged()
    {
        var dto = CreateSessionDtoFromExistingData();
        OnSessionChanged?.Invoke(this, dto);
    }

    private MediaSessionDto? CreateSessionDto(GlobalSystemMediaTransportControlsSessionMediaProperties props, string? thumbnailBase64)
    {
        if (_yandexSession == null || _sessionInfo == null)
            return null;

        var playbackInfo = _yandexSession.ControlSession?.GetPlaybackInfo();

        return new MediaSessionDto
        {
            Id = _sessionInfo.Guid,
            AppId = YANDEX_MUSIC_APP_IDS[0],
            AppName = "Яндекс Музыка",
            Title = props.Title ?? string.Empty,
            Artist = props.Artist ?? string.Empty,
            Album = props.AlbumTitle ?? string.Empty,
            PlaybackStatus = playbackInfo?.PlaybackStatus.ToString() ?? "Unknown",
            ThumbnailBase64 = thumbnailBase64,
            IsFocused = true,
            Volume = (int)_currentVolumeInfo.Volume,
            IsMuted = _currentVolumeInfo.IsMuted
        };
    }

    private MediaSessionDto? CreateSessionDtoFromExistingData()
    {
        if (_yandexSession == null || _sessionInfo == null)
            return null;

        var props = _sessionInfo.LastMediaProperties;
        var playbackStatus = _sessionInfo.LastPlaybackStatus;

        return new MediaSessionDto
        {
            Id = _sessionInfo.Guid,
            AppId = YANDEX_MUSIC_APP_IDS[0],
            AppName = "Яндекс Музыка",
            Title = props?.Title ?? string.Empty,
            Artist = props?.Artist ?? string.Empty,
            Album = props?.AlbumTitle ?? string.Empty,
            PlaybackStatus = playbackStatus.ToString(),
            ThumbnailBase64 = _sessionInfo.LastThumbnailBase64,
            IsFocused = true,
            Volume = (int)_currentVolumeInfo.Volume,
            IsMuted = _currentVolumeInfo.IsMuted
        };
    }

    public MediaSessionDto? GetCurrentSession()
    {
        var dto = CreateSessionDtoFromExistingData();
        if (dto != null && IsValidSessionData(dto.Title))
            return dto;
        return null;
    }

    public async Task<bool> SendCommandAsync(string command, int? stepPercent = null, int? value = null)
    {
        try
        {
            switch (command.ToLowerInvariant())
            {
                case "play":
                    if (_yandexSession?.ControlSession == null) return false;
                    await _yandexSession.ControlSession.TryPlayAsync();
                    return true;
                case "pause":
                    if (_yandexSession?.ControlSession == null) return false;
                    await _yandexSession.ControlSession.TryPauseAsync();
                    return true;
                case "playpause":
                case "toggle":
                    if (_yandexSession?.ControlSession == null) return false;
                    await _yandexSession.ControlSession.TryTogglePlayPauseAsync();
                    return true;
                case "next":
                case "nexttrack":
                    if (_yandexSession?.ControlSession == null) return false;
                    await _yandexSession.ControlSession.TrySkipNextAsync();
                    return true;
                case "previous":
                case "prev":
                case "prevtrack":
                    if (_yandexSession?.ControlSession == null) return false;
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
