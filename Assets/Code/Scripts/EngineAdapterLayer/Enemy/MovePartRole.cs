using System;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 移動部位。壊れた数が体の上限に達すると、体は移動しなくなる。
    /// </summary>
    [Serializable]
    public sealed class MovePartRole : EnemyPartRole
    {
        public override void OnBroken(EnemyBody body)
        {
            body.BreakMovePart();
        }
    }
}
