using MediaControllerService.Models;
using SkiaSharp;
using Windows.Storage.Streams;

namespace MediaControllerService.Services;

public class ThumbnailService
{
    private readonly Dictionary<ThumbnailCacheKey, string> _cache = new();
    private readonly object _cacheLock = new object();
    private readonly ThumbnailConfig _config;

    public ThumbnailService(ThumbnailConfig config)
    {
        _config = config;
    }

    public async Task<string?> GetThumbnailBase64Async(IRandomAccessStreamReference? thumbnailReference, string artist, string album)
    {
        if (thumbnailReference == null)
            return null;

        var cacheKey = new ThumbnailCacheKey(artist, album);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cachedBase64))
            {
                return cachedBase64;
            }
        }

        try
        {
            using var stream = await thumbnailReference.OpenReadAsync();
            using var inputStream = stream.AsStreamForRead();
            using var skiaStream = new SKManagedStream(inputStream);
            using var originalBitmap = SKBitmap.Decode(skiaStream);

            if (originalBitmap == null)
                return null;

            using var resizedBitmap = ResizeAndCropToSquare(originalBitmap, _config.Size);
            using var image = SKImage.FromBitmap(resizedBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, _config.Quality);

            if (data == null)
                return null;

            var bytes = data.ToArray();
            var base64 = Convert.ToBase64String(bytes);

            lock (_cacheLock)
            {
                _cache[cacheKey] = base64;
            }

            return base64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThumbnailService] Error processing thumbnail: {ex.Message}");
            return null;
        }
    }

    private static SKBitmap ResizeAndCropToSquare(SKBitmap source, int targetSize)
    {
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        int cropSize = Math.Min(sourceWidth, sourceHeight);
        int cropX = (sourceWidth - cropSize) / 2;
        int cropY = (sourceHeight - cropSize) / 2;

        var cropped = new SKBitmap(cropSize, cropSize);
        source.ExtractSubset(cropped, new SKRectI(cropX, cropY, cropX + cropSize, cropY + cropSize));

        var resized = new SKBitmap(targetSize, targetSize);
        cropped.ScalePixels(resized, SKFilterQuality.High);

        cropped.Dispose();

        return resized;
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }
}
