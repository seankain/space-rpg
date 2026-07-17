using Godot;

// Spawns data-driven NPCs (docs/plans/npc-resource-files.md) into non-chunked
// scenes: drop one of these into an interior (e.g. ShopInterior.tscn) and
// every NpcDefinition naming that scene as its SpawnScenePath spawns on
// ready. Chunked levels don't use this node — ChunkManager.AddChunk calls
// Spawn directly so NPCs live and die with their chunk.
public partial class NpcSpawner : Node3D
{
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

	// Instantiates a definition's role scene, primes it with the definition
	// (before _Ready, so state checks like "already defeated" see it), and
	// parents it at the definition's local position and facing.
	public static Npc Spawn(NpcDefinition definition, Node parent)
	{
		if (definition.NpcScene == null)
		{
			GD.PushError($"NPC '{definition.NpcId}' has no NpcScene to instantiate.");
			return null;
		}
		var node = definition.NpcScene.Instantiate();
		if (node is not Npc npc)
		{
			GD.PushError($"NPC '{definition.NpcId}': NpcScene root is a {node.GetType().Name}, not an Npc.");
			node.Free();
			return null;
		}
		npc.Initialize(definition);
		npc.Position = definition.LocalPosition;
		npc.RotationDegrees = new Vector3(0f, definition.RotationDegreesY, 0f);
		parent.AddChild(npc);
		return npc;
	}
}
