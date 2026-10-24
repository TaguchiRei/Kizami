using UnityEngine;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 割れたガラス1枚分の破片をまとめたもの。物理は使わず、Transform だけで重力に従って飛ばし、地面の高さの少し上で止める。
    /// 破片の動きの状態は GlassShardBuffers にあり、毎フレームの更新は GlassShardPool が Job でまとめて行う。このクラスは、飛ばし始めの配置と、グループの寿命を扱う。
    /// 破片の位置は常にワールド空間で扱う。まとまりの根は原点に等倍で置いてある前提（GlassShardPool が保つ）。
    /// </summary>
    public sealed class GlassShardGroup : IRecyclable
    {
        private readonly GameObject _root;
        private readonly Transform[] _shards;
        private readonly Vector3[] _restPositions;
        private readonly Quaternion[] _restRotations;
        private readonly Vector3[] _restScales;
        private readonly GlassShardBuffers _buffers;

        /// <summary> GlassShardBuffers の中のグループの番号 </summary>
        private readonly int _index;

        /// <summary> RecycleBuffer が割り振る番号 </summary>
        public int RecycleId { get; set; }

        /// <summary> 破片を飛ばしている最中か </summary>
        public bool IsActive { get; private set; }

        /// <summary> 根の下の破片。順番は GlassShardBuffers の破片の番号と同じ </summary>
        public Transform[] Shards => _shards;

        /// <param name="root">破片を子に持つ根。子の位置・向き・大きさを、板に合わせて置く前の基準にする</param>
        /// <param name="index">GlassShardBuffers の中のグループの番号</param>
        /// <param name="buffers">全破片の動きの状態</param>
        public GlassShardGroup(GameObject root, int index, GlassShardBuffers buffers)
        {
            _root = root;
            _index = index;
            _buffers = buffers;
            var count = root.transform.childCount;
            _shards = new Transform[count];
            _restPositions = new Vector3[count];
            _restRotations = new Quaternion[count];
            _restScales = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var shard = root.transform.GetChild(i);
                _shards[i] = shard;
                _restPositions[i] = shard.localPosition;
                _restRotations[i] = shard.localRotation;
                _restScales[i] = shard.localScale;
            }
        }

        /// <summary>
        /// 破片を板の位置・向き・大きさに置き、origin から離れる向きへ飛ばし始める。
        /// </summary>
        /// <param name="pane">割れた板</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        /// <param name="groundY">破片が止まる地面の高さ（ワールド空間）</param>
        /// <param name="speed">飛ばす速さ（m/s）</param>
        /// <param name="speedJitter">破片ごとの速さのばらつきの割合</param>
        /// <param name="maxAngularSpeed">回す角速度の最大（rad/s）</param>
        /// <param name="lifetime">飛ばし始めてから消すまでの秒数</param>
        public void Spawn(Transform pane, Vector3 origin, float groundY, float speed, float speedJitter,
            float maxAngularSpeed, float lifetime)
        {
            _buffers.GroundY[_index] = groundY;
            _buffers.Remaining[_index] = lifetime;

            var offset = _index * _buffers.ShardsPerGroup;
            var paneScale = pane.lossyScale;
            for (var i = 0; i < _shards.Length; i++)
            {
                var position = pane.TransformPoint(_restPositions[i]);
                _shards[i].SetPositionAndRotation(position, pane.rotation * _restRotations[i]);
                var scale = Vector3.Scale(_restScales[i], paneScale);
                _shards[i].localScale = scale;
                _buffers.SpawnScales[offset + i] = scale;

                var away = position - origin;
                var direction = away.sqrMagnitude > 1e-6f ? away.normalized : pane.forward;
                _buffers.Velocities[offset + i] = direction * (speed * (1f + Random.Range(-speedJitter, speedJitter)));
                _buffers.AngularVelocities[offset + i] = Random.insideUnitSphere * maxAngularSpeed;
                _buffers.IsLanded[offset + i] = false;
            }

            _root.SetActive(true);
            IsActive = true;
        }

        /// <summary>
        /// 残り時間を deltaTime 分だけ減らす。寿命が来たら false を返す。
        /// </summary>
        /// <param name="deltaTime">進める秒数</param>
        public bool Advance(float deltaTime)
        {
            var remaining = _buffers.Remaining[_index] - deltaTime;
            if (remaining <= 0f) return false;

            _buffers.Remaining[_index] = remaining;
            return true;
        }

        public void OnRecycle()
        {
            IsActive = false;
            _buffers.Remaining[_index] = 0f;
            _root.SetActive(false);
        }
    }
}
