
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Numerics;

public class CharacterEntity
{
    public ulong Id {get;set;}
    public string Name {get;set;}
    public ulong ChunkId {get;set;}
    public Vector3 Position {get;set;}
    public uint Level{get;set;}
    public uint ExperiencePoints {get;set;}
    public uint HealthPoints {get;set;}
    public uint MaxHealthPoints {get;set;}
    public uint MagicPoints {get;set;}
    public uint MaxMagicPoints {get;set;}
    public CharacterStats Stats {get;set;}
    public CharacterEquipSlots EquipSlots {get;set;}
    public List<ActiveStatusEffect> ActiveStatusEffects{get;set;}

}