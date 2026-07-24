#if TOOLS
using Godot;

// Editor integration for the world-map bake (see docs/plans/world-map.md):
//   - a Project > Tools > Bake World Maps menu item that re-bakes everything;
//   - auto-rebake of just the affected area when a chunk scene is saved, so
//     the author loop (edit chunk -> save -> check map) needs zero clicks.
// Both paths run the shared MapBakeJob. Enabled via addons/map_baker/plugin.cfg.
[Tool]
public partial class MapBakerPlugin : EditorPlugin
{
    private const string MenuItem = "Bake World Maps";

    // Guards against overlapping bakes: a bake writes no scenes, so it can't
    // retrigger scene_saved, but a manual bake and a save-triggered one could
    // otherwise race for the single capture viewport.
    private bool baking;

    public override void _EnterTree()
    {
        AddToolMenuItem(MenuItem, Callable.From(OnBakeAllRequested));
        SceneSaved += OnSceneSaved;
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(MenuItem);
        SceneSaved -= OnSceneSaved;
    }

    private async void OnBakeAllRequested()
    {
        if (baking)
        {
            return;
        }
        baking = true;
        try
        {
            await MapBakeJob.RunAsync();
        }
        finally
        {
            baking = false;
        }
    }

    private async void OnSceneSaved(string filepath)
    {
        var area = AreaOfChunkScene(filepath);
        if (area == null || baking)
        {
            return;
        }
        baking = true;
        try
        {
            await MapBakeJob.BakeAreaAsync(area);
        }
        finally
        {
            baking = false;
        }
    }

    // The area directory name for a saved scene path, or null if the saved
    // scene isn't a chunk under Scenes/Levels/Chunks/<Area>/Chunk_<x>_<z>.tscn.
    private static string AreaOfChunkScene(string scenePath)
    {
        var prefix = $"{MapBakeJob.ChunksRoot}/";
        if (!scenePath.StartsWith(prefix))
        {
            return null;
        }
        var relative = scenePath.Substring(prefix.Length);
        var slash = relative.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }
        var file = relative.Substring(slash + 1);
        if (!file.StartsWith("Chunk_") || !file.EndsWith(".tscn"))
        {
            return null;
        }
        return relative.Substring(0, slash);
    }
}
#endif
