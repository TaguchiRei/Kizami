using UnityEngine;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 割れたガラス1枚分の破片をまとめたもの。物理は使わず、Transform だけで重力に従って飛ばし、地面の高さの少し上で止める。
    /// 破片の位置は常にワールド空間で扱う。まとまりの根は原点に等倍で置いてある前提（GlassShardPool が保つ）。
    /// </summary>
    public sealed class GlassShardGroup : IRecyclable
    {
        private readonly GameObject _root;
        private readonly Transform[] _shards;
        private readonly Vector3[] _restPositions;
        private readonly Quaternion[] _restRotations;
        private readonly Vector3[] _restScales;
        private readonly Vector3[] _velocities;
        private readonly Vector3[] _angularVelocities;
        private readonly bool[] _isLanded;

        private float _groundY;
        private float _elapsed;
        private float _lifetime;

        /// <summary> RecycleBuffer が割り振る番号 </summary>
        public int RecycleId { get; set; }

        /// <summary> 破片を飛ばしている最中か </summary>
        public bool IsActive { get; private set; }

        /// <param name="root">破片を子に持つ根。子の位置・向き・大きさを、板に合わせて置く前の基準にする</param>
        public GlassShardGroup(GameObject root)
        {
            _root = root;
            var count = root.transform.childCount;
            _shards = new Transform[count];
            _restPositions = new Vector3[count];
            _restRotations = new Quaternion[count];
            _restScales = new Vector3[count];
            _velocities = new Vector3[count];
            _angularVelocities = new Vector3[count];
            _isLanded = new bool[count];

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
            _groundY = groundY;
            _elapsed = 0f;
            _lifetime = lifetime;

            var paneScale = pane.lossyScale;
            for (var i = 0; i < _shards.Length; i++)
            {
                var position = pane.TransformPoint(_restPositions[i]);
                _shards[i].SetPositionAndRotation(position, pane.rotation * _restRotations[i]);
                _shards[i].localScale = Vector3.Scale(_restScales[i], paneScale);

                var away = position - origin;
                var direction = away.sqrMagnitude > 1e-6f ? away.normalized : pane.forward;
                _velocities[i] = direction * (speed * (1f + Random.Range(-speedJitter, speedJitter)));
                _angularVelocities[i] = Random.insideUnitSphere * maxAngularSpeed;
                _isLanded[i] = false;
            }

            _root.SetActive(true);
            IsActive = true;
        }

        /// <summary>
        /// 破片を deltaTime 分だけ進める。寿命が来たら false を返す。
        /// </summary>
        /// <param name="deltaTime">進める秒数</param>
        /// <param name="gravityY">重力加速度の Y 成分（m/s²）</param>
        /// <param name="landedHeight">地面の高さからこの分だけ上で止める（m）</param>
        public bool Step(float deltaTime, float gravityY, float landedHeight)
        {
            _elapsed += deltaTime;
            if (_elapsed >= _lifetime) return false;

            var floorY = _groundY + landedHeight;
            for (var i = 0; i < _shards.Length; i++)
            {
                if (_isLanded[i]) continue;

                var velocity = _velocities[i];
                velocity.y += gravityY * deltaTime;
                _velocities[i] = velocity;

                var shard = _shards[i];
                var position = shard.position + velocity * deltaTime;
                var rotation = shard.rotation;
                var angularVelocity = _angularVelocities[i];
                var angularSpeed = angularVelocity.magnitude;
                if (angularSpeed > 1e-4f)
                {
                    rotation = Quaternion.AngleAxis(angularSpeed * deltaTime * Mathf.Rad2Deg, angularVelocity / angularSpeed) * rotation;
                }

                if (position.y <= floorY)
                {
                    position.y = floorY;
                    _isLanded[i] = true;
                }

                shard.SetPositionAndRotation(position, rotation);
            }

            return true;
        }

        public void OnRecycle()
        {
            IsActive = false;
            _root.SetActive(false);
        }
    }
}
