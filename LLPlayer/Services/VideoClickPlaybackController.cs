namespace LLPlayer.Services;

/// <summary>Responds immediately, restoring the first click's playback state when a double-click follows.</summary>
public sealed class VideoClickPlaybackController(Func<bool> isPlaying, Action play, Action pause)
{
    private long? _firstClickAt;
    private bool _wasPlaying;

    public void SingleClick(long nowMs)
    {
        _wasPlaying = isPlaying();
        _firstClickAt = nowMs;
        SetPlaying(!_wasPlaying);
    }

    public void DoubleClick(long nowMs, long doubleClickIntervalMs)
    {
        if (_firstClickAt is { } first && nowMs >= first && nowMs - first <= doubleClickIntervalMs)
            SetPlaying(_wasPlaying);
        _firstClickAt = null;
    }

    private void SetPlaying(bool playing)
    {
        if (isPlaying() == playing) return;
        if (playing) play(); else pause();
    }
}
