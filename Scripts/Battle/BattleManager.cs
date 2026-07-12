using Godot;

// Stub seam for the turn-based combat task. Dialogue (BattleNpc) calls
// StartBattle; the real implementation will assemble the encounter from the
// party (GameState.Party) and the opponent, and swap to a battle scene.
public static class BattleManager
{
    public static void StartBattle(string opponentName)
    {
        GD.Print($"TODO: start turn-based battle against {opponentName} (combat system not yet implemented).");
    }
}
