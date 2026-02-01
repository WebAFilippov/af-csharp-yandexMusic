using System.Text.Json.Serialization;

namespace MediaControllerService.Models;

public class MediaSessionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("appId")]
    public string AppId { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("playbackStatus")]
    public string PlaybackStatus { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailBase64")]
    public string? ThumbnailBase64 { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("volume")]
    public int Volume { get; set; }

    [JsonPropertyName("isMuted")]
    public bool IsMuted { get; set; }
}
