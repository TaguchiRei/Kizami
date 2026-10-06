using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体を貸していない敵の状態から部位ごとの行列を Job で作り、Graphics.RenderMeshInstanced で部位ごとにまとめて描画する。体から外れた部位は描かない。
    /// 部位のメッシュ・マテリアル・体の根から見た位置は、体のプレハブから初期化のときに読む。
    /// </summary>
    /// <remarks>
    /// マテリアルは GPU インスタンシングを有効にしておく必要がある。
    /// プロジェクトはインスタンシングのバリアントを使われていなければ削る設定なので、実行中に有効にしてもビルドでは描けない為。
    /// </remarks>
    public sealed class EnemyCrowdRenderer : IDisposable
    {
        /// <summary>
        /// 1 回の描画で出せるインスタンスの数。上限の 1023 は uniform scaling を仮定するシェーダーの値で、URP Lit は行列を 2 つ送るので 511 になる。
        /// Unity は自動で分けないので、これを超える分は描画を分ける
        /// </summary>
        private const int MAX_INSTANCES_PER_DRAW = 511;

        private readonly Mesh[] _meshes;

        /// <summary> 部位ごとの、サブメッシュごとの描画の設定 </summary>
        private readonly RenderParams[][] _renderParams;

        /// <summary> 部位ごとの、体の根から見た部位の行列 </summary>
        private NativeArray<float4x4> _partLocalMatrices;

        /// <summary> 部位ごとに、敵の数の分の区画を持つ行列。部位 p の区画は p × 敵の数から始まる </summary>
        private NativeArray<float4x4> _matrices;

        /// <summary> 部位ごとの、区画に書いた行列の数 </summary>
        private NativeArray<int> _counts;

        /// <param name="bodyPrefab">部位のメッシュ・マテリアル・位置を読む体のプレハブ</param>
        /// <param name="capacity">敵の状態の数</param>
        public EnemyCrowdRenderer(EnemyBody bodyPrefab, int capacity)
        {
            var parts = bodyPrefab.Parts;
            var rootWorldToLocal = bodyPrefab.transform.worldToLocalMatrix;

            _meshes = new Mesh[parts.Count];
            _renderParams = new RenderParams[parts.Count][];
            _partLocalMatrices = new NativeArray<float4x4>(parts.Count, Allocator.Persistent);
            _matrices = new NativeArray<float4x4>(parts.Count * capacity, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            _counts = new NativeArray<int>(parts.Count, Allocator.Persistent);

            for (var i = 0; i < parts.Count; i++)
            {
                var cuttable = parts[i].Cuttable;
                var meshFilter = cuttable.GetComponent<MeshFilter>();
                var meshRenderer = cuttable.GetComponent<MeshRenderer>();

                _meshes[i] = meshFilter.sharedMesh;
                _partLocalMatrices[i] = rootWorldToLocal * cuttable.transform.localToWorldMatrix;

                var materials = meshRenderer.sharedMaterials;
                _renderParams[i] = new RenderParams[materials.Length];
                for (var s = 0; s < materials.Length; s++)
                {
                    _renderParams[i][s] = new RenderParams(materials[s])
                    {
                        layer = cuttable.gameObject.layer,
                        shadowCastingMode = meshRenderer.shadowCastingMode,
                        receiveShadows = meshRenderer.receiveShadows
                    };
                }
            }
        }

        /// <summary>
        /// ステージに出ていて体を貸していない敵の、残っている部位を、このフレームの描画に出す。
        /// </summary>
        public void Render(NativeArray<EnemyAgent> agents)
        {
            new BuildMatricesJob
            {
                Agents = agents,
                PartLocalMatrices = _partLocalMatrices,
                Matrices = _matrices,
                Counts = _counts
            }.Schedule(_meshes.Length, 1).Complete();

            var instanceData = _matrices.Reinterpret<Matrix4x4>();

            for (var part = 0; part < _meshes.Length; part++)
            {
                var regionStart = part * agents.Length;
                var count = _counts[part];

                for (var offset = 0; offset < count; offset += MAX_INSTANCES_PER_DRAW)
                {
                    var drawCount = math.min(MAX_INSTANCES_PER_DRAW, count - offset);

                    for (var s = 0; s < _renderParams[part].Length; s++)
                    {
                        Graphics.RenderMeshInstanced(_renderParams[part][s], _meshes[part], s, instanceData,
                            drawCount, regionStart + offset);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_partLocalMatrices.IsCreated) _partLocalMatrices.Dispose();
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_counts.IsCreated) _counts.Dispose();
        }

        /// <summary>
        /// 部位 1 つにつき 1 回呼ばれ、描く敵のその部位の行列を、部位の区画に詰めて書く。
        /// </summary>
        [BurstCompile]
        private struct BuildMatricesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EnemyAgent> Agents;
            [ReadOnly] public NativeArray<float4x4> PartLocalMatrices;

            /// <summary> 部位ごとの区画には、その部位の Execute だけが書く </summary>
            [NativeDisableParallelForRestriction] public NativeArray<float4x4> Matrices;

            public NativeArray<int> Counts;

            public void Execute(int part)
            {
                var regionStart = part * Agents.Length;
                var local = PartLocalMatrices[part];
                var count = 0;

                var partBit = 1u << part;

                for (var i = 0; i < Agents.Length; i++)
                {
                    var agent = Agents[i];
                    if (!agent.IsAlive || agent.BodyIndex >= 0 || (agent.LostParts & partBit) != 0) continue;

                    var root = float4x4.TRS(agent.Position, quaternion.RotateY(agent.Yaw), new float3(1f));
                    Matrices[regionStart + count] = math.mul(root, local);
                    count++;
                }

                Counts[part] = count;
            }
        }
    }
}
