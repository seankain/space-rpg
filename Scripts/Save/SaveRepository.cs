using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Pure serialization and disk IO for saved games; no Godot types so it can be
// exercised without booting the engine. SaveManager owns the Godot-facing side.
public class SaveRepository
{
    // v2: GameState gained Inventory (v1 saves migrate to an empty inventory).
    // v3: GameState gained Quests (older saves migrate to an empty quest log).
    // v4: CharacterEquipSlots stores equipped item ids instead of the old
    //     unused enum placeholders (nothing could equip before, so older
    //     saves just load with every slot empty).
    // v5: GameState gained party Credits and interior return-point fields
    //     (pre-v5 saves load with the new-game starting credits and no
    //     pending interior to return from — GameState's property defaults
    //     cover both, so there is no explicit migration step).
    // v6: GameState gained DefeatedNpcs (beaten battle challengers stay down
    //     across saves; pre-v6 saves load with nobody defeated).
    // v7: DefeatedNpcs entries are stable NpcDefinition.NpcId slugs instead
    //     of display names. The rewrite of legacy entries needs NpcDatabase
    //     (a Godot-side registry), so SaveManager performs it after
    //     LoadState rather than here.
    // v8: GameState gained Flags, the world-flag store conversations set and
    //     read (npc-dialogue-yarn.md Phase 3). Pre-v8 saves load with no flags
    //     — the property default covers it, so there is no migration step; an
    //     NPC simply greets a restored old save as if for the first time.
    public const int CurrentSaveVersion = 8;

    private const string MetaFileName = "meta.json";
    private const string StateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // System.Numerics.Vector3 exposes X/Y/Z as fields.
        IncludeFields = true,
    };

    private readonly string rootDirectory;
    private readonly Action<string> logWarning;

    public SaveRepository(string rootDirectory, Action<string> logWarning = null)
    {
        this.rootDirectory = rootDirectory;
        this.logWarning = logWarning ?? (_ => {});
    }

    public List<SaveData> ListSaves()
    {
        var saves = new List<SaveData>();
        if (!Directory.Exists(rootDirectory))
        {
            return saves;
        }
        foreach (var slotDirectory in Directory.EnumerateDirectories(rootDirectory))
        {
            var metaPath = Path.Combine(slotDirectory, MetaFileName);
            if (!File.Exists(metaPath))
            {
                continue;
            }
            try
            {
                var meta = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(metaPath), JsonOptions);
                if (meta == null || meta.SlotId == Guid.Empty)
                {
                    throw new InvalidDataException("save metadata is missing a slot id");
                }
                if (meta.SaveVersion > CurrentSaveVersion)
                {
                    logWarning($"Save '{slotDirectory}' is from a newer game version ({meta.SaveVersion}) and was skipped.");
                    continue;
                }
                saves.Add(meta);
            }
            catch (Exception e)
            {
                logWarning($"Skipping unreadable save '{slotDirectory}': {e.Message}");
            }
        }
        return saves.OrderByDescending(s => s.SaveTime).ToList();
    }

    public void Save(SaveData meta, GameState state)
    {
        var slotDirectory = SlotDirectory(meta.SlotId);
        Directory.CreateDirectory(slotDirectory);
        // State first: meta is what makes a slot show up in listings, so a listed
        // save always has a readable state file even if we crash between writes.
        WriteAtomic(Path.Combine(slotDirectory, StateFileName), JsonSerializer.Serialize(state, JsonOptions));
        WriteAtomic(Path.Combine(slotDirectory, MetaFileName), JsonSerializer.Serialize(meta, JsonOptions));
    }

    public GameState LoadState(Guid slotId)
    {
        var statePath = Path.Combine(SlotDirectory(slotId), StateFileName);
        var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(statePath), JsonOptions);
        if (state == null || string.IsNullOrEmpty(state.CurrentLevelPath))
        {
            throw new InvalidDataException($"Save state at '{statePath}' has no level to restore.");
        }
        // Pre-v2 saves (or hand-edited files) have no inventory block.
        state.Inventory ??= new Inventory();
        // Pre-v3 saves have no quest log.
        state.Quests ??= new List<QuestProgress>();
        // Pre-v6 saves have no defeated-NPC record.
        state.DefeatedNpcs ??= new List<string>();
        // Pre-v8 saves have no world flags.
        state.Flags ??= new Dictionary<string, string>();
        // Pre-v4 saves stored placeholder enum slots whose properties no
        // longer bind; make sure every member has an (empty) slot block.
        state.Party ??= new List<CharacterEntity>();
        foreach (var member in state.Party)
        {
            member.EquipSlots ??= new CharacterEquipSlots();
        }
        return state;
    }

    public void Delete(Guid slotId)
    {
        var slotDirectory = SlotDirectory(slotId);
        if (Directory.Exists(slotDirectory))
        {
            Directory.Delete(slotDirectory, recursive: true);
        }
    }

    private string SlotDirectory(Guid slotId) => Path.Combine(rootDirectory, $"slot_{slotId:N}");

    private static void WriteAtomic(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }
}
