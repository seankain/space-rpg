using Godot;
using System;
using System.Collections.Generic;

// The game's visual effects, keyed by VfxId. Spawn attaches a one-shot
// effect to any Node3D — battle arena, field level, a prop — and the effect
// frees itself when done, so callers fire and forget. Works under a paused
// scene tree as long as the parent still processes (battles run that way).
//
// Effects are built in code for now, matching the code-built battle arena.
// Each registry entry is just a factory, so a packaged effect (e.g. the
// Brackeys demo VFX under ThirdParty/Brackeys, once those assets are
// committed) can replace a code-built one by registering
// scene.Instantiate<OneShotVfx>() instead.
public static class VfxLibrary
{
    public static OneShotVfx Spawn(VfxId id, Node3D parent, Vector3 localPosition = default)
    {
        if (id == VfxId.None || parent == null)
        {
            return null;
        }
        if (!builders.TryGetValue(id, out var build))
        {
            GD.PushWarning($"VfxLibrary has no effect registered for {id}.");
            return null;
        }
        var vfx = build();
        vfx.Position = localPosition;
        parent.AddChild(vfx);
        return vfx;
    }

    private static readonly Dictionary<VfxId, Func<OneShotVfx>> builders = new()
    {
        [VfxId.MeleeImpact] = BuildMeleeImpact,
        [VfxId.PlasmaBurst] = BuildPlasmaBurst,
        [VfxId.NanoMend] = BuildNanoMend,
        [VfxId.ItemSparkle] = BuildItemSparkle,
    };

    // A sharp radial spatter of white-gold sparks that fall away fast.
    private static OneShotVfx BuildMeleeImpact()
    {
        var vfx = new OneShotVfx();
        vfx.AddChild(Emitter(amount: 24, lifetime: 0.45f, explosiveness: 1f, quadSize: 0.1f,
            new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0),
                Spread = 180f,
                InitialVelocityMin = 2.5f,
                InitialVelocityMax = 4f,
                Gravity = new Vector3(0, -8, 0),
                ScaleMin = 0.5f,
                ScaleMax = 1f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.1f,
                ColorRamp = FadeRamp(new Color(1, 1, 1), new Color(1, 0.85f, 0.3f)),
            }));
        return vfx;
    }

    // Plasma Surge: a brief white-hot core flash inside a rising cloud of
    // orange-to-red embers.
    private static OneShotVfx BuildPlasmaBurst()
    {
        var vfx = new OneShotVfx();
        vfx.AddChild(Emitter(amount: 6, lifetime: 0.3f, explosiveness: 1f, quadSize: 0.5f,
            new ParticleProcessMaterial
            {
                Spread = 180f,
                InitialVelocityMin = 0.2f,
                InitialVelocityMax = 0.6f,
                Gravity = Vector3.Zero,
                ScaleMin = 0.8f,
                ScaleMax = 1.4f,
                ColorRamp = FadeRamp(new Color(1, 0.95f, 0.8f), new Color(1, 0.55f, 0.15f)),
            }));
        vfx.AddChild(Emitter(amount: 40, lifetime: 0.6f, explosiveness: 1f, quadSize: 0.12f,
            new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0),
                Spread = 180f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 5f,
                // Hot embers drift up instead of falling.
                Gravity = new Vector3(0, 2, 0),
                ScaleMin = 0.4f,
                ScaleMax = 1f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.2f,
                ColorRamp = FadeRamp(
                    new Color(1, 0.9f, 0.6f), new Color(1, 0.45f, 0.1f), new Color(0.9f, 0.15f, 0.05f)),
            }));
        return vfx;
    }

    // Nano Mend: green repair motes welling up around the body. Low
    // explosiveness staggers the spawns so the swarm trickles upward instead
    // of popping all at once.
    private static OneShotVfx BuildNanoMend()
    {
        var vfx = new OneShotVfx();
        vfx.AddChild(Emitter(amount: 36, lifetime: 1.1f, explosiveness: 0.25f, quadSize: 0.08f,
            new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0),
                Spread = 15f,
                InitialVelocityMin = 1f,
                InitialVelocityMax = 2f,
                Gravity = new Vector3(0, 0.5f, 0),
                ScaleMin = 0.6f,
                ScaleMax = 1f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.5f,
                ColorRamp = FadeRamp(new Color(0.6f, 1, 0.8f), new Color(0.15f, 0.85f, 0.5f)),
            }));
        return vfx;
    }

    // Consumable items: a gentle blue shimmer drifting off the target.
    private static OneShotVfx BuildItemSparkle()
    {
        var vfx = new OneShotVfx();
        vfx.AddChild(Emitter(amount: 16, lifetime: 0.9f, explosiveness: 0.5f, quadSize: 0.1f,
            new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0),
                Spread = 30f,
                InitialVelocityMin = 0.8f,
                InitialVelocityMax = 1.5f,
                Gravity = Vector3.Zero,
                ScaleMin = 0.5f,
                ScaleMax = 1f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.4f,
                ColorRamp = FadeRamp(new Color(0.8f, 0.92f, 1), new Color(0.35f, 0.6f, 1)),
            }));
        return vfx;
    }

    private static GpuParticles3D Emitter(int amount, float lifetime, float explosiveness,
        float quadSize, ParticleProcessMaterial process) => new()
    {
        Amount = amount,
        Lifetime = lifetime,
        Explosiveness = explosiveness,
        OneShot = true,
        // OneShotVfx starts the emitter once it's in the tree.
        Emitting = false,
        ProcessMaterial = process,
        DrawPass1 = new QuadMesh
        {
            Size = new Vector2(quadSize, quadSize),
            Material = ParticleMaterial(),
        },
    };

    // Unshaded additive billboards tinted by the process material's color
    // ramp — reads as glowing energy under any arena lighting.
    private static StandardMaterial3D ParticleMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
    };

    // A lifetime color ramp through the given colors that always ends fully
    // transparent, so every effect fades out instead of blinking off.
    private static GradientTexture1D FadeRamp(params Color[] colors)
    {
        var offsets = new float[colors.Length + 1];
        var points = new Color[colors.Length + 1];
        for (var i = 0; i < colors.Length; i++)
        {
            offsets[i] = i / (float)colors.Length;
            points[i] = colors[i];
        }
        offsets[colors.Length] = 1f;
        points[colors.Length] = colors[^1] with { A = 0 };
        return new GradientTexture1D
        {
            Gradient = new Gradient { Offsets = offsets, Colors = points },
        };
    }
}
