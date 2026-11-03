namespace LLPlayer.Services;

public static class SubtitleLayoutPolicy
{
    public static bool Reveal(SubtitleTrackPlacement placement, bool onlyOnHover,
        double x, double y, double width, double height, double regionHeight)
    {
        if (!onlyOnHover) return true;
        if (x < 0 || x > width || y < 0 || y > height || height <= 0) return false;
        return placement == SubtitleTrackPlacement.Top ? y <= regionHeight : y >= height - regionHeight;
    }

    public static bool UseAbsoluteBitmap(bool absolute, SubtitleTrackPlacement placement) =>
        absolute && placement == SubtitleTrackPlacement.Legacy;
}
