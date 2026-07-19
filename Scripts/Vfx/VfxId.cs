// Ids for the visual effects VfxLibrary can spawn. Engine-free on purpose:
// data classes (Power today; items, equipment, world events later) tag
// themselves with a VfxId so presentation stays out of the data layer and
// the data sources remain compilable by the xunit test project.
//
// Same registry rule as ItemCatalog/PowerCatalog ids: values are permanent
// once shipped — never reuse them.
public enum VfxId
{
    None = 0,
    // Melee hit connecting: a quick spark burst at the target.
    MeleeImpact = 1,
    // Plasma Surge: a hot orange plasma detonation.
    PlasmaBurst = 2,
    // Nano Mend: green repair motes rising over the target.
    NanoMend = 3,
    // Consumable used on a combatant: a soft blue shimmer.
    ItemSparkle = 4,
}
