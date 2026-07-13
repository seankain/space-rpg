using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Autoload (registered in project.godot). Owns the running GameState and all
// save/load orchestration; menus and levels talk to this, never to disk.
public partial class SaveManager : Node
{
    public static SaveManager Instance { get; private set; }

    public GameState CurrentState { get; private set; }
    public bool GameInProgress => CurrentState != null;

    private SaveRepository repository;

    public override void _Ready()
    {
        Instance = this;
        repository = new SaveRepository(
            ProjectSettings.GlobalizePath("user://saves"),
            warning => GD.PushWarning(warning));
    }

    public GameState StartNewGame(CharacterCreationData creation)
    {
        CurrentState = new GameState
        {
            CurrentLevelPath = "res://Scenes/Levels/Intro.tscn",
            LocationName = "Intro Station",
            LocationId = Guid.NewGuid(),
            Party = new List<CharacterEntity>
            {
                new CharacterEntity
                {
                    Id = 1,
                    Name = string.IsNullOrWhiteSpace(creation?.Name) ? "Player" : creation.Name.Trim(),
                    Level = 1,
                    HealthPoints = 10,
                    MaxHealthPoints = 10,
                    MagicPoints = 5,
                    MaxMagicPoints = 5,
                    Stats = creation?.Stats ?? new CharacterStats(),
                    EquipSlots = new CharacterEquipSlots(),
                    ActiveStatusEffects = new List<ActiveStatusEffect>(),
                }
            },
        };
        return CurrentState;
    }

    public IReadOnlyList<SaveData> ListSaves() => repository.ListSaves();

    public SaveData CreateSave()
    {
        CaptureState();
        var existing = repository.ListSaves();
        var meta = new SaveData
        {
            SlotId = Guid.NewGuid(),
            SaveNumber = existing.Count == 0 ? 1 : existing.Max(s => s.SaveNumber) + 1,
            SaveCreationTime = DateTime.Now,
            SaveTime = DateTime.Now,
            SaveLocationFriendlyName = CurrentState.LocationName,
            SaveLocationId = CurrentState.LocationId,
        };
        repository.Save(meta, CurrentState);
        return meta;
    }

    public void OverwriteSave(SaveData meta)
    {
        CaptureState();
        meta.SaveVersion = SaveRepository.CurrentSaveVersion;
        meta.SaveTime = DateTime.Now;
        meta.SaveLocationFriendlyName = CurrentState.LocationName;
        meta.SaveLocationId = CurrentState.LocationId;
        repository.Save(meta, CurrentState);
    }

    public GameState LoadSave(SaveData meta)
    {
        CurrentState = repository.LoadState(meta.SlotId);
        return CurrentState;
    }

    public void DeleteSave(SaveData meta) => repository.Delete(meta.SlotId);

    // Refreshes CurrentState from the live scene. Menus guarantee a game is
    // running before any save entry point can be reached.
    public void CaptureState()
    {
        if (CurrentState == null)
        {
            throw new InvalidOperationException("Cannot capture state: no game is in progress.");
        }
        if (GetTree().GetFirstNodeInGroup(Player.GroupName) is Node3D player)
        {
            CurrentState.PlayerPosition = ToNumerics(player.GlobalPosition);
            CurrentState.PlayerRotation = ToNumerics(player.Rotation);
        }
        // Followers persist through each member's CharacterEntity.Position
        // (party plan Phase 2); Level.AddFollowers restores them.
        foreach (var node in GetTree().GetNodesInGroup(PartyMemberFollower.GroupName))
        {
            if (node is PartyMemberFollower follower)
            {
                var member = CurrentState.Party?.FirstOrDefault(m => m.Id == follower.MemberId);
                if (member != null)
                {
                    member.Position = ToNumerics(follower.GlobalPosition);
                }
            }
        }
    }

    public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    public static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
