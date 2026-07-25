using System.Collections.Generic;
using System.IO;
using Xunit;

// Guards against silent world-map drift (world-map plan Phase 4): once an
// area's maps are baked and committed, every chunk scene must still match the
// image that was baked from it. An area with no committed bake yet is left
// alone — this catches drift, not "not baked yet" — so the suite stays green
// until the bake workflow is adopted, then keeps it honest.
//
// The check is filesystem-only (no engine), hashing chunk .tscn text the same
// way MapBakeJob did via the shared MapBakeManifest.
public class WorldMapBakeFreshnessTests
{
    [Fact]
    public void CommittedMapsMatchTheirChunkScenes()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            // Can't locate the project (unusual run layout); nothing to check.
            return;
        }
        var chunksRoot = Path.Combine(repoRoot, "Scenes", "Levels", "Chunks");
        if (!Directory.Exists(chunksRoot))
        {
            return;
        }

        var problems = new List<string>();
        foreach (var areaDir in Directory.GetDirectories(chunksRoot))
        {
            var areaName = Path.GetFileName(areaDir);
            var mapsAreaDir = Path.Combine(repoRoot, "Resources", "Maps", areaName);
            var manifestPath = Path.Combine(mapsAreaDir, "bake_manifest.json");
            if (!File.Exists(manifestPath))
            {
                // This area has never been baked and committed; not drift.
                continue;
            }
            CheckArea(areaName, areaDir, mapsAreaDir, manifestPath, problems);
        }

        Assert.True(problems.Count == 0,
            "World maps are stale — run Project > Tools > Bake World Maps and commit Resources/Maps/:\n  "
            + string.Join("\n  ", problems));
    }

    private static void CheckArea(string areaName, string areaDir, string mapsAreaDir, string manifestPath, List<string> problems)
    {
        var manifest = MapBakeManifest.FromJson(File.ReadAllText(manifestPath));
        var seen = new HashSet<string>();
        foreach (var scenePath in Directory.GetFiles(areaDir, "Chunk_*.tscn"))
        {
            var sceneFile = Path.GetFileName(scenePath);
            seen.Add(sceneFile);

            var pngName = sceneFile[..^".tscn".Length] + ".png";
            if (!File.Exists(Path.Combine(mapsAreaDir, pngName)))
            {
                problems.Add($"{areaName}/{pngName}: no baked image");
            }

            if (!manifest.ChunkHashes.TryGetValue(sceneFile, out var baked))
            {
                problems.Add($"{areaName}/{sceneFile}: not in bake manifest");
            }
            else if (MapBakeManifest.HashSceneText(File.ReadAllText(scenePath)) != baked)
            {
                problems.Add($"{areaName}/{sceneFile}: scene changed since it was baked");
            }
        }
        foreach (var recorded in manifest.ChunkHashes.Keys)
        {
            if (!seen.Contains(recorded))
            {
                problems.Add($"{areaName}/{recorded}: in bake manifest but the chunk scene is gone");
            }
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            {
                return dir.FullName;
            }
        }
        return null;
    }
}
