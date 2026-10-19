using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループが出てくる位置。EnemySpawnSystem の子に置き、1つにつき編成の1グループを出す。この位置がグループの持ち場になる。
    /// 属する区画は作者が指定する。追跡を始める判定は持ち場の位置で行い、指定した区画は置き場所の確認に使う。
    /// 建物（ボクセルのモデル）を指定すると、読み込んだ時点の体積の壊れたとみなす割合以上が削れたときに、補充と出し直しの敵を出さなくなる。
    /// 建物は EnemySpawn レイヤーに置き、敵の経路の格子に入れない。敵は建物の内側に出て、そのまま歩いて出てくる。
    /// 出現を保留すると、初期化では置き場所だけを確保して敵を出さず、解放したあとに出す。
    /// 動かない設定にすると、部隊は追跡範囲に入ってもプレイヤーを追わず、持ち場で待機したまま攻撃する。
    /// </summary>
    public sealed class EnemySpawnPoint : MonoBehaviour
    {
        /// <summary> 構造物の上に置く点。構造物は敵の生成システムの子ではない為、ここから集める </summary>
        private static readonly List<EnemySpawnPoint> _structurePoints = new();

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

        [SerializeField, Min(0f)]
        [Tooltip("グループが全滅してから、満員のグループを出し直すまでの時間（秒）")]
        private float _respawnInterval = 30f;

        [SerializeField]
        [Tooltip("スポーン位置の建物。壊れたら補充と出し直しの敵を出さない。未設定なら壊れない")]
        private VoxelModelLoader _structure;

        [SerializeField]
        [Tooltip("スポーン位置の建物が階ごとのボクセルの建物（VoxelBuilding）のとき、その建物。壊れたとみなす割合は下と同じ。未設定なら使わない")]
        private VoxelBuilding _structureBuilding;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("建物の、読み込んだ時点の体積のうち、この割合以上が削れたら壊れたとみなす")]
        private float _brokenLossRatio = 0.05f;

        [SerializeField]
        [Tooltip("敵を出すか。初期化のあとに切り替えたときは、出さない方向にだけ効く")]
        private bool _isEnabled = true;

        [SerializeField]
        [Tooltip("動かないか。追跡範囲に入っても追わず、持ち場で待機したまま攻撃する")]
        private bool _isStationary;

        [SerializeField]
        [Tooltip("構造物（倒れる塔など）の上に置く点か。初期化のときの位置は仮なので、区画の重複を調べず、出すときの位置から出す")]
        private bool _isOnStructure;

        [SerializeField]
        [Tooltip("出す敵をその場に固定するか。固定された敵は歩かず落ちず、立ったまま攻撃する。格子の外や構造物の上に出す敵に使う")]
        private bool _isPinned;

        [SerializeField]
        [Tooltip("出現を保留するか。初期化では置き場所だけ確保して敵を出さず、Release のあとに出す。解放から出るまでは、出し直しの間隔の残りを待つ")]
        private bool _isHeld;

        /// <summary> 建物の、読み込んだ時点の体積（m³）。読み込む前は0 </summary>
        private float _initialVolume;

        private IDisposable _loadedSubscription;

        /// <summary> 出すグループの編成。未設定なら null </summary>
        public EnemySquadComposition Composition => _composition;

        /// <summary> 属する区画の番号 </summary>
        public Vector2Int Section => _section;

        /// <summary> 敵を出せる状態か。GameObject が非アクティブのときも出さない </summary>
        public bool IsEnabled => _isEnabled && isActiveAndEnabled;

        /// <summary> グループが全滅してから、満員のグループを出し直すまでの時間（秒） </summary>
        public float RespawnInterval => _respawnInterval;

        /// <summary> 建物が壊れたか。建物が未設定か、読み込む前は false </summary>
        public bool IsBroken
        {
            get
            {
                if (_structureBuilding != null) return _structureBuilding.LostVolumeRatio >= _brokenLossRatio;
                if (_structure == null || _initialVolume <= 0f) return false;

                return _structure.Volume <= _initialVolume * (1f - _brokenLossRatio);
            }
        }

        /// <summary> 構造物の上に置く点か </summary>
        public bool IsOnStructure => _isOnStructure;

        /// <summary> 有効な、構造物の上に置く点 </summary>
        public static IReadOnlyList<EnemySpawnPoint> StructurePoints => _structurePoints;

        /// <summary> 出す敵をその場に固定するか </summary>
        public bool IsPinned => _isPinned;

        /// <summary> 動かないか </summary>
        public bool IsStationary => _isStationary;

        /// <summary> 出現を保留しているか </summary>
        public bool IsHeld => _isHeld;

        /// <summary> 初期の出現と、補充と出し直しで敵を出せるか </summary>
        public bool CanSpawn => IsEnabled && !_isHeld && !IsBroken;

        /// <summary> 出すグループの編成を差し替える。敵を出す前に呼ぶ </summary>
        public void SetComposition(EnemySquadComposition composition)
        {
            _composition = composition;
        }

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

        /// <summary> 出現を保留する。構造物の上に置く点は、一度出したあと保留に戻り、倒されても出し直さない </summary>
        public void Hold()
        {
            _isHeld = true;
        }

        /// <summary> 出現の保留を解く </summary>
        public void Release()
        {
            _isHeld = false;
        }

        /// <summary> 半径の円の中から、出す位置を1つ選ぶ </summary>
        public Vector3 GetSpawnPosition()
        {
            var offset = Random.insideUnitCircle * _radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        private void OnEnable()
        {
            if (_isOnStructure) _structurePoints.Add(this);
        }

        private void OnDisable()
        {
            _structurePoints.Remove(this);
        }

        private void Awake()
        {
            // VoxelModelLoader は Start で読み込むので、Awake で登録すれば読み込みの通知を逃さない
            if (_structure != null)
            {
                _loadedSubscription = _structure.RegisterOnLoaded(loader => _initialVolume = loader.InitialVolume);
            }
        }

        private void OnDestroy()
        {
            _loadedSubscription?.Dispose();
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
        /// 区画の色でアイコンと半径の円を描く。区画の番号が位置と食い違うか、位置の区画に Hierarchy で前の有効なスポーン位置があれば赤、敵を出さない状態か出現を保留しているか建物が壊れていれば灰色にする。
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
            if (!_isEnabled || _isHeld || IsBroken) return Color.gray;

            var system = GetComponentInParent<EnemySpawnSystem>();
            if (system == null) return EnemySpawnSystem.GetSectionColor(_section);

            var section = system.GetSection(transform.position);
            if (section != _section || HasEarlierPointInSection(system, section)) return Color.red;

            return EnemySpawnSystem.GetSectionColor(_section);
        }

        /// <summary>
        /// 同じ生成システムの中で、Hierarchy でこれより前にある有効なスポーン位置が、位置の区画 section にあるか。
        /// EnemySpawnAdapter は、区画が重なったときに前のスポーン位置だけを使う。
        /// </summary>
        private bool HasEarlierPointInSection(EnemySpawnSystem system, Vector2Int section)
        {
            foreach (var other in system.GetComponentsInChildren<EnemySpawnPoint>())
            {
                if (other == this) return false;
                if (other._isEnabled && system.GetSection(other.transform.position) == section) return true;
            }

            return false;
        }
    }
}
