using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// One running battle: builds the generic arena (flat ground plane under a
// themed skybox, placeholder capsules for combatants), then drives the turn
// loop. Constructed and torn down by BattleManager; everything here is
// built in code so battles need no hand-authored scene yet.
//
// Turn structure: rounds repeat until one side is wiped. Each round every
// living combatant acts once, ordered by Dexterity (party wins ties). Party
// turns await the BattleHud menus; enemy turns run a simple AI.
public partial class BattleScene : Node3D
{
    private const float LineSpacing = 2.2f;
    private const float SideOffset = 3.5f;

    private BattleEncounter encounter;
    private List<BattleCombatant> party;
    private List<BattleCombatant> enemies;
    private BattleArenaTheme theme;

    private BattleHud hud;
    private readonly Dictionary<BattleCombatant, Node3D> visuals = new();
    private readonly Dictionary<BattleCombatant, Label3D> labels = new();
    private readonly Random random = new();

    public Camera3D BattleCamera { get; private set; }

    // Must be called before the node enters the tree.
    public void Setup(BattleEncounter encounter, List<BattleCombatant> party, BattleArenaTheme theme)
    {
        this.encounter = encounter;
        this.party = party;
        this.theme = theme;
        enemies = encounter.Enemies.Select(BattleCombatant.FromEnemyDefinition).ToList();
        DisambiguateNames(enemies);
    }

    public override void _Ready()
    {
        BuildArena();
        SpawnCombatants();

        hud = new BattleHud();
        AddChild(hud);
        hud.UpdateParty(party);

        _ = RunBattle();
    }

    private async Task RunBattle()
    {
        try
        {
            hud.ShowMessage(encounter.IntroMessage);
            await Delay(1.4);

            while (true)
            {
                foreach (var actor in TurnOrder())
                {
                    // May have fallen earlier this round.
                    if (actor.IsDown)
                    {
                        continue;
                    }
                    var action = actor.IsPartyMember
                        ? await hud.PromptAction(actor, enemies, party, PartyInventory)
                        : ChooseEnemyAction(actor);
                    await Execute(action);

                    if (enemies.All(e => e.IsDown))
                    {
                        await OnVictory();
                        return;
                    }
                    if (party.All(p => p.IsDown))
                    {
                        OnDefeat();
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError($"Battle loop failed: {e}");
        }
    }

    // All living combatants, fastest first; party members win Dexterity ties.
    private List<BattleCombatant> TurnOrder() =>
        party.Concat(enemies)
            .Where(c => !c.IsDown)
            .OrderByDescending(c => c.Stats.Dexterity)
            .ThenByDescending(c => c.IsPartyMember)
            .ToList();

    private static Inventory PartyInventory =>
        SaveManager.Instance?.CurrentState?.Inventory ?? new Inventory();

    private BattleAction ChooseEnemyAction(BattleCombatant actor)
    {
        var damagePower = actor.Powers.FirstOrDefault(p =>
            p.Effect == PowerEffect.Damage && p.PowerPointCost <= actor.PowerPoints);
        if (damagePower != null && random.NextDouble() < 0.35)
        {
            return new BattleAction
            {
                Kind = BattleActionKind.Power,
                Actor = actor,
                Power = damagePower,
                Target = RandomLiving(party),
            };
        }
        return new BattleAction
        {
            Kind = BattleActionKind.Attack,
            Actor = actor,
            Target = RandomLiving(party),
        };
    }

    private BattleCombatant RandomLiving(List<BattleCombatant> side)
    {
        var living = side.Where(c => !c.IsDown).ToList();
        return living[random.Next(living.Count)];
    }

    private async Task Execute(BattleAction action)
    {
        switch (action.Kind)
        {
            case BattleActionKind.Attack:
            {
                var damage = AttackDamage(action.Actor, action.Target);
                action.Target.TakeDamage(damage);
                Announce($"{action.Actor.Name} attacks {action.Target.Name} for {damage} damage.", action.Target);
                break;
            }
            case BattleActionKind.Power when action.Power.Effect == PowerEffect.Damage:
            {
                action.Actor.PowerPoints -= action.Power.PowerPointCost;
                var damage = PowerDamage(action.Actor, action.Power, action.Target);
                action.Target.TakeDamage(damage);
                Announce($"{action.Actor.Name} unleashes {action.Power.Name} on {action.Target.Name} for {damage} damage.", action.Target);
                break;
            }
            case BattleActionKind.Power:
            {
                action.Actor.PowerPoints -= action.Power.PowerPointCost;
                var healed = HealAmount(action.Actor, action.Power);
                action.Target.Heal(healed);
                Announce($"{action.Actor.Name}'s {action.Power.Name} restores {healed} HP to {action.Target.Name}.", action.Target);
                break;
            }
            case BattleActionKind.Item:
            {
                var item = ItemCatalog.Get(action.ItemId) as ConsumableItem;
                if (item == null || !PartyInventory.Remove(action.ItemId))
                {
                    Announce($"{action.Actor.Name} fumbles for an item that isn't there.", action.Target);
                    break;
                }
                action.Target.Heal(item.HealAmount);
                Announce($"{action.Actor.Name} uses a {item.Name} on {action.Target.Name}, restoring {item.HealAmount} HP.", action.Target);
                break;
            }
        }

        RefreshCombatantVisual(action.Actor);
        RefreshCombatantVisual(action.Target);
        hud.UpdateParty(party);
        await Delay(1.2);
    }

    private void Announce(string message, BattleCombatant target)
    {
        if (target != null && target.IsDown)
        {
            message += $" {target.Name} is down!";
        }
        hud.ShowMessage(message);
    }

    // Small-number melee math sized for ~10 HP pools: strength-driven attack
    // minus a light constitution mitigation, never below 1.
    private static uint AttackDamage(BattleCombatant actor, BattleCombatant target) =>
        (uint)Math.Max(1, 1 + (int)actor.Stats.Strength / 2 - (int)target.Stats.Constitution / 4);

    private static uint PowerDamage(BattleCombatant actor, Power power, BattleCombatant target) =>
        (uint)Math.Max(1, (int)power.Amount + (int)actor.Stats.Intelligence / 2 - (int)target.Stats.Constitution / 4);

    private static uint HealAmount(BattleCombatant actor, Power power) =>
        power.Amount + actor.Stats.Wisdom / 2;

    private async Task OnVictory()
    {
        var xp = (uint)enemies.Sum(e => e.XpReward);
        hud.ShowMessage($"Victory! The party gains {xp} XP.");

        // Persist the fight's outcome onto the party entities. Downed members
        // get back up at 1 HP — losing a fighter costs resources, not the run.
        foreach (var member in party)
        {
            member.Entity.HealthPoints = Math.Max(1, member.HealthPoints);
            member.Entity.MagicPoints = member.PowerPoints;
            member.Entity.ExperiencePoints += xp;
        }
        hud.UpdateParty(party);
        await Delay(2.0);
        BattleManager.Instance.FinishVictory();
    }

    private void OnDefeat()
    {
        hud.ShowMessage("The party has fallen...");
        hud.ShowGameOver(() => BattleManager.Instance.ResolveGameOver());
    }

    private async Task Delay(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // ----- Arena construction -----

    private void BuildArena()
    {
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = theme.GroundColor,
                Roughness = 0.9f,
            },
        };
        AddChild(ground);

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -30, 0),
            LightColor = theme.SunColor,
            LightEnergy = theme.SunEnergy,
            ShadowEnabled = true,
        };
        AddChild(sun);

        // The theme's sky rides on the battle camera as an environment
        // override, so the field level's WorldEnvironment (still in the tree,
        // just hidden) never fights it.
        BattleCamera = new Camera3D
        {
            Position = new Vector3(0, 4.5f, SideOffset + 5.5f),
            Environment = BuildEnvironment(),
        };
        AddChild(BattleCamera);
        BattleCamera.LookAt(GlobalPosition + new Vector3(0, 1, 0));
    }

    private Godot.Environment BuildEnvironment()
    {
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = theme.SkyTopColor,
            SkyHorizonColor = theme.SkyHorizonColor,
            GroundHorizonColor = theme.GroundHorizonColor,
            GroundBottomColor = theme.GroundBottomColor,
        };
        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMaterial },
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
    }

    private void SpawnCombatants()
    {
        // Party in a line facing -Z toward the enemy line; camera sits behind
        // the party. Enemy capsules keep their definition's tint.
        PlaceLine(party, SideOffset, i => new Color(0.3f, 0.55f, 0.95f));
        PlaceLine(enemies, -SideOffset, i => encounter.Enemies[i].BodyColor);
    }

    private void PlaceLine(List<BattleCombatant> side, float z, Func<int, Color> colorFor)
    {
        for (var i = 0; i < side.Count; i++)
        {
            var combatant = side[i];
            var x = (i - (side.Count - 1) / 2f) * LineSpacing;
            var color = colorFor(i);

            var root = new Node3D { Position = new Vector3(x, 0, z) };
            AddChild(root);

            var mesh = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.6f },
                Position = new Vector3(0, 0.9f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
            };
            root.AddChild(mesh);

            var label = new Label3D
            {
                Position = new Vector3(0, 2.2f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0008f,
                FontSize = 36,
                OutlineSize = 8,
                OutlineModulate = new Color(0, 0, 0, 0.85f),
            };
            root.AddChild(label);

            visuals[combatant] = root;
            labels[combatant] = label;
            RefreshCombatantVisual(combatant);
        }
    }

    private void RefreshCombatantVisual(BattleCombatant combatant)
    {
        if (combatant == null || !labels.TryGetValue(combatant, out var label))
        {
            return;
        }
        label.Text = $"{combatant.Name}\nHP {combatant.HealthPoints}/{combatant.MaxHealthPoints}";

        // Downed fighters keel over instead of despawning so the lineup stays
        // readable; revived ones stand back up.
        var root = visuals[combatant];
        if (root.GetChildOrNull<MeshInstance3D>(0) is not { } mesh)
        {
            return;
        }
        if (combatant.IsDown)
        {
            mesh.RotationDegrees = new Vector3(90, 0, 0);
            mesh.Position = new Vector3(0, 0.4f, 0);
        }
        else
        {
            mesh.RotationDegrees = Vector3.Zero;
            mesh.Position = new Vector3(0, 0.9f, 0);
        }
    }

    // "Dock Drone" twice becomes "Dock Drone A" / "Dock Drone B" so targeting
    // menus and combat messages stay unambiguous.
    private static void DisambiguateNames(List<BattleCombatant> combatants)
    {
        foreach (var group in combatants.GroupBy(c => c.Name).Where(g => g.Count() > 1))
        {
            var suffix = 'A';
            foreach (var combatant in group)
            {
                combatant.Name = $"{combatant.Name} {suffix++}";
            }
        }
    }
}
