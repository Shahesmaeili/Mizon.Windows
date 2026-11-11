using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mizon.Windows.Controls;

/// <summary>UI-thread-owned, bounded particle storage. Resize never rebuilds unchanged particles.</summary>
internal sealed class ParticleScene
{
    // Pair links are O(n²). Bound extreme density/monitor sizes while preserving the
    // upstream count formula for normal VPN page sizes.
    internal const int MaximumParticleCount = 256;
    private const double MaximumCoordinate = 32768;
    private double _width;
    private double _height;
    private int _density = -1;
    public List<ParticleModel> Particles { get; } = new(MaximumParticleCount);

    public bool Resize(double width, double height, int density)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            var changed = Particles.Count != 0;
            Clear();
            return changed;
        }

        width = Math.Min(width, MaximumCoordinate);
        height = Math.Min(height, MaximumCoordinate);
        density = density < 0 ? 5 : Math.Min(density, 9);
        if (_width == width && _height == height && _density == density) return false;
        _width = width;
        _height = height;
        _density = density;

        var count = Convert.ToInt32(Math.Min(MaximumParticleCount, Math.Min(width, height) / (10 - density)));
        var container = new Vector2((float)width, (float)height);
        while (Particles.Count < count) Particles.Add(ParticleModel.CreateParticle(container));
        if (Particles.Count > count) Particles.RemoveRange(count, Particles.Count - count);
        foreach (var particle in Particles)
        {
            particle.Container = new Vector3(container.X, container.Y, particle.Container.Z);
            if (particle.Offset.X > container.X || particle.Offset.Y > container.Y)
                particle.ResetOffset(container);
        }
        return true;
    }

    public void Clear()
    {
        Particles.Clear();
        _width = _height = 0;
        _density = -1;
    }
}
