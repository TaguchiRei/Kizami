using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 街にあるスポーン位置の、このステージでの設定を上書きする。ステージのシーンへ置く。敵を出さないスポーン位置を止め、編成をステージの編成に差し替える。
    /// 街のスポーン位置は常に街にある為、敵を出したくないステージ（チュートリアル）がこれで止める。
    /// すべて止める設定にすると、残す ID のスポーン位置以外をすべて止める。施設の数が街の組み立てで変わっても、ID を並べ直さずに済む。
    /// 編成は、ID が差し替える対象に当たるスポーン位置だけを差し替える。部隊の数と出す位置は変えない。
    /// EnemySpawnAdapter が敵の出し方を集めるより前に効かせる為、Awake で上書きする。街のシーンを先に読み込んでおく。
    /// </summary>
    public sealed class StageSpawnOverride : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("敵を出さないスポーン位置のギミック ID")]
        private string[] _disabledSpawnPointIds = Array.Empty<string>();

        [SerializeField]
        [Tooltip("すべてのスポーン位置を止めるか。止めない ID は下に並べる")]
        private bool _disablesAll;

        [SerializeField]
        [Tooltip("すべて止めるときに残す、スポーン位置のギミック ID")]
        private string[] _keptSpawnPointIds = Array.Empty<string>();

        [SerializeField]
        [Tooltip("このステージで使う編成。未設定なら差し替えない")]
        private EnemySquadComposition _composition;

        [SerializeField]
        [Tooltip("編成を差し替えるスポーン位置のギミック ID。末尾を * にすると前方一致")]
        private string[] _compositionSpawnPointIds = { "Spawn_Factory_*" };

        private void Awake()
        {
            if (_composition != null) ReplaceCompositions();
            if (_disablesAll) DisableAllExceptKept();

            foreach (var id in _disabledSpawnPointIds)
            {
                if (StageGimmick.TryFind(id, out var gimmick) && gimmick.TryGetComponent<EnemySpawnPoint>(out var point))
                {
                    point.Disable();
                }
                else
                {
                    Debug.LogWarning($"ID '{id}' のスポーン位置が見つからない為、止められません。", this);
                }
            }
        }

        private void ReplaceCompositions()
        {
            foreach (var point in FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!point.TryGetComponent<StageGimmick>(out var gimmick)) continue;

                foreach (var pattern in _compositionSpawnPointIds)
                {
                    if (!StageGimmick.Matches(pattern, gimmick.Id)) continue;

                    point.SetComposition(_composition);
                    break;
                }
            }
        }

        private void DisableAllExceptKept()
        {
            foreach (var point in FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (point.TryGetComponent<StageGimmick>(out var gimmick) && Array.IndexOf(_keptSpawnPointIds, gimmick.Id) >= 0) continue;

                point.Disable();
            }
        }
    }
}
