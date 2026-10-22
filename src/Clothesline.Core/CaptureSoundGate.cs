namespace Clothesline.Core;

/// Receives only committed automatic captures. Never queues a deferred sound.
public sealed class CaptureSoundGate
{
    private long? lastPlayed;
    public bool ShouldPlay(long elapsedMilliseconds, bool enabled, bool paused, bool fullscreen)
    {
        if (!enabled || paused || fullscreen || (lastPlayed is long last && elapsedMilliseconds - last < 2000)) return false;
        lastPlayed = elapsedMilliseconds;
        return true;
    }
}
