using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 街にあるスポーン位置のうち、このステージで敵を出さないものを ID で止める。ステージのシーンへ置く。
    /// 街のスポーン位置は常に街にある為、敵を出したくないステージ（チュートリアル）がこれで止める。
    /// EnemySpawnAdapter が敵の出し方を集めるより前に効かせる為、Awake で止める。街のシーンを先に読み込んでおく。
    /// </summary>
    public sealed class StageSpawnOverride : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("敵を出さないスポーン位置のギミック ID")]
        private string[] _disabledSpawnPointIds = System.Array.Empty<string>();

        private void Awake()
        {
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
    }
}
