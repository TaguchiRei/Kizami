using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボクセルから切り離されて落ちてくる塊に、敵が潰されたかを調べる。EnemySpawnAdapter が持つ。
    /// 敵には PhysX のコライダーを付けないので、塊の範囲に入った敵について、体の中心が塊の SDF の内側（または表面の近く）かを調べる。
    /// 落ちている塊は少ないので、格子の索引は作らず、塊ごとに全部の敵と比べる。
    /// </summary>
    public sealed class EnemyCollapseDetector : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new();
        private readonly List<SeparatedPiece> _separatedPieces = new();

        private readonly float _minFallSpeed;
        private readonly float _minVolume;
        private readonly float _bodyCenterHeight;
        private readonly float _surfaceMargin;

        /// <param name="minFallSpeed">潰す塊の、下向きの速さの下限（m/s）</param>
        /// <param name="minVolume">潰す塊の、体積の下限（m³）</param>
        /// <param name="bodyCenterHeight">調べる体の中心の、体の根からの高さ（m）</param>
        /// <param name="surfaceMargin">体の中心が塊の表面からこの距離（m）以内なら、潰されたとする</param>
        public EnemyCollapseDetector(float minFallSpeed, float minVolume, float bodyCenterHeight, float surfaceMargin)
        {
            _minFallSpeed = minFallSpeed;
            _minVolume = minVolume;
            _bodyCenterHeight = bodyCenterHeight;
            _surfaceMargin = surfaceMargin;
        }

        /// <summary>
        /// ボクセルのモデルを見張り、切り離された Rigidbody を持つ塊を調べる対象に加える。
        /// </summary>
        public void Watch(VoxelModelLoader loader)
        {
            _subscriptions.Add(loader.RegisterOnPieceSplit(OnPieceSplit));
        }

        /// <summary>
        /// 下限より速く落ちている、下限より大きい塊に体の中心が入った敵を倒し、崩落で倒されたことを記録する。
        /// </summary>
        /// <returns>この呼び出しで倒した敵の数</returns>
        public int Detect(NativeArray<EnemyAgent> agents)
        {
            var defeatedCount = 0;

            for (var i = _separatedPieces.Count - 1; i >= 0; i--)
            {
                var separated = _separatedPieces[i];
                if (separated.Piece == null || separated.Body == null)
                {
                    _separatedPieces.RemoveAt(i);
                    continue;
                }

                if (separated.Body.linearVelocity.y > -_minFallSpeed || separated.Piece.Volume < _minVolume) continue;

                var bounds = separated.Piece.WorldBounds;
                bounds.Expand(_surfaceMargin * 2f);

                for (var a = 0; a < agents.Length; a++)
                {
                    var agent = agents[a];
                    if (!agent.IsAlive) continue;

                    var center = (Vector3)(agent.Position + new float3(0f, _bodyCenterHeight, 0f));
                    if (!bounds.Contains(center) || separated.Piece.SampleDistance(center) > _surfaceMargin) continue;

                    agent.IsAlive = false;
                    agent.IsDefeatedByCollapse = true;
                    agents[a] = agent;
                    defeatedCount++;
                }
            }

            return defeatedCount;
        }

        private void OnPieceSplit(VoxelPiece[] pieces)
        {
            for (var i = 1; i < pieces.Length; i++)
            {
                if (pieces[i].TryGetComponent<Rigidbody>(out var body))
                {
                    _separatedPieces.Add(new SeparatedPiece { Piece = pieces[i], Body = body });
                }
            }
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
            _separatedPieces.Clear();
        }

        /// <summary>
        /// ボクセルのピースから切り離された、Rigidbody を持つ塊。
        /// </summary>
        private struct SeparatedPiece
        {
            public VoxelPiece Piece;
            public Rigidbody Body;
        }
    }
}
