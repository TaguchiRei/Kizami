using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 崩落で倒した敵のエネルギーを、VFX Graph の粒で MainCamera の位置へ吸い込ませ、倒した数をチャージとして渡す Adapter。インゲームのシーンへ置く。
    /// 倒した位置はフレームごとに溜め、LateUpdate で GraphicsBuffer にまとめて VFX Graph へ渡し、イベントを 1 回だけ送る。
    /// チャージは粒が届くのを待たずに、倒したフレームに 1 回だけまとめて渡す。VFX Graph の粒は、届いたことを CPU に返しにくい為。
    /// </summary>
    /// <remarks>
    /// VFX Graph で受け取るものは次のとおり（名前を変えるときは、グラフの側も合わせる）。
    /// GraphicsBuffer の EnergyPositions（float3 の並び）、int の EnergyPositionCount（その数）、Vector3 の EnergyTarget（吸い込む先）、
    /// イベント OnEnergy（spawnCount に出す粒の数を入れて送る）。
    /// 粒の時間は VFX Graph の既定どおり Time.deltaTime で進み、スローモード中は一緒に遅くなる。
    /// </remarks>
    public sealed class EnemyEnergyAdapter : InitializableMonoBehaviour
    {
        private static readonly int _energyEventId = Shader.PropertyToID("OnEnergy");
        private static readonly int _positionsId = Shader.PropertyToID("EnergyPositions");
        private static readonly int _positionCountId = Shader.PropertyToID("EnergyPositionCount");
        private static readonly int _targetId = Shader.PropertyToID("EnergyTarget");
        private static readonly int _spawnCountId = Shader.PropertyToID("spawnCount");

        private readonly List<Vector3> _pendingPositions = new();

        [SerializeField]
        [Tooltip("エネルギーの粒を出す VisualEffect")]
        private VisualEffect _effect;

        [SerializeField, Min(1)]
        [Tooltip("1 フレームに VFX Graph へ渡せる、倒した位置の数。超えた分は粒を出さない（チャージには数える）")]
        private int _maxPositionsPerFrame = 256;

        [SerializeField, Min(1)]
        [Tooltip("倒した敵 1 体あたりに出す粒の数")]
        private int _particlesPerEnemy = 8;

        private Action<int> _onDefeatedCollected;
        private GraphicsBuffer _positionBuffer;
        private VFXEventAttribute _eventAttribute;

        /// <summary> このフレームに溜めた、倒した敵の数 </summary>
        private int _pendingCount;

        /// <summary>
        /// PlayerInitializer から呼ばれる。
        /// </summary>
        /// <param name="onDefeatedCollected">フレームごとに、そのフレームに倒した敵の数を渡す関数（ChargeService.AddCollapsedEnemies）</param>
        public void Initialize(Action<int> onDefeatedCollected)
        {
            _onDefeatedCollected = onDefeatedCollected;

            if (_effect == null)
            {
                UsefulLogger.LogError("VisualEffect が設定されていない為、エネルギーの粒を出せません。チャージは増えます。", this);
            }
            else
            {
                _positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxPositionsPerFrame,
                    sizeof(float) * 3);
                _eventAttribute = _effect.CreateVFXEventAttribute();
            }

            base.Initialize();
        }

        /// <summary>
        /// 崩落で倒した敵の位置を溜める。粒とチャージは、このフレームの LateUpdate でまとめて出す。
        /// </summary>
        /// <param name="position">粒を出す位置（ワールド座標）</param>
        public void Emit(Vector3 position)
        {
            if (!Initialized) return;

            _pendingCount++;
            if (_pendingPositions.Count < _maxPositionsPerFrame) _pendingPositions.Add(position);
        }

        private void LateUpdate()
        {
            var cameraMain = Camera.main;
            if (_effect != null && cameraMain != null) _effect.SetVector3(_targetId, cameraMain.transform.position);

            if (_pendingCount == 0) return;

            _onDefeatedCollected?.Invoke(_pendingCount);
            _pendingCount = 0;

            if (_effect != null && _pendingPositions.Count > 0)
            {
                _positionBuffer.SetData(_pendingPositions);
                _effect.SetGraphicsBuffer(_positionsId, _positionBuffer);
                _effect.SetInt(_positionCountId, _pendingPositions.Count);
                _eventAttribute.SetFloat(_spawnCountId,_pendingPositions.Count * _particlesPerEnemy);
                _effect.SendEvent(_energyEventId, _eventAttribute);
            }

            _pendingPositions.Clear();
        }

        private void OnDestroy()
        {
            _positionBuffer?.Release();
            _eventAttribute?.Dispose();
        }
    }
}
