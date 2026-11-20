namespace Mizon.Windows.PreviewXaml;

internal struct PreviewCountMotion
{
    private int? _from, _to;
    private long _start, _end;

    internal bool IsRunning(long now) => now < _end;

    internal void Set(int? value, bool animate, long now)
    {
        if (animate && value == _to) return;
        var current = Read(now);
        _to = value;
        _from = current;
        _start = now;
        _end = animate && current is int previous && value is int next && previous != next
            ? now + Math.Clamp(Math.Abs((long)next - previous) * 58L, 380L, 1_150L)
            : now;
    }

    internal int? Read(long now)
    {
        if (now >= _end || _from is not int from || _to is not int to) return _to;
        var progress = Math.Clamp((double)(now - _start) / (_end - _start), 0d, 1d);
        var smooth = progress * progress * (3 - 2 * progress);
        return (int)Math.Round(from + (to - (double)from) * smooth);
    }
}
