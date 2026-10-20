using System;
using UnityEngine;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 割れたガラスの破片を、事前に作っておいて使い回すプール。ステージに1つ置く。
    /// 板1枚分の破片をまとめて1単位（GlassShardGroup）とし、全部使い中のときは最も古いものから再利用する。
    /// 破片は Rigidbody もコライダーも持たず、このクラスの Update でまとめて動かす。
    /// 破片が止まる地面の高さは、板ごとに真下へレイを1本撃って求める。当たらなければ固定の高さを使う。
    /// 破片のプレハブは1種類で、すべての板が同じ破片を使う。
    /// </summary>
    public sealed class GlassShardPool : MonoBehaviour
    {
        private GlassShardGroup[] _groups = Array.Empty<GlassShardGroup>();
        private RecycleBuffer<GlassShardGroup> _buffer;

        [SerializeField]
        [Tooltip("破片をまとめたプレハブ。子の1つ1つが破片で、板と同じ原点で作る。コライダーは付けない")]
        private GameObject _shardsPrefab;

        [SerializeField, Min(1)]
        [Tooltip("事前に作る破片のまとまりの数。板1枚で1つ使う")]
        private int _capacity = 400;

        [SerializeField]
        [Tooltip("破片が止まる地面のレイヤー。建物のレイヤーを含めない")]
        private LayerMask _groundLayers;

        [SerializeField, Min(0f)]
        [Tooltip("板の真下へ地面を探すレイの長さ（m）")]
        private float _groundRayLength = 300f;

        [SerializeField]
        [Tooltip("レイが地面に当たらなかったときに破片が止まる高さ（ワールド空間）")]
        private float _fallbackGroundY;

        [SerializeField, Min(0f)]
        [Tooltip("地面の高さからこの分だけ上で破片を止める（m）")]
        private float _landedHeight = 0.03f;

        /// <summary> ステージにあるプール。置かれていなければ null </summary>
        public static GlassShardPool Instance { get; private set; }

        /// <summary> 事前の生成が終わり、破片を出せるか </summary>
        public bool IsGenerated { get; private set; }

        /// <summary>
        /// 板 pane の破片を出して飛ばす。生成が終わる前や、破片のプレハブが未設定なら出さずに false を返す。
        /// </summary>
        /// <param name="pane">割れた板</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        /// <param name="speed">飛ばす速さ（m/s）</param>
        /// <param name="speedJitter">破片ごとの速さのばらつきの割合</param>
        /// <param name="maxAngularSpeed">回す角速度の最大（rad/s）</param>
        /// <param name="lifetime">飛ばし始めてから消すまでの秒数</param>
        public bool Spawn(Transform pane, Vector3 origin, float speed, float speedJitter, float maxAngularSpeed,
            float lifetime)
        {
            if (!IsGenerated) return false;

            var groundY = Physics.Raycast(pane.position, Vector3.down, out var hit, _groundRayLength, _groundLayers,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : _fallbackGroundY;

            _buffer.Get().Spawn(pane, origin, groundY, speed, speedJitter, maxAngularSpeed, lifetime);
            return true;
        }

        private void Reset()
        {
            _groundLayers = 1 << LayerMask.NameToLayer("Ground");
        }

        private void Awake()
        {
            Instance = this;

            // 破片をワールド座標のまま動かす為、根は原点に等倍で置く
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// 破片のまとまりを事前に作る。大量の生成が1フレームに集中しないよう、非同期で作る。
        /// </summary>
        private async void Start()
        {
            if (_shardsPrefab == null) return;

            // 複製を非アクティブのまま作る為、非アクティブにした元を複製する
            var template = Instantiate(_shardsPrefab, transform);
            template.SetActive(false);
            var instances = await InstantiateAsync(template, _capacity, transform);
            if (this == null) return;

            Destroy(template);
            _groups = new GlassShardGroup[instances.Length];
            for (var i = 0; i < instances.Length; i++)
            {
                var root = instances[i];
                root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                root.SetActive(false);
                _groups[i] = new GlassShardGroup(root);
            }

            _buffer = new RecycleBuffer<GlassShardGroup>(_groups);
            IsGenerated = true;
        }

        private void Update()
        {
            if (!IsGenerated) return;

            var deltaTime = Time.deltaTime;
            var gravityY = Physics.gravity.y;
            foreach (var group in _groups)
            {
                if (!group.IsActive || group.Step(deltaTime, gravityY, _landedHeight)) continue;

                group.OnRecycle();
                _buffer.Release(group);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
