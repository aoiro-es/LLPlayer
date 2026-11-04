using FlyleafLib.MediaFramework.MediaDemuxer;

namespace LLPlayer.Services;

public static class ChapterNavigation
{
    public static Demuxer.Chapter? Find(IEnumerable<Demuxer.Chapter>? chapters, long position, bool next)
    {
        var ordered = chapters?.OrderBy(c => c.StartTime).ToArray();
        if (ordered is not { Length: > 0 }) return null;
        // Ignore timestamp rounding at a chapter boundary; previous always selects a prior marker.
        const long tolerance = TimeSpan.TicksPerMillisecond * 100;
        return next ? ordered.FirstOrDefault(c => c.StartTime > position + tolerance)
            : ordered.LastOrDefault(c => c.StartTime < position - tolerance);
    }
}
