namespace MediaControllerService;

public class AppConfig
{
    public ThumbnailConfig Thumbnail { get; set; } = new ThumbnailConfig();
}

public class ThumbnailConfig
{
    public int Size { get; set; } = 150;
    public int Quality { get; set; } = 85;
}
