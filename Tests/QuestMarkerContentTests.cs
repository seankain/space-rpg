using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

// Guards the half of a marker's target the engine-free catalogs can't check
// (quest-markers.md Phase 2): an npc: target names an NPC that must actually
// exist, and NpcDatabase — the registry that would know — is Godot-facing.
//
// So this reads the committed .tres definitions off disk the way
// WorldMapBakeFreshnessTests reads chunk scenes. A renamed or deleted NPC id
// otherwise costs nothing at build time and shows up as a quest marker that
// silently never appears.
public class QuestMarkerContentTests
{
    [Fact]
    public void EveryNpcMarkerTargetNamesADefinitionOnDisk()
    {
        var npcIds = LoadNpcIds();
        if (npcIds == null)
        {
            // Can't locate the project (unusual run layout); nothing to check.
            return;
        }

        var problems = new List<string>();
        foreach (var quest in QuestCatalog.All)
        {
            foreach (var marker in quest.Markers)
            {
                if (marker.Target.Kind == QuestMarkerTargetKind.Npc
                    && !npcIds.Contains(marker.Target.Reference))
                {
                    problems.Add($"quest {quest.Id} ({quest.Title}) marker '{marker.Label}' "
                        + $"targets unknown NPC '{marker.Target.Reference}'");
                }
            }
        }

        Assert.True(problems.Count == 0,
            "Quest markers name NPCs that have no definition under Resources/Npcs:\n  "
            + string.Join("\n  ", problems));
    }

    private static HashSet<string> LoadNpcIds()
    {
        var repoRoot = FindRepoRoot();
        var npcRoot = repoRoot == null ? null : Path.Combine(repoRoot, "Resources", "Npcs");
        if (npcRoot == null || !Directory.Exists(npcRoot))
        {
            return null;
        }
        var ids = new HashSet<string>();
        foreach (var path in Directory.GetFiles(npcRoot, "*.tres", SearchOption.AllDirectories))
        {
            // NpcId is a plain exported string in the .tres text:
            //   NpcId = "intro.vex"
            var match = Regex.Match(File.ReadAllText(path), "NpcId\\s*=\\s*\"([^\"]*)\"");
            if (match.Success)
            {
                ids.Add(match.Groups[1].Value);
            }
        }
        return ids;
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
