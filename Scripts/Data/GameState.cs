using System;
using System.Collections.Generic;
using System.Numerics;

public class GameState
{
    public string CurrentLevelPath {get;set;}
    public string LocationName {get;set;}
    public Guid LocationId {get;set;}
    // Null until first captured; a fresh game spawns at the level's Spawn marker instead.
    public Vector3? PlayerPosition {get;set;}
    public Vector3? PlayerRotation {get;set;}
    public List<CharacterEntity> Party{get;set;} = new();
    // Shared party inventory; equipment stays per-character on EquipSlots.
    public Inventory Inventory {get;set;} = new();
}
