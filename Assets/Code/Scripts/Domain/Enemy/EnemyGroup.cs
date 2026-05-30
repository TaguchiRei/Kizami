using UnityEngine;

namespace Kizami.Domain.Runtime.Enemy
{
    /// <summary>
    /// 各コロニーを動かすためのドメインロジックを含むエンティティ
    /// </summary>
    public class EnemyGroup
    {
        public EnemyColony[] Colonies { get; }

        public Vector3 CenterPosition { get; private set; }

        // 処理順に依存して形状が変化しないようにするための計算バッファ
        private readonly Vector3[] _nextVelocity;
        private readonly Vector3[] _nextPosition;

        // パラメータ
        private readonly float _moveForce;
        private readonly float _colonyRepulsionRadius;
        private readonly float _colonyRepulsionForce;
        private readonly float _playerRepulsionRadius;
        private readonly float _playerRepulsionForce;
        private readonly float _maxSpeed;

        public EnemyGroup(EnemyColony[] colonies,
            Vector3 centerPosition, float moveForce,
            float colonyRepulsionRadius, float colonyRepulsionForce,
            float playerRepulsionRadius, float playerRepulsionForce,
            float maxSpeed)
        {
            Colonies = colonies;
            CenterPosition = centerPosition;

            _nextVelocity = new Vector3[colonies.Length];
            _nextPosition = new Vector3[colonies.Length];

            _moveForce = moveForce;
            _colonyRepulsionRadius = colonyRepulsionRadius;
            _colonyRepulsionForce = colonyRepulsionForce;
            _playerRepulsionRadius = playerRepulsionRadius;
            _playerRepulsionForce = playerRepulsionForce;
            _maxSpeed = maxSpeed;
        }

        public void Update(Vector3 playerPosition, float dt)
        {
            EnemyColony[] colonies = Colonies;

            // 移動先をすべて計算
            for (int i = 0; i < colonies.Length; i++)
            {
                EnemyColony colony = colonies[i];

                Vector3 force = Vector3.zero;

                // プレイヤーへ接近 
                Vector3 seek = playerPosition - colony.CenterPosition;
                seek.y = 0; // 高さを無視

                if (seek.sqrMagnitude > 0.0001f)
                {
                    force += seek.normalized * _moveForce;
                }

                // コロニー同士の反発 
                for (int j = 0; j < colonies.Length; j++)
                {
                    if (i == j) continue;

                    EnemyColony other = colonies[j];
                    Vector3 offset = colony.CenterPosition - other.CenterPosition;
                    offset.y = 0; // 高さを無視
                    float dist = offset.magnitude;

                    if (dist > _colonyRepulsionRadius) continue;

                    // 完全に重なっている場合は、インデックスに基づいて分離方向を決める
                    if (dist <= 0.0001f)
                    {
                        float angle = (float)i / colonies.Length * Mathf.PI * 2f;
                        Vector3 pushDir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        force += pushDir * _colonyRepulsionForce;
                    }
                    else
                    {
                        float ratio = 1f - dist / _colonyRepulsionRadius;
                        force += (offset / dist) * ratio * _colonyRepulsionForce;
                    }
                }

                // プレイヤー反発
                {
                    Vector3 offset = colony.CenterPosition - playerPosition;
                    offset.y = 0; // 高さを無視
                    float dist = offset.magnitude;

                    if (dist < _playerRepulsionRadius)
                    {
                        if (dist <= 0.0001f)
                        {
                            // プレイヤーの真上にいる場合は真後ろ（適当な方向）へ飛ばす
                            force += Vector3.back * _playerRepulsionForce;
                        }
                        else
                        {
                            float ratio = 1f - dist / _playerRepulsionRadius;
                            force += (offset / dist) * ratio * _playerRepulsionForce;
                        }
                    }
                }

                // 速度更新
                Vector3 velocity = colony.Velocity + force * dt;
                velocity.y = 0; // 垂直方向の速度を排除

                float maxSqr = _maxSpeed * _maxSpeed;

                if (velocity.sqrMagnitude > maxSqr)
                {
                    velocity = velocity.normalized * _maxSpeed;
                }

                _nextVelocity[i] = velocity;
                _nextPosition[i] = colony.CenterPosition + velocity * dt;
            }
            
            // 一括で反映を行う
            for (int i = 0; i < colonies.Length; i++)
            {
                colonies[i].SetVelocity(_nextVelocity[i]);
                colonies[i].SetCenterPosition(_nextPosition[i]);
            }
        }
    }
}