using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージごとの敵の出し方と経路の格子の設定。ステージシーンに置き、子に EnemySpawnPoint を置く。
    /// インゲームの EnemySpawnAdapter が FindAnyObjectByType で見つけて読む。
    /// 区画は、格子の範囲の中心が区画の中心に来るように並べる。
    /// </summary>
    public sealed class EnemySpawnSystem : MonoBehaviour
    {
        /// <summary> 区画の色分けの面の不透明度 </summary>
        private const float SECTION_FILL_ALPHA = 0.15f;

        /// <summary> 区画の色。隣どうしが同じ色にならないよう、番号の x と z の偶奇で 4 色から選ぶ </summary>
        private static readonly Color[] _sectionColors =
        {
            new(0.95f, 0.55f, 0.2f),
            new(0.3f, 0.75f, 0.95f),
            new(0.45f, 0.85f, 0.35f),
            new(0.85f, 0.45f, 0.9f)
        };

        [SerializeField]
        [Tooltip("敵の経路の格子を作る範囲（ワールド座標）。床を探すレイは上面から底面まで撃つので、天井より下に置く")]
        private Bounds _navigationBounds = new(Vector3.zero, new Vector3(100f, 30f, 100f));

        [SerializeField, Min(1f)]
        [Tooltip("距離マップを計算する区画の一辺（m）。プレイヤーのいる区画とその周りの 3×3（追跡範囲）だけを計算し、持ち場がその外のグループは追わない。区画は格子の範囲の中心が区画の中心に来るように並べる")]
        private float _sectionSize = 100f;

        [SerializeField]
        [Tooltip("区画の色分けを、選択していないときもシーンビューに描く")]
        private bool _drawsSectionsAlways = true;

        /// <summary> 敵の経路の格子を作る範囲（ワールド座標） </summary>
        public Bounds NavigationBounds => _navigationBounds;

        /// <summary> 距離マップを計算する区画の一辺（m） </summary>
        public float SectionSize => _sectionSize;

        /// <summary> 番号 (0, 0) の区画の最小の角（ワールド座標の x, z） </summary>
        public Vector2 SectionOrigin =>
            new Vector2(_navigationBounds.center.x, _navigationBounds.center.z) - Vector2.one * (_sectionSize * 0.5f);

        /// <summary>
        /// 区画の番号から、シーンビューで区画とスポーン位置を描く色を返す。
        /// </summary>
        public static Color GetSectionColor(Vector2Int section)
        {
            return _sectionColors[(section.x & 1) + 2 * (section.y & 1)];
        }

        /// <summary>
        /// 位置が入る区画の番号を返す。
        /// </summary>
        public Vector2Int GetSection(Vector3 position)
        {
            var local = (new Vector2(position.x, position.z) - SectionOrigin) / _sectionSize;
            return new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
        }

        private void OnDrawGizmos()
        {
            if (_drawsSectionsAlways) DrawSections();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawsSectionsAlways) DrawSections();

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(_navigationBounds.center, _navigationBounds.size);
        }

        /// <summary>
        /// この GameObject の高さに、区画ごとの半透明の面と枠を、区画の色で描く。格子の範囲の底面は地面より下にあることが多く、地面に隠れる為。
        /// 範囲の外にはみ出す区画は、範囲の中の部分だけを描く。
        /// </summary>
        private void DrawSections()
        {
            var min = GetSection(_navigationBounds.min);
            var max = GetSection(_navigationBounds.max);
            var origin = SectionOrigin;
            var boundsMin = new Vector2(_navigationBounds.min.x, _navigationBounds.min.z);
            var boundsMax = new Vector2(_navigationBounds.max.x, _navigationBounds.max.z);
            var y = transform.position.y;

            for (var z = min.y; z <= max.y; z++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var sectionMin = Vector2.Max(origin + new Vector2(x, z) * _sectionSize, boundsMin);
                    var sectionMax = Vector2.Min(origin + new Vector2(x + 1, z + 1) * _sectionSize, boundsMax);
                    var center = (sectionMin + sectionMax) * 0.5f;
                    var size = sectionMax - sectionMin;
                    var color = GetSectionColor(new Vector2Int(x, z));

                    Gizmos.color = new Color(color.r, color.g, color.b, SECTION_FILL_ALPHA);
                    Gizmos.DrawCube(new Vector3(center.x, y, center.y), new Vector3(size.x, 0f, size.y));
                    Gizmos.color = color;
                    Gizmos.DrawWireCube(new Vector3(center.x, y, center.y), new Vector3(size.x, 0f, size.y));
                }
            }
        }
    }
}
