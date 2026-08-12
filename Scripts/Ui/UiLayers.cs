// Draw order for every UI CanvasLayer in the game, stated once. Previously each
// node carried its own `Layer = N; // above X, below Y` comment, which is how
// EventToasts and BattleHud both ended up on 60 with their relative order left
// to tree order.
//
// The menus are not listed: the main menu and the in-game menu are plain
// Controls drawing in scene order inside Main.tscn, and the shop rides its
// level's own CanvasLayer.
public static class UiLayers
{
    // The tracked quest's objective line, drawn straight over the world and
    // under everything else — it is the thing a conversation or a menu covers.
    public const int QuestHud = 40;

    // The conversation box, under everything that can be raised over it.
    public const int Dialogue = 50;

    // Corner notifications: over the dialogue box, under the battle HUD so a
    // victory toast can't sit on the action buttons.
    public const int Toasts = 60;

    public const int BattleHud = 65;

    // Editor tooling, in the order the author stacks it.
    public const int EditorToolbar = 80;
    public const int EditorPanel = 90;
    public const int Console = 100;
}
