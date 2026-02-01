namespace MediaControllerService.Models;

public class ThumbnailCacheKey
{
    public string Artist { get; }
    public string Album { get; }

    public ThumbnailCacheKey(string artist, string album)
    {
        Artist = artist ?? string.Empty;
        Album = album ?? string.Empty;
    }

    public override bool Equals(object? obj)
    {
        if (obj is ThumbnailCacheKey other)
        {
            return Artist.Equals(other.Artist, StringComparison.OrdinalIgnoreCase) &&
                   Album.Equals(other.Album, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Artist.ToLowerInvariant(),
            Album.ToLowerInvariant()
        );
    }

    public override string ToString() => $"{Artist}|{Album}";
}
