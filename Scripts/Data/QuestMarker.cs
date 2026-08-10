using System.Globalization;

// Where a quest wants the player to go, authored on the quest definition
// (quest-markers.md Phase 1). A marker names a *target* rather than a position:
// positions are resolved at display time from whichever registry owns that
// target's truth — NPCs from NpcDatabase, world pickups from the map bake — the
// same source-of-truth split the world map already uses for its landmarks.
//
// Engine-free on purpose (no Godot types): the xunit project compiles this file
// directly, and the map UI is the only thing that needs the engine.
public enum QuestMarkerTargetKind
{
    // A named NPC, by NpcDefinition.NpcId ("intro.dockmaster_hale").
    Npc,

    // The world pickup for an ItemCatalog id — "the Maguffin Cube, wherever it
    // is lying". Once the item is in the party's pockets there is nothing to
    // point at, which is what the marker's VisibleWhen condition is for.
    Item,

    // A hand-authored world point in a named area, for objectives with nothing
    // standing at them ("reach the cargo bay").
    Point,
}

// A marker's target as a flat colon token — "npc:intro.vex", "item:1",
// "point:IntroStation:64:-12" — matching the TokenRef habit the dialogue
// vocabulary established, so quest definitions stay terse when the catalog
// moves from C# to JSON (quest-system.md Phase 1).
public class QuestMarkerTarget
{
    public const char Separator = ':';

    public QuestMarkerTargetKind Kind { get; set; }

    // The NPC id, the item id, or the area name, by Kind.
    public string Reference { get; set; } = "";

    // World position, Point targets only. The other kinds resolve their
    // position from a registry.
    public float X { get; set; }
    public float Z { get; set; }

    public static QuestMarkerTarget Npc(string npcId) => new()
    {
        Kind = QuestMarkerTargetKind.Npc,
        Reference = npcId ?? "",
    };

    public static QuestMarkerTarget Item(uint itemId) => new()
    {
        Kind = QuestMarkerTargetKind.Item,
        Reference = itemId.ToString(CultureInfo.InvariantCulture),
    };

    public static QuestMarkerTarget Point(string areaName, float x, float z) => new()
    {
        Kind = QuestMarkerTargetKind.Point,
        Reference = areaName ?? "",
        X = x,
        Z = z,
    };

    // The item id an Item target names. False for any other kind, or for a
    // reference that doesn't parse (Validate reports why).
    public bool TryItemId(out uint itemId)
    {
        itemId = 0;
        return Kind == QuestMarkerTargetKind.Item
            && uint.TryParse(Reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId);
    }

    public string ToToken() => Kind switch
    {
        QuestMarkerTargetKind.Point => string.Join(
            Separator,
            KindToken(Kind),
            Reference,
            X.ToString(CultureInfo.InvariantCulture),
            Z.ToString(CultureInfo.InvariantCulture)),
        _ => $"{KindToken(Kind)}{Separator}{Reference}",
    };

    public static QuestMarkerTarget Parse(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }
        var parts = token.Split(Separator);
        if (!TryParseKind(parts[0], out var kind) || parts.Length < 2)
        {
            return null;
        }
        var target = new QuestMarkerTarget { Kind = kind, Reference = parts[1] };
        if (kind != QuestMarkerTargetKind.Point)
        {
            return parts.Length == 2 ? target : null;
        }
        if (parts.Length != 4
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return null;
        }
        target.X = x;
        target.Z = z;
        return target;
    }

    // Static check run when a quest is registered: does this target name
    // something a locator could plausibly find? Returns null when valid, else
    // the reason. Whether the named NPC or pickup *exists* is checked against
    // the Godot-side registries (NpcDatabase, the map bake) in Phase 2 — this
    // layer only knows the engine-free catalogs.
    public static string Validate(QuestMarkerTarget target)
    {
        if (target == null)
        {
            return "marker has no target";
        }
        if (string.IsNullOrWhiteSpace(target.Reference))
        {
            return $"{KindToken(target.Kind)} target has an empty reference";
        }
        if (target.Reference.IndexOf(Separator) >= 0)
        {
            return $"{KindToken(target.Kind)} target reference '{target.Reference}' can't contain '{Separator}'";
        }
        if (target.Kind != QuestMarkerTargetKind.Item)
        {
            return null;
        }
        if (!target.TryItemId(out var itemId))
        {
            return $"item target: item id '{target.Reference}' is not a whole number";
        }
        return ItemCatalog.Get(itemId) == null ? $"item target: no item with id {itemId}" : null;
    }

    private static string KindToken(QuestMarkerTargetKind kind) => kind switch
    {
        QuestMarkerTargetKind.Npc => "npc",
        QuestMarkerTargetKind.Item => "item",
        _ => "point",
    };

    private static bool TryParseKind(string token, out QuestMarkerTargetKind kind)
    {
        switch (token)
        {
            case "npc":
                kind = QuestMarkerTargetKind.Npc;
                return true;
            case "item":
                kind = QuestMarkerTargetKind.Item;
                return true;
            case "point":
                kind = QuestMarkerTargetKind.Point;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

// One "go here" on a quest: what to point at, what to call it, and when it
// applies. There are no quest stages yet (quest-system.md Phase 1), so which
// markers are live is decided by a condition over GameState — the same fixed,
// tested vocabulary conversations gate their branches with. That is what lets
// the fetch quest move its marker from the cube to Hale the moment the cube is
// picked up, and it keeps working when stages land: a stage is one more verb.
//
// Marker conditions are evaluated with no speaking NPC and no scene host, so a
// verb that ever needs either fails closed (warns, hides the marker) exactly as
// it does in a conversation. Every verb in today's vocabulary reads GameState
// alone.
public class QuestMarker
{
    public QuestMarkerTarget Target { get; set; }

    // Short objective text: the map tooltip and the journal's objective line.
    public string Label { get; set; } = "";

    // Null means "always, while the quest is in progress".
    public ConditionRef VisibleWhen { get; set; }

    public static QuestMarker Create(QuestMarkerTarget target, string label, string visibleWhen = null) => new()
    {
        Target = target,
        Label = label,
        VisibleWhen = ConditionRef.Parse(visibleWhen),
    };

    // Static check for catalog registration: a usable target, something to call
    // it, and a condition the vocabulary can actually evaluate. Returns null
    // when valid, else the reason.
    public static string Validate(QuestMarker marker)
    {
        if (marker == null)
        {
            return "marker is null";
        }
        if (QuestMarkerTarget.Validate(marker.Target) is { } targetProblem)
        {
            return targetProblem;
        }
        if (string.IsNullOrWhiteSpace(marker.Label))
        {
            return $"marker on {marker.Target.ToToken()} has no label";
        }
        return DialogueConditions.Validate(marker.VisibleWhen) is { } conditionProblem
            ? $"marker on {marker.Target.ToToken()}: {conditionProblem}"
            : null;
    }
}
