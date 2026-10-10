using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループが出てくる位置。EnemySpawnSystem の子に置き、1 つにつき編成の 1 グループを出す。この位置がグループの持ち場になる。
    /// 属する区画は作者が指定する。追跡を始める判定は持ち場の位置で行い、指定した区画は置き場所の確認に使う。
    /// </summary>
    public sealed class EnemySpawnPoint : MonoBehaviour
    {
        /// <summary> シーンビューのアイコンの画像。Assets/Gizmos/ からの相対パス </summary>
        private const string ICON_NAME = "EnemySpawnPoint.png";

        /// <summary> アイコンを描く、スポーン位置からの高さ（m） </summary>
        private const float ICON_HEIGHT = 2f;

        [SerializeField]
        [Tooltip("出すグループの編成")]
        private EnemySquadComposition _composition;

        [SerializeField]
        [Tooltip("属する区画の番号。置いたときに位置から入る。位置と食い違うと、初期化のときに警告を出す")]
        private Vector2Int _section;

        [SerializeField, HideInInspector]
        [Tooltip("区画の番号を入れたか。(0, 0) と未設定を見分ける為に持つ")]
        private bool _hasSection;

        [SerializeField, Min(0f)]
        [Tooltip("この半径（m）の円の中にメンバーを出す")]
        private float _radius = 4f;

        [SerializeField]
        [Tooltip("敵を出すか。初期化のあとに切り替えたときは、出さない方向にだけ効く")]
        private bool _isEnabled = true;

        /// <summary> 出すグループの編成。未設定なら null </summary>
        public EnemySquadComposition Composition => _composition;

        /// <summary> 属する区画の番号 </summary>
        public Vector2Int Section => _section;

        /// <summary> 敵を出せる状態か。GameObject が非アクティブのときも出さない </summary>
        public bool IsEnabled => _isEnabled && isActiveAndEnabled;

        /// <summary> 敵を出せる状態にする </summary>
        public void Enable()
        {
            _isEnabled = true;
        }

        /// <summary> 敵を出さない状態にする </summary>
        public void Disable()
        {
            _isEnabled = false;
        }

        /// <summary> 半径の円の中から、出す位置を 1 つ選ぶ </summary>
        public Vector3 GetSpawnPosition()
        {
            var offset = Random.insideUnitCircle * _radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        private void Reset()
        {
            _hasSection = false;
            FillSection();
        }

        private void OnValidate()
        {
            if (!_hasSection) FillSection();
        }

        /// <summary>
        /// 区画の番号を、親の EnemySpawnSystem から位置で求めて入れる。親がなければ入れない。親が非アクティブ（使わない方の生成システム）でも入れる。
        /// </summary>
        private void FillSection()
        {
            var system = GetComponentInParent<EnemySpawnSystem>(true);
            if (system == null) return;

            _section = system.GetSection(transform.position);
            _hasSection = true;
        }

        /// <summary>
        /// 区画の色でアイコンと半径の円を描く。区画の番号が位置と食い違えば赤、敵を出さない状態なら灰色にする。
        /// アイコンは距離によらず同じ大きさで描かれ、クリックするとこのスポーン位置を選べる。
        /// </summary>
        private void OnDrawGizmos()
        {
            var color = GetGizmoColor();
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, _radius);
            Gizmos.DrawIcon(transform.position + Vector3.up * ICON_HEIGHT, ICON_NAME, false, color);
        }

        private Color GetGizmoColor()
        {
            if (!_isEnabled) return Color.gray;

            var system = GetComponentInParent<EnemySpawnSystem>();
            if (system != null && system.GetSection(transform.position) != _section) return Color.red;

            return EnemySpawnSystem.GetSectionColor(_section);
        }
    }
}
