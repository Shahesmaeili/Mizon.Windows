using Mizon.Windows.Controls;
using System.Diagnostics;
using System.Numerics;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

var bounce = new ParticleModel(new Vector3(100, 80, 60), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(99.9f, 0.1f, 59.9f));
Check(bounce.NextStep() == 1, "The original simulation advances exactly one step.");
Check(bounce.Offset == new Vector3(100, 0, 60), "All three bounds clamp on the crossing frame.");
Check(bounce.Head == new Vector3(-0.5f, 0.5f, -0.5f), "All three velocity axes bounce independently.");
bounce.Offset = new Vector3(4, 3, 25);
Check(bounce.GetRange(Vector2.Zero) == 5, "Link range depends on XY, not perspective depth.");
Check(bounce.GetRange(new Vector3(0, 0, 900)) == 5, "The Vector3 link overload also ignores depth.");
bounce.NextStep();
Check(bounce.Offset.Z == 25 && bounce.Head.Z > 0, "The near depth plane bounces at 25.");

var scene = new ParticleScene();
Check(scene.Resize(1440, 720, 5), "Initial layout produces a scene.");
Check(scene.Particles.Count == 144, "The normal upstream density formula is unchanged.");
var retained = scene.Particles[0];
Check(!scene.Resize(1440, 720, 5), "Unchanged layout does not rebuild the scene.");
scene.Resize(1600, 720, 5);
Check(ReferenceEquals(retained, scene.Particles[0]), "Width-only resize retains existing models.");
Check(scene.Particles[0].Container.X == 1600, "Retained particles adopt new bounds.");
scene.Resize(1600, 720, -5);
Check(scene.Particles.Count == 144, "Negative density retains upstream default five.");
scene.Resize(double.MaxValue, double.MaxValue, int.MaxValue);
Check(scene.Particles.Count == ParticleScene.MaximumParticleCount, "Extreme dimensions/density cannot grow quadratic work without a bound.");

foreach (var size in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 0d })
{
    scene.Resize(size, 720, 5);
    Check(scene.Particles.Count == 0, "Invalid width is safe and clears particles.");
    scene.Resize(1440, size, 5);
    Check(scene.Particles.Count == 0, "Invalid height is safe and clears particles.");
}

// Repeated shrink/expand and density changes exercise particles leaving old bounds,
// not only the same count calculation repeated in a fixture.
for (var cycle = 0; cycle < 600; cycle++)
{
    var width = 31 + cycle * 149 % 1900;
    var height = 27 + cycle * 73 % 1100;
    scene.Resize(width, height, cycle % 13 - 2);
    Check(scene.Particles.Count <= ParticleScene.MaximumParticleCount, "Resizing keeps the scene bounded.");
    foreach (var particle in scene.Particles)
    {
        particle.NextStep();
        Check(particle.Offset.X >= 0 && particle.Offset.X <= width, "X remains inside resized bounds.");
        Check(particle.Offset.Y >= 0 && particle.Offset.Y <= height, "Y remains inside resized bounds.");
        Check(particle.Offset.Z >= 25 && particle.Offset.Z <= particle.Container.Z, "Depth remains finite and inside its original range.");
    }
}

scene.Clear();
Check(scene.Particles.Count == 0, "Unload clears particle references.");
Check(scene.Resize(1440, 720, 5), "Reload with the same size recreates the scene.");
Check(scene.Particles.Count == 144, "Reload restores the expected density.");

static void RunFrames(ParticleScene scene, int frames)
{
    for (var frame = 0; frame < frames; frame++)
    {
        for (var i = 0; i < scene.Particles.Count; i++) scene.Particles[i].NextStep();
        scene.Resize(1440, 720, 5);
    }
}

RunFrames(scene, 4000); // Warm JIT and Random before measuring production work.
var started = Stopwatch.GetTimestamp();
var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
RunFrames(scene, 10000);
var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
var elapsed = Stopwatch.GetElapsedTime(started);
Check(allocatedBytes == 0, "Steady physics and unchanged resize must allocate no managed bytes.");
Console.WriteLine($"PASS: {assertions} assertions; 10,000 frames / 144 particles: {elapsed.TotalMilliseconds:F1} ms, {allocatedBytes} allocated bytes.");
Console.WriteLine("Scope: production physics/storage only; GPU, WinUI events and visible animation require separate runtime verification.");
