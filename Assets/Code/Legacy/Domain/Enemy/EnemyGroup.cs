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
        private readonly float _friction;
        private readonly float _stopDistance;
        private readonly float _slowDistance;

        public EnemyGroup(EnemyColony[] colonies,
            Vector3 centerPosition, float moveForce,
            float colonyRepulsionRadius, float colonyRepulsionForce,
            float playerRepulsionRadius, float playerRepulsionForce,
            float maxSpeed, float friction, float stopDistance, float slowDistance)
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
            _friction = friction;
            _stopDistance = stopDistance;
            _slowDistance = slowDistance;
        }

        public void Update(Vector3 playerPosition, float dt)
        {
            EnemyColony[] colonies = Colonies;

            // 移動先をすべて計算
            for (int i = 0; i < colonies.Length; i++)
            {
                EnemyColony colony = colonies[i];

                Vector3 force = Vector3.zero;

                // プレイヤーへ接近 (Arrive 挙動)
                Vector3 toPlayer = playerPosition - colony.CenterPosition;
                toPlayer.y = 0;
                float distToPlayer = toPlayer.magnitude;

                if (distToPlayer > _stopDistance)
                {
                    // プレイヤーへの推進力
                    float speedScale = 1.0f;
                    if (distToPlayer < _slowDistance)
                    {
                        // 減衰距離内であれば、距離に応じて力を弱める
                        speedScale = (distToPlayer - _stopDistance) / (_slowDistance - _stopDistance);
                    }
                    
                    if (toPlayer.sqrMagnitude > 0.0001f)
                    {
                        force += (toPlayer / distToPlayer) * _moveForce * speedScale;
                    }
                }
                else
                {
                    // 停止距離内であれば、即座に速度を殺すための強い逆方向の力をかけるか、直接速度を0にする
                    // ここでは力ではなく速度更新時に処理する
                }

                // コロニー同士の反発 
                for (int j = 0; j < colonies.Length; j++)
                {
                    if (i == j) continue;

                    EnemyColony other = colonies[j];
                    Vector3 offset = colony.CenterPosition - other.CenterPosition;
                    offset.y = 0;
                    float dist = offset.magnitude;

                    if (dist > _colonyRepulsionRadius) continue;

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

                // 速度更新
                Vector3 velocity = colony.Velocity * Mathf.Pow(_friction, dt * 60f); // 摩擦の適用
                velocity += force * dt;
                velocity.y = 0;

                // 停止距離内の場合は停止
                if (distToPlayer <= _stopDistance)
                {
                    velocity = Vector3.zero;
                }

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