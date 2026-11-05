using System.Windows;

namespace LLPlayer.Services;

public static class PreparationHoverPolicy
{
    public static bool Reveal(Point cursor, Size viewport, Size progress, double playbackBarHeight)
    {
        if (!double.IsFinite(cursor.X) || !double.IsFinite(cursor.Y) || viewport.Width <= 0 || viewport.Height <= 0
            || cursor.X < 0 || cursor.Y < 0 || cursor.X > viewport.Width || cursor.Y > viewport.Height) return false;
        double width = Math.Min(viewport.Width, Math.Max(240, progress.Width + 16));
        double height = Math.Min(viewport.Height, Math.Max(120, progress.Height + playbackBarHeight + 16));
        return cursor.X >= viewport.Width - width && cursor.Y >= viewport.Height - height;
    }
}
