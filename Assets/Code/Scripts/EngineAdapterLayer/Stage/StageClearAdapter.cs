using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージシーンの破壊対象（DestructionTarget・CoreSystem・TutorialDummy）を集め、破壊済みの数が変わるたびに、破壊対象の数と破壊済みの数を通知する Adapter。インゲームのシーンへ置く。
    /// クリアの判定は通知を受ける Application 側が行う。
    /// </summary>
    public sealed class StageClearAdapter : InitializableMonoBehaviour
    {
        private readonly List<IDestructionTarget> _targets = new();

        private Action<int, int> _onTargetsChanged;
        private int _destroyedCount = -1;

        /// <summary> ステージシーンにある破壊対象 </summary>
        public IReadOnlyList<IDestructionTarget> Targets => _targets;

        /// <summary>
        /// ステージシーンの破壊対象を集め、最初の数を通知する。ステージシーンを読み込んだあとに呼ぶ。
        /// </summary>
        /// <param name="onTargetsChanged">破壊済みの数が変わったときに呼ぶ処理。引数は破壊対象の数と破壊済みの数</param>
        /// <param name="targetGimmickIds">クリアの対象にするギミックの ID。末尾の * は前方一致。空なら、見つけた破壊対象をすべて対象にする</param>
        public void Initialize(Action<int, int> onTargetsChanged, IReadOnlyList<string> targetGimmickIds)
        {
            _onTargetsChanged = onTargetsChanged;
            _targets.Clear();
            _targets.AddRange(FindObjectsByType<DestructionTarget>(FindObjectsSortMode.None));
            _targets.AddRange(FindObjectsByType<VoxelBuildingDestructionTarget>(FindObjectsSortMode.None));
            _targets.AddRange(FindObjectsByType<CoreSystem>(FindObjectsSortMode.None));
            // チュートリアルのダミーは、台本が出すまで非アクティブで待つ
            _targets.AddRange(FindObjectsByType<TutorialDummy>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            if (targetGimmickIds.Count > 0) KeepGimmicks(targetGimmickIds);

            if (_targets.Count == 0)
            {
                UsefulLogger.LogWarning("ステージに破壊対象がない為、クリアになりません。", this);
            }

            _destroyedCount = -1;
            base.Initialize();
            CountDestroyed();
        }

        /// <summary>
        /// 破壊対象のうち、ID の一覧に当たるギミックだけを残す。ID に当たる破壊対象がなければ警告する。
        /// </summary>
        private void KeepGimmicks(IReadOnlyList<string> ids)
        {
            _targets.RemoveAll(target =>
                !(target is Component component && component.TryGetComponent<StageGimmick>(out var gimmick) &&
                  ContainsId(ids, gimmick.Id)));

            foreach (var id in ids)
            {
                var found = _targets.Exists(target => StageGimmick.Matches(id, ((Component)target).GetComponent<StageGimmick>().Id));
                if (!found) UsefulLogger.LogWarning($"ID '{id}' の破壊対象が見つかりません。", this);
            }
        }

        private static bool ContainsId(IReadOnlyList<string> ids, string id)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (StageGimmick.Matches(ids[i], id)) return true;
            }

            return false;
        }

        private void Update()
        {
            if (!Initialized) return;

            CountDestroyed();
        }

        /// <summary>
        /// 破壊済みの破壊対象を数え、前回から変わっていたら通知する。
        /// </summary>
        private void CountDestroyed()
        {
            var destroyedCount = 0;
            foreach (var target in _targets)
            {
                // 破棄された Unity のオブジェクトは、インターフェース越しでは == null で見分けられない
                if (target is UnityEngine.Object unityObject && unityObject == null || target.IsDestroyed) destroyedCount++;
            }

            if (destroyedCount == _destroyedCount) return;

            _destroyedCount = destroyedCount;
            _onTargetsChanged?.Invoke(_targets.Count, destroyedCount);
        }
    }
}
