// Derived from DevWinUI ParticleModel and InternalExtensions (MIT).
// The original depth radius, velocity, integer random placement and bounce formulas
// are retained. See README.md for the pinned source and local lifetime changes.
using System;
using System.Numerics;

namespace Mizon.Windows.Controls;

internal sealed class ParticleModel
{
    private Vector3 _head;
    private int _step;

    public int Step => _step;
    public Vector3 Container { get; internal set; }
    public Vector3 Offset { get; internal set; }
    public Vector3 Head => _head;

    public ParticleModel(Vector3 container, Vector3 head, Vector3 offset)
    {
        Container = container;
        _head = head;
        Offset = offset;
    }

    public int NextStep()
    {
        _step++;
        var next = Offset + Head;
        if (next.X < 0) { next.X = 0; _head.X = -_head.X; }
        if (next.X > Container.X) { next.X = Container.X; _head.X = -_head.X; }
        if (next.Y < 0) { next.Y = 0; _head.Y = -_head.Y; }
        if (next.Y > Container.Y) { next.Y = Container.Y; _head.Y = -_head.Y; }
        if (next.Z < 25) { next.Z = 25; _head.Z = -_head.Z; }
        if (next.Z > Container.Z) { next.Z = Container.Z; _head.Z = -_head.Z; }
        Offset = next;
        return _step;
    }

    public float GetRange(Vector3 sourceOffset) => GetRange(new Vector2(sourceOffset.X, sourceOffset.Y));

    public float GetRange(Vector2 sourceOffset)
    {
        var x = Offset.X - sourceOffset.X;
        var y = Offset.Y - sourceOffset.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    public void ResetOffset(Vector3 container) => Offset = NextVector3(container);

    public void ResetOffset(Vector2 container)
        => ResetOffset(new Vector3(container.X, container.Y, Random.Shared.Next(30, 61)));

    public static ParticleModel CreateParticle(Vector2 container)
        => CreateParticle(container.X, container.Y, Random.Shared.Next(30, 61));

    public static ParticleModel CreateParticle(double width, double height, double depth)
        => CreateParticle(new Vector3(Convert.ToSingle(width), Convert.ToSingle(height), Convert.ToSingle(depth)));

    public static ParticleModel CreateParticle(float width, float height, float depth)
        => CreateParticle(new Vector3(width, height, depth));

    public static ParticleModel CreateParticle(Vector3 container)
    {
        var head = new Vector3(
            Convert.ToSingle(Random.Shared.NextDouble() / 2),
            Convert.ToSingle(RandomNegative(Random.Shared.NextDouble())) / 2,
            Convert.ToSingle(RandomNegative(Random.Shared.NextDouble())) / 2);
        return new ParticleModel(container, head, NextVector3(container));
    }

    private static Vector3 NextVector3(Vector3 value) => new(
        Random.Shared.Next(Convert.ToInt32(value.X)),
        Random.Shared.Next(Convert.ToInt32(value.Y)),
        Random.Shared.Next(Convert.ToInt32(value.Z)));

    private static double RandomNegative(double value) => Random.Shared.Next(0, 2) == 0 ? -value : value;
}
