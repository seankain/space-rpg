using System;

public class SaveData
{
    public int SaveVersion {get;set;} = SaveRepository.CurrentSaveVersion;
    public Guid SlotId {get;set;}
    public uint SaveNumber {get;set;}
    public DateTime SaveCreationTime {get;set;}
    public DateTime SaveTime {get;set;}
    public string SaveLocationFriendlyName {get;set;}
    public Guid SaveLocationId {get;set;}
}
