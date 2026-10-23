namespace Clothesline.Core;

public readonly record struct VideoTrimRange(double Start, double End)
{
    public double Seconds => End - Start;
    public static VideoTrimRange Create(double start, double end, double duration)
    {
        // Players and demuxers can disagree slightly at the last audio packet.
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(duration) || duration <= 0 || start < 0 || end > duration + .1)
            throw new ArgumentException(T("裁剪时间必须在录像范围内。"));
        end = Math.Min(end, duration);
        if (end - start < .1 - 1e-9) throw new ArgumentException(T("保留片段至少需要 0.1 秒，结束时间须晚于开始时间。"));
        return new(start, end);
    }
}
