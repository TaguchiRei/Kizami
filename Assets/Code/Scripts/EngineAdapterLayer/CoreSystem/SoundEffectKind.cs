namespace Kizami.EngineAdapter
{
    /// <summary>
    /// SoundPlayer が鳴らす効果音の種類。値の並びは SoundEffectList.md の分類（プレイヤー・敵・ボス・ステージ）に合わせる。
    /// </summary>
    public enum SoundEffectKind
    {
        GlassBreak,
        ArmorCut,
        VoxelHit,
        VoxelCollapse,
        BossWarning,

        PlayerSkill,
        PlayerSkillRush,
        PlayerSkillUtenkougou,
        PlayerSwing,
        PlayerCutHit,
        PlayerJump,
        PlayerLand,
        PlayerWallLatch,
        PlayerFootstep,
        PlayerDamage,
        OrbAbsorb,
        SlowModeStart,
        SlowModeEnd,

        EnemyShoot,
        EnemyBulletHitPlayer,
        EnemyBulletHitWall,
        FinisherAbsorb,
        FinisherBeamWarning,
        FinisherBeam,
        FinisherBeamHit,
        BarrierRaise,
        BarrierRelease,
        BarrierBreak,
        ArmorBreak,
        EnemyDefeat,
        EnemyCollapseBlast,
        EnemyFootstep,

        BossArrival,
        BossLanding,
        BossStomp,
        BossLaser,
        BossHeadAbsorb,
        BossHeadWarning,
        BossHeadBeam,
        BossTurretShot,
        BossFinisherStrike,
        BossTowerThrow,
        BossTowerImpact,
        BossCrystalThrow,
        BossCrystalBreak,
        BossWeaponBreak,
        BossPhaseAdvance,
        BossStopped,

        StageClear,
        StageFail,
        UiConfirm
    }
}
