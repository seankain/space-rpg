public enum STATUSEFFECT
{
    Poison,
    Asleep,
    Confused
}

public class ActiveStatusEffect
{
    public float SecondsRemaining {get;set;}
    public STATUSEFFECT EffectType {get;set;}
}