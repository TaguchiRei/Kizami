using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ボスの武装の、種類ごとの数と、壊せる武装が残っているかを保持するステート。
    /// 武装の設定と壊れ具合から EngineAdapterLayer（BossBattleAdapter）が書き込み、ApplicationLayer（BossBattleService）が読んで、行えない行動を飛ばす。
    /// </summary>
    [RegisterBoard(typeof(EnemyBoard))]
    public sealed class BossWeaponState : SceneStateBase, IBossWeaponState
    {
        public int OutriggerCount { get; private set; }
        public int LaserCount { get; private set; }
        public int MissileCount { get; private set; }
        public bool HasBreakable { get; private set; }

        public override string GetLog()
        {
            return $"Outrigger: {OutriggerCount}  \nLaser: {LaserCount}  \nMissile: {MissileCount}  \nHasBreakable: {HasBreakable}";
        }

        /// <summary>
        /// 種類ごとの武装の数を設定する。壊れた武装も数える。
        /// </summary>
        /// <param name="outriggerCount">アウトリガーの数</param>
        /// <param name="laserCount">下部レーザーの数</param>
        /// <param name="missileCount">降下フィニッシャーを呼ぶ武装の数</param>
        public void SetCounts(int outriggerCount, int laserCount, int missileCount)
        {
            OutriggerCount = outriggerCount;
            LaserCount = laserCount;
            MissileCount = missileCount;
        }

        /// <summary>
        /// 壊せる武装が残っているかを設定する。
        /// </summary>
        /// <param name="hasBreakable">壊れていない武装が 1 つ以上あるか</param>
        public void SetHasBreakable(bool hasBreakable)
        {
            HasBreakable = hasBreakable;
        }
    }

    /// <summary>
    /// ボスの武装の読み取り面。
    /// </summary>
    public interface IBossWeaponState : IStateGetter
    {
        /// <summary> アウトリガーの数。壊れたものも数える。0 なら足踏みしない </summary>
        int OutriggerCount { get; }

        /// <summary> 下部レーザーの数。壊れたものも数える。0 ならレーザーを撃たない </summary>
        int LaserCount { get; }

        /// <summary> 降下フィニッシャーを呼ぶ武装の数。壊れたものも数える。0 なら降下フィニッシャーを出さない </summary>
        int MissileCount { get; }

        /// <summary> 壊れていない武装が 1 つ以上あるか。なければダウンで武装を壊さずに進む </summary>
        bool HasBreakable { get; }
    }
}
