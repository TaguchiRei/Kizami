using UnityEngine;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 敵を倒した原因。
    /// </summary>
    public enum EnemyDefeatCause
    {
        /// <summary> 核を切って倒した </summary>
        Cut,
        /// <summary> 崩落で倒した。落ちてきたボクセルの塊や倒れた建物の階に潰された敵と、高い所から落ちた敵を含む </summary>
        Collapse
    }

    /// <summary>
    /// 同じ原因で同時に倒した敵の通知。切断は1回の振りごと、崩落は1フレームごとにまとめる。
    /// </summary>
    public readonly struct EnemyDefeatNotice
    {
        /// <summary> 倒した数 </summary>
        public int Count { get; }
        /// <summary> 倒した原因 </summary>
        public EnemyDefeatCause Cause { get; }
        /// <summary> 倒した敵の位置の平均（ワールド空間） </summary>
        public Vector3 Center { get; }

        /// <param name="count">倒した数</param>
        /// <param name="cause">倒した原因</param>
        /// <param name="center">倒した敵の位置の平均</param>
        public EnemyDefeatNotice(int count, EnemyDefeatCause cause, Vector3 center)
        {
            Count = count;
            Cause = cause;
            Center = center;
        }
    }
}
