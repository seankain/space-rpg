using Godot;
using System;
using System.Linq;

// Autoload (registered in project.godot). Owns the field <-> battle mode
// switch: pauses the scene tree, hides the running level, spawns a
// BattleScene (arena themed by the current world area) far above the field,
// and swaps the camera. The static StartBattle entry point is what dialogue
// (BattleNpc) calls.
//
// Victory hands control straight back to the field with battle results
// written onto the party. Defeat is a game over: the level is torn down and
// the player is sent to the Load Game menu to restore a previous save.
public partial class BattleManager : Node
{
    public static BattleManager Instance { get; private set; }

    public static bool BattleInProgress => Instance?.currentBattle != null;

    // Keeps the battle (and its UI) running while the paused tree freezes
    // the field underneath it.
    private static readonly Vector3 ArenaOffset = new(0, 300, 0);

    private BattleScene currentBattle;
    private Camera3D fieldCamera;
    private string pendingOpponentId;
    private string pendingOpponentName;
    private Action pendingOnVictory;
    private Action onVictory;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    // Entry point for dialogue choices. Deferred so the conversation can
    // finish closing (it recaptures the mouse as it ends) before battle mode
    // takes over the tree. opponentId keys the EnemyCatalog encounter
    // (Npc.NpcId); opponentName only labels the generic fallback fight.
    // onVictory lets the challenger react to losing (e.g. despawn); it is
    // not called on defeat.
    public static void StartBattle(string opponentId, string opponentName, Action onVictory = null)
    {
        if (Instance == null)
        {
            GD.PushWarning("BattleManager.StartBattle called before the autoload is ready.");
            return;
        }
        if (BattleInProgress || Instance.pendingOpponentId != null)
        {
            GD.PushWarning($"Ignoring StartBattle({opponentId}): a battle is already running.");
            return;
        }
        Instance.pendingOpponentId = opponentId;
        Instance.pendingOpponentName = opponentName;
        Instance.pendingOnVictory = onVictory;
        Callable.From(Instance.BeginPendingBattle).CallDeferred();
    }

    private void BeginPendingBattle()
    {
        var opponentId = pendingOpponentId;
        var opponentName = pendingOpponentName;
        onVictory = pendingOnVictory;
        pendingOpponentId = null;
        pendingOpponentName = null;
        pendingOnVictory = null;

        var state = SaveManager.Instance?.CurrentState;
        if (state == null || state.Party.Count == 0)
        {
            GD.PushWarning($"Cannot start battle against {opponentName}: no party in the current game state.");
            return;
        }

        var encounter = EnemyCatalog.GetEncounter(opponentId, opponentName);
        var partyCombatants = state.Party.Select(BattleCombatant.FromPartyMember).ToList();
        var theme = BattleArenaTheme.GetForLocation(state.LocationName);

        // Freeze the field. The level keeps its state (NPCs, pickups, player
        // transform) and is simply hidden until the battle resolves.
        GetTree().Paused = true;
        SetLevelVisible(false);
        fieldCamera = GetViewport().GetCamera3D();

        currentBattle = new BattleScene { Position = ArenaOffset };
        currentBattle.Setup(encounter, partyCombatants, theme);
        AddChild(currentBattle);
        currentBattle.BattleCamera.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    // Called by BattleScene when every enemy is down and results have been
    // written back to the party.
    public void FinishVictory()
    {
        TearDownBattle();
        SetLevelVisible(true);
        GetTree().Paused = false;
        if (fieldCamera != null && IsInstanceValid(fieldCamera))
        {
            fieldCamera.MakeCurrent();
        }
        fieldCamera = null;
        Input.MouseMode = Input.MouseModeEnum.Captured;

        var victoryCallback = onVictory;
        onVictory = null;
        victoryCallback?.Invoke();
    }

    // Called by the game-over panel's "Load a Save" button. The run is over:
    // drop the dead level and put the player in front of the Load Game menu.
    public void ResolveGameOver()
    {
        TearDownBattle();
        onVictory = null;
        fieldCamera = null;
        GetTree().Paused = false;
        LevelManager.Instance?.ShowGameOverLoadMenu();
    }

    private void TearDownBattle()
    {
        if (currentBattle != null)
        {
            currentBattle.QueueFree();
            currentBattle = null;
        }
    }

    private static void SetLevelVisible(bool visible)
    {
        if (LevelManager.Instance?.LevelRoot is { } levelRoot)
        {
            levelRoot.Visible = visible;
        }
    }
}
