using System;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の部位の役割。部位が壊れたとき（切られたとき、または体から外れて落ちたとき）の処理を、役割ごとに持つ。
    /// EnemyPart に [SerializeReference] で持たせ、Inspector の SubclassSelector で選ぶ。
    /// </summary>
    [Serializable]
    public abstract class EnemyPartRole
    {
        /// <summary>
        /// 部位が初めて壊れたときに 1 回だけ呼ばれる。体を倒すと、同じ切断の中で残りの部位の処理は呼ばれない。
        /// </summary>
        /// <param name="body">部位を持つ体</param>
        public abstract void OnBroken(EnemyBody body);
    }
}
