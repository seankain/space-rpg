using Godot;

// The tabbed in-game menu (Quests / Party / Inventory / Map / Log). Each tab is
// an independent script that refreshes itself off its own VisibilityChanged, so
// this owns nothing but the window itself — opening and closing, and the policy
// that goes with it.
//
// LevelManager used to set Visible on this node directly; it now asks
// UiWindowManager to open it, which is what keeps it from stacking with the
// pause menu and what freezes the player while it is up.
public partial class InGameMenu : Control, IUiWindow
{
	public string WindowName => "in-game menu";

	// Full-screen: nothing underneath it should show through.
	public bool Exclusive => true;

	public void SetShown(bool shown) => Visible = shown;

	public override void _Ready()
	{
		// Closed until something opens it. The stack owns visibility from here.
		Visible = false;
	}
}
