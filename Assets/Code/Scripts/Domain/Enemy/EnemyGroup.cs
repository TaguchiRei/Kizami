using System.Numerics;
using Kizami.Domain.Runtime.Enemy;
using Vector3 = UnityEngine.Vector3;

public class EnemyGroup
{
    public EnemyColony[] Colonies { get; }

    public Vector3 CenterPosition { get; private set; }

    private readonly float _moveForce;
    private readonly float _colonyRepulsionRadius;
    private readonly float _colonyRepulsionForce;
    private readonly float _playerRepulsionRadius;
    private readonly float _playerRepulsionForce;
    private readonly float _maxSpeed;

    public void Update(
        Vector3 playerPosition,
        float deltaTime)
    {
        for (int i = 0; i < Colonies.Length; i++)
        {
            EnemyColony colony = Colonies[i];

            Vector3 force = Vector3.zero;

            //
            // プレイヤーへ近づく
            //
            Vector3 seekDirection =
                playerPosition - colony.CenterPosition;

            if (seekDirection.sqrMagnitude > 0.0001f)
            {
                force += seekDirection.normalized * _moveForce;
            }

            //
            // コロニー同士の反発
            //
            for (int j = 0; j < Colonies.Length; j++)
            {
                if (i == j)
                {
                    continue;
                }

                EnemyColony other = Colonies[j];

                Vector3 offset =
                    colony.CenterPosition -
                    other.CenterPosition;

                float distance = offset.magnitude;

                if (distance <= 0f)
                {
                    continue;
                }

                if (distance > _colonyRepulsionRadius)
                {
                    continue;
                }

                float ratio =
                    1f - distance / _colonyRepulsionRadius;

                force +=
                    offset.normalized *
                    ratio *
                    _colonyRepulsionForce;
            }

            //
            // プレイヤーとの反発
            //
            {
                Vector3 offset =
                    colony.CenterPosition -
                    playerPosition;

                float distance = offset.magnitude;

                if (distance > 0f &&
                    distance < _playerRepulsionRadius)
                {
                    float ratio =
                        1f - distance / _playerRepulsionRadius;

                    force +=
                        offset.normalized *
                        ratio *
                        _playerRepulsionForce;
                }
            }

            Vector3 velocity = colony.Velocity + force * deltaTime;

            if (velocity.sqrMagnitude >
                _maxSpeed * _maxSpeed)
            {
                velocity =
                    velocity.normalized *
                    _maxSpeed;
            }

            colony.SetVelocity(velocity);

            colony.SetCenterPosition(
                colony.CenterPosition +
                velocity * deltaTime);
        }
    }
}