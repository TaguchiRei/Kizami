using System;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 核の部位。壊れると体が倒れる。
    /// </summary>
    [Serializable]
    public sealed class CorePartRole : EnemyPartRole
    {
        public override void OnBroken(EnemyBody body)
        {
            body.Defeat();
        }
    }
}
