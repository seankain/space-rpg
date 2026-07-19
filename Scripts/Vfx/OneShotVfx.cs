using Godot;

// Root of a fire-and-forget particle effect: starts every GpuParticles3D
// child once it enters the tree and frees itself after they have all
// finished, so callers never manage effect lifetime. VfxLibrary builds its
// code-made effects on this root; a hand-authored effect scene can attach
// it as the root script and get the same behavior.
public partial class OneShotVfx : Node3D
{
    private int running;

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is not GpuParticles3D emitter)
            {
                continue;
            }
            running++;
            emitter.OneShot = true;
            emitter.Finished += OnEmitterFinished;
            emitter.Emitting = true;
        }
        if (running == 0)
        {
            QueueFree();
        }
    }

    private void OnEmitterFinished()
    {
        running--;
        if (running == 0)
        {
            QueueFree();
        }
    }
}
