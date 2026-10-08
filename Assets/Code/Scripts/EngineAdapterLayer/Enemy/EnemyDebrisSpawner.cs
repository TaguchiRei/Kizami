using UnityEngine;
using UsefulToolkit.MeshCut;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体から外れた切っていない部位を、見た目用の物（EnemyDebris）で散らばらせて消す。EnemySpawnAdapter が持つ。
    /// 見た目用の物は初期化のときに作り、空きがなければ最も古い物を使い回す。
    /// </summary>
    public sealed class EnemyDebrisSpawner
    {
        /// <summary> 見た目用の物。並びは _debrisBuffer の RecycleId と同じ </summary>
        private readonly EnemyDebris[] _debris;

        private readonly RecycleBuffer<EnemyDebris> _debrisBuffer;
        private readonly float _lifetime;
        private readonly float _outwardSpeed;
        private readonly float _upwardSpeed;
        private readonly float _angularSpeed;

        /// <param name="parent">見た目用の物を置く親。拡大率を 1 に保つこと</param>
        /// <param name="material">ディゾルブのマテリアル</param>
        /// <param name="capacity">同時に出せる見た目用の物の数</param>
        /// <param name="lifetime">出てから消え終わるまでの時間（秒）</param>
        /// <param name="outwardSpeed">体の中心から外へ飛ぶ速さ（m/s）</param>
        /// <param name="upwardSpeed">上へ飛ぶ速さ（m/s）</param>
        /// <param name="angularSpeed">回る速さ（度/秒）</param>
        public EnemyDebrisSpawner(Transform parent, Material material, int capacity, float lifetime,
            float outwardSpeed, float upwardSpeed, float angularSpeed)
        {
            _lifetime = lifetime;
            _outwardSpeed = outwardSpeed;
            _upwardSpeed = upwardSpeed;
            _angularSpeed = angularSpeed;

            _debris = new EnemyDebris[capacity];
            for (var i = 0; i < _debris.Length; i++)
            {
                _debris[i] = new EnemyDebris(parent, material);
            }

            _debrisBuffer = new RecycleBuffer<EnemyDebris>(_debris);
        }

        /// <summary>
        /// 部位と同じ形の見た目用の物を出し、体の中心から外向きと上向きに飛ばす。
        /// </summary>
        /// <param name="part">体から外れた、切っていない部位</param>
        /// <param name="origin">散らばる中心（体の位置）</param>
        public void Spawn(CuttableObject part, Vector3 origin)
        {
            if (part == null) return;

            var outward = part.transform.position - origin;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0f)
            {
                outward.Normalize();
            }
            else
            {
                var circle = Random.insideUnitCircle.normalized;
                outward = new Vector3(circle.x, 0f, circle.y);
            }

            var velocity = outward * _outwardSpeed + Vector3.up * _upwardSpeed;
            _debrisBuffer.Get().Show(part, velocity, Random.onUnitSphere * _angularSpeed);
        }

        /// <summary>
        /// 出ている見た目用の物を動かし、消え終わったものをバッファへ返す。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Tick(float deltaTime)
        {
            foreach (var debris in _debris)
            {
                if (!debris.IsActive || debris.Tick(deltaTime, _lifetime)) continue;

                debris.OnRecycle();
                _debrisBuffer.Release(debris);
            }
        }
    }
}
