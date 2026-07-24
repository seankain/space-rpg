#if TOOLS
using Godot;

// File > Run entry point for the world-map bake (the other is the
// Project > Tools > Bake World Maps menu item added by MapBakerPlugin). Both
// call the same MapBakeJob; open this script in the editor's script editor
// and press Ctrl+Shift+X to bake every area.
[Tool]
public partial class MapBaker : EditorScript
{
    // async void because capture waits on rendered frames; the editor keeps
    // pumping frames while the coroutine is suspended.
    public override async void _Run() => await MapBakeJob.RunAsync();
}
#endif
