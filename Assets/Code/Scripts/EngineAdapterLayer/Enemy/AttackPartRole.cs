using System;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 攻撃部位。壊れた部位のビットに入っている攻撃部位が 1 つでもあれば、その敵は撃たない（EnemyShooter が判定する）。
    /// 壊れたときに体に起きることはない。
    /// </summary>
    [Serializable]
    public sealed class AttackPartRole : EnemyPartRole
    {
        public override void OnBroken(EnemyBody body)
        {
        }
    }
}
