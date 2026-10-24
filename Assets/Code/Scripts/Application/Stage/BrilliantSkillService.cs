using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.External;
using UnityEngine;

namespace Kizami.Application
{
    /// <summary>
    /// プレイヤーのよい行動（連続キル・倒壊の巻き込み・一撃の破壊）を判定し、成立したら追加スコアを渡して通知するユースケース。
    /// 複数の種類が同時に成立したときは、種類ごとに成立させる。
    /// 投げた言葉の着弾では、ボクセルを削って範囲の中の敵を倒す。着弾の結果で別の言葉が出て、それをまた投げて追加スコアを重ねられないよう、着弾で倒した敵と壊したボクセルは判定に数えない。ボスでの撃破も数えない。
    /// 時間はゲーム内の時間（Time.time）で数える。倒れる建物の物理と敵の動きが同じ時間で進む為で、スローモード中は実時間では長く、ポーズ中は進まない。
    /// </summary>
    public sealed class BrilliantSkillService
    {
        private readonly BrilliantSkillSettings _settings;
        private readonly Action<int> _addBonusScore;
        private readonly Action<BrilliantSkillKind> _onTriggered;
        private readonly Action<Vector3, float> _carveExplosion;
        private readonly Action<Vector3, float> _defeatInRadius;
        private readonly List<CollapseWindow> _collapseWindows = new();

        private float _lastDefeatTime = float.NegativeInfinity;
        private int _streakCount;
        private int _nextStreakThreshold;

        /// <summary> 投げた言葉の着弾を処理している間は true </summary>
        private bool _isResolvingWordImpact;

        /// <param name="settings">判定の条件と追加スコア</param>
        /// <param name="addBonusScore">追加スコアを足す関数</param>
        /// <param name="onTriggered">成立した種類を通知する関数。同時に成立した種類の数だけ続けて呼ぶ</param>
        /// <param name="carveExplosion">中心と半径を受け取り、その球のボクセルを削る関数。なければ着弾で削らない</param>
        /// <param name="defeatInRadius">中心と半径を受け取り、その球の中の敵を倒す関数</param>
        public BrilliantSkillService(BrilliantSkillSettings settings, Action<int> addBonusScore,
            Action<BrilliantSkillKind> onTriggered, Action<Vector3, float> carveExplosion,
            Action<Vector3, float> defeatInRadius)
        {
            _settings = settings;
            _addBonusScore = addBonusScore;
            _onTriggered = onTriggered;
            _carveExplosion = carveExplosion;
            _defeatInRadius = defeatInRadius;
            _nextStreakThreshold = settings.RampageDefeatCount;
        }

        /// <summary>
        /// 敵を倒したことを伝える。着弾とボスでの撃破は数えない。連続キルの数に足し、崩落で倒した敵は、倒れた建物の範囲にいれば倒壊の巻き込みの数にも足す。
        /// </summary>
        /// <param name="notice">倒した敵の通知</param>
        public void OnDefeated(EnemyDefeatNotice notice)
        {
            if (_isResolvingWordImpact || notice.Cause == EnemyDefeatCause.Boss) return;

            var now = Time.time;
            CountStreak(notice.Count, now);

            if (notice.Cause != EnemyDefeatCause.Collapse) return;

            CountCollapse(notice, now);
        }

        /// <summary>
        /// 建物が倒れ始めたことを伝える。この瞬間から決めた時間の間、範囲の中で崩落で倒した敵を数える。
        /// </summary>
        /// <param name="bounds">倒れた階のピースをすべて含む範囲（ワールド空間）。余白は含まない</param>
        public void OnBuildingCollapsed(Bounds bounds)
        {
            _collapseWindows.Add(new CollapseWindow(bounds.center, bounds.extents.magnitude + _settings.CollapseMargin,
                Time.time + _settings.CollapseWindow));
        }

        /// <summary>
        /// 1回の攻撃でボクセルを壊したことを伝える。着弾による破壊は数えない。
        /// </summary>
        /// <param name="volume">壊したボクセルの体積（m3）</param>
        public void OnVoxelsDestroyed(float volume)
        {
            if (!_isResolvingWordImpact && volume >= _settings.ShatterVolume) Trigger(BrilliantSkillKind.Shatter);
        }

        /// <summary>
        /// 投げた言葉がぶつかった点で、ボクセルを削り、範囲の中の敵を倒す。
        /// </summary>
        /// <param name="point">ぶつかった点（ワールド空間）</param>
        public void OnWordLanded(Vector3 point)
        {
            var radius = _settings.WordImpactRadius;
            _isResolvingWordImpact = true;
            try
            {
                _carveExplosion?.Invoke(point, radius);
                _defeatInRadius?.Invoke(point, radius);
            }
            finally
            {
                _isResolvingWordImpact = false;
            }
        }

        /// <summary>
        /// 続けて倒した数を数え、必要な数に届くたびに成立させる。間が空いたら数え直す。
        /// </summary>
        private void CountStreak(int count, float now)
        {
            if (now - _lastDefeatTime > _settings.RampageInterval)
            {
                _streakCount = 0;
                _nextStreakThreshold = _settings.RampageDefeatCount;
            }

            _lastDefeatTime = now;
            _streakCount += count;
            if (_streakCount < _nextStreakThreshold) return;

            _nextStreakThreshold = (_streakCount / _settings.RampageDefeatCount + 1) * _settings.RampageDefeatCount;
            Trigger(BrilliantSkillKind.Rampage);
        }

        /// <summary>
        /// 倒れた建物の範囲で崩落で倒した敵を、その建物の巻き込んだ数に足し、必要な数に届いたら成立させる。期限が切れた建物は忘れる。
        /// </summary>
        private void CountCollapse(EnemyDefeatNotice notice, float now)
        {
            for (var i = _collapseWindows.Count - 1; i >= 0; i--)
            {
                var window = _collapseWindows[i];
                if (now > window.ExpireTime)
                {
                    _collapseWindows.RemoveAt(i);
                    continue;
                }

                if ((notice.Center - window.Center).sqrMagnitude > window.Radius * window.Radius) continue;

                window.DefeatCount += notice.Count;
                if (!window.IsTriggered && window.DefeatCount >= _settings.CollapseDefeatCount)
                {
                    window.IsTriggered = true;
                    Trigger(BrilliantSkillKind.Collapse);
                }

                _collapseWindows[i] = window;
            }
        }

        private void Trigger(BrilliantSkillKind kind)
        {
            _addBonusScore?.Invoke(_settings.GetScore(kind));
            _onTriggered?.Invoke(kind);
        }

        /// <summary>
        /// 倒れ始めた建物 1 つぶんの、巻き込んだ敵を数える窓。
        /// </summary>
        private struct CollapseWindow
        {
            public readonly Vector3 Center;
            public readonly float Radius;
            public readonly float ExpireTime;
            public int DefeatCount;
            public bool IsTriggered;

            public CollapseWindow(Vector3 center, float radius, float expireTime)
            {
                Center = center;
                Radius = radius;
                ExpireTime = expireTime;
                DefeatCount = 0;
                IsTriggered = false;
            }
        }
    }
}
