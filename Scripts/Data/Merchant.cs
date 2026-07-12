// A trading NPC's side of the shop ledger: their credits and stock, spent
// down and bought back as the player trades. Engine-free like the rest of
// Scripts/Data. Built fresh from the shopkeeper's exported scene properties,
// so stock and credits reset whenever the shop scene reloads; persisting
// merchant state into saves is future work.
public class Merchant
{
    public string Name {get;set;}
    public uint Credits {get;set;}
    public Inventory Stock {get;set;} = new();
}
