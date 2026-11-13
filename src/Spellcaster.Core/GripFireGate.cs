namespace Spellcaster.Core;

/// <summary>Snapshot of vanilla attack/cast restrictions, independent of Unity.</summary>
public struct GripFireGate
{
    public bool Gameplay, CanInput;
    public bool Paused, Attacking, Dashing, Dead, HazardDeath, HazardRespawning;
    public bool ControlRelinquished, NoInput, HardLanding, DashLanding, BackDashing;
    public bool Recoiling, RecoilingLeft, RecoilingRight, RecoilFrozen, Transitioning;
    public bool Focusing, AtBench, InAcid;
    public double AttackCooldown, DialogueEndTimer;

    // CanAttack's stricter !attacking subsumes CanCast's attack recovery test.
    // Wall sliding is deliberately absent, as it is in vanilla CanAttack.
    public bool Allowed => Gameplay && CanInput && !Paused && !Attacking && !Dashing &&
        !Dead && !HazardDeath && !HazardRespawning && !ControlRelinquished && !NoInput &&
        !HardLanding && !DashLanding && !BackDashing && !Recoiling && !RecoilingLeft &&
        !RecoilingRight && !RecoilFrozen && !Transitioning && !Focusing && !AtBench &&
        !InAcid && AttackCooldown <= 0 && DialogueEndTimer <= 0;
}
