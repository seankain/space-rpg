using Godot;

// Spawns data-driven NPCs (docs/plans/npc-resource-files.md) into non-chunked
// scenes: drop one of these into an interior (e.g. ShopInterior.tscn) and
// every NpcDefinition naming that scene as its SpawnScenePath spawns on
// ready. Chunked levels don't use this node — ChunkManager.AddChunk calls
// Spawn directly so NPCs live and die with their chunk.
public partial class NpcSpawner : Node3D
{
	private const string NpcScenePath = "res://Scenes/Npc.tscn";

	public override void _Ready()
	{
		var root = Owner ?? GetParent();
		if (root == null || string.IsNullOrEmpty(root.SceneFilePath))
		{
			GD.PushWarning("NpcSpawner cannot identify its hosting scene; no NPCs spawned.");
			return;
		}
		// This _Ready fires while the hosting scene is still inside its own
		// parent's AddChild (LoadingScreen adds the fully built level), and
		// adding children to a node busy setting up its children fails —
		// spawn at the end of the frame instead.
		Callable.From(() =>
		{
			if (!IsInstanceValid(root))
			{
				return;
			}
			foreach (var definition in NpcDatabase.ForScene(root.SceneFilePath))
			{
				Spawn(definition, root);
			}
		}).CallDeferred();
	}

	// Instantiates the shared Npc scene for a definition — unless one of its
	// roles vetoes spawning (already recruited, defeated-and-stays-down) —
	// primes it with the definition (before _Ready, so role runtime state
	// can rely on it), and parents it at the definition's local position and
	// facing.
	public static Npc Spawn(NpcDefinition definition, Node parent)
	{
		var state = SaveManager.Instance?.CurrentState;
		foreach (var role in definition.Roles ?? System.Array.Empty<NpcRole>())
		{
			if (role != null && !role.ShouldSpawn(definition, state))
			{
				return null;
			}
		}
		var npc = GD.Load<PackedScene>(NpcScenePath).Instantiate<Npc>();
		npc.Initialize(definition);
		npc.Position = definition.LocalPosition;
		npc.RotationDegrees = new Vector3(0f, definition.RotationDegreesY, 0f);
		parent.AddChild(npc);
		return npc;
	}
}
