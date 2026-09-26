using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelPiece が融解・蒸発した分を受け取り、融解した粒として保持・更新・表示するコンポーネント。シーンに 1 つ置く。
    ///
    /// このコンポーネントを設定した VoxelPiece を登録し、ワールド空間の範囲でまとめて加熱できる。
    /// 温度は、常温を 0、融点を 1 とした値。
    ///
    /// 粒は重力で落ち、登録済みの VoxelPiece とは SDF で、それ以外の物とはレイキャストで当たりを取る。
    /// 粒同士はぶつからないが、密集している所では密度の格子を使って押し広げる。
    /// 温度が高いほど面をよく滑り、凝固点より冷えると止まり、蒸発点以上になると消える。
    /// 表示は、粒を滑らかにつないだ液面のメッシュか、粒ごとの球のどちらか。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    public sealed class VoxelMeltSystem : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const int JobBatchSize = 64;

        [SerializeField]
        [Tooltip("加熱・融解・蒸発・冷却と、粒の動きの設定")]
        private VoxelThermalSettings _settings;

        [SerializeField, Min(0)]
        [Tooltip("保持する粒の上限数。上限を超えた分の融解は粒にせず捨てる")]
        private int _maxParticles = 20000;

        [SerializeField]
        [Tooltip("粒が当たる、ボクセル以外の物のレイヤー")]
        private LayerMask _environmentLayers = ~0;

        [SerializeField]
        [Tooltip("粒がこの高さ（ワールド空間の Y）より下に落ちたら、蒸発させずに取り除く")]
        private float _destroyBelowY = -20f;

        [Header("表示")]
        [SerializeField]
        [Tooltip("粒の表示方法")]
        private VoxelMeltRenderMode _renderMode = VoxelMeltRenderMode.Surface;

        [SerializeField]
        [Tooltip("表示に使うマテリアル。GPU インスタンシングが無効なら、有効にした複製を使う")]
        private Material _particleMaterial;

        [SerializeField, Min(0.005f)]
        [Tooltip("液面を作る格子の大きさ（m）。小さいほど細かいが重い")]
        private float _surfaceVoxelSize = 0.02f;

        [SerializeField, Min(0.1f)]
        [Tooltip("液面を作るときに、粒の半径（体積の等しい球の半径）に掛ける倍率。大きいほど粒がつながりやすいが、見た目の体積が増える")]
        private float _surfaceRadiusScale = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("液面を作るときに、粒同士を滑らかにつなぐ幅（m）")]
        private float _surfaceBlend = 0.03f;

        [SerializeField]
        [Tooltip("表示方法が Spheres のときに使う、直径 1 のメッシュ。未設定なら球")]
        private Mesh _particleMesh;

        private readonly List<VoxelPiece> _pieces = new();
        private readonly List<VoxelEvaporation> _pendingEvaporations = new();
        private readonly ActionChannel<IReadOnlyList<VoxelEvaporation>> _evaporated = new();

        private NativeList<VoxelMeltParticle> _particles;
        private NativeArray<Matrix4x4> _matrices;
        private Mesh _renderMesh;
        private Material _renderMaterial;
        private bool _ownsRenderMaterial;
        private bool _hasWarnedParticleLimit;
        private VoxelMeltSurface _surface;

        public VoxelThermalSettings Settings => _settings;

        /// <summary> 液面の三角形数 </summary>
        public int SurfaceTriangleCount => _surface?.TriangleCount ?? 0;

        /// <summary> 直近の液面の作り直しにかかった時間 </summary>
        public double LastSurfaceBuildMilliseconds => _surface?.LastBuildMilliseconds ?? 0d;

        /// <summary> 面を持つ液面チャンクの数 </summary>
        public int SurfaceChunkCount => _surface?.ChunkCount ?? 0;

        /// <summary> 直近の液面の作り直しで作り直したチャンクの数 </summary>
        public int LastRebuiltSurfaceChunkCount => _surface?.LastRebuiltChunkCount ?? 0;

        /// <summary> このコンポーネントを設定し、有効になっている VoxelPiece </summary>
        public IReadOnlyList<VoxelPiece> Pieces => _pieces;

        /// <summary> 保持している粒の数 </summary>
        public int ParticleCount => _particles.IsCreated ? _particles.Length : 0;

        /// <summary> 凝固点より冷えて止まっている粒の数 </summary>
        public int FrozenCount { get; private set; }

        /// <summary> 保持している粒の体積の合計（ワールド空間, m³） </summary>
        public float FluidVolume { get; private set; }

        /// <summary> これまでに蒸発した体積の合計（ワールド空間, m³） </summary>
        public float EvaporatedVolume { get; private set; }

        /// <summary>
        /// 設定を差し替える。
        /// </summary>
        public void SetSettings(VoxelThermalSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// ワールド空間の形状と重なる、登録済みの全ての VoxelPiece と、形状の内側にある粒を加熱する。
        /// </summary>
        /// <param name="shape">加熱する範囲（ワールド空間）</param>
        /// <param name="amount">形状の中心側で加える温度。負なら冷やす</param>
        /// <param name="falloff">形状の境界から内側へ、加える温度を 0 から amount まで強めていく幅（ワールド空間, m）</param>
        public void ApplyHeat<TShape>(in TShape shape, float amount, float falloff)
            where TShape : struct, ITransformableVoxelShape<TShape>
        {
            var shapeBounds = shape.Bounds;
            var bounds = new Bounds();
            bounds.SetMinMax(shapeBounds.Min, shapeBounds.Max);

            foreach (var piece in _pieces)
            {
                if (piece.WorldBounds.Intersects(bounds)) piece.ApplyHeat(shape, amount, falloff, Space.World);
            }

            HeatParticles(shape, amount, falloff);
        }

        /// <summary>
        /// 粒や固体の一部が蒸発したときに呼ぶ処理を登録する。
        /// 蒸発したフレームの LateUpdate で、そのフレームに蒸発した分をまとめて 1 回呼ぶ。
        /// 引数のリストは呼び出しの間だけ有効で、呼び出し後に中身が変わる。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnEvaporated(Action<IReadOnlyList<VoxelEvaporation>> callback)
        {
            return _evaporated.Register(callback);
        }

        internal void Register(VoxelPiece piece)
        {
            if (!_pieces.Contains(piece)) _pieces.Add(piece);
        }

        internal void Unregister(VoxelPiece piece)
        {
            _pieces.Remove(piece);
        }

        /// <summary>
        /// 融解した分を受け取る。温度が蒸発点以上なら蒸発として扱い、そうでなければ粒にする。
        /// </summary>
        /// <param name="position">位置（ワールド空間）</param>
        /// <param name="temperature">温度</param>
        /// <param name="volume">体積（ワールド空間, m³）</param>
        internal void AddMelted(Vector3 position, float temperature, float volume)
        {
            if (volume <= 0f || !_particles.IsCreated) return;

            if (_settings != null && temperature >= _settings.EvaporationTemperature)
            {
                AddEvaporation(position, volume);
                return;
            }

            if (_particles.Length >= _maxParticles)
            {
                if (!_hasWarnedParticleLimit)
                {
                    Debug.LogWarning($"粒の数が上限 {_maxParticles} に達した為、以降の融解は粒にせず捨てます。", this);
                    _hasWarnedParticleLimit = true;
                }

                return;
            }

            _particles.Add(new VoxelMeltParticle
            {
                Position = position,
                Temperature = temperature,
                Volume = volume
            });
            FluidVolume += volume;
        }

        private void Awake()
        {
            _particles = new NativeList<VoxelMeltParticle>(1024, Allocator.Persistent);
            _surface = new VoxelMeltSurface();
            _renderMesh = _particleMesh != null ? _particleMesh : GetBuiltinSphereMesh();

            if (_particleMaterial != null)
            {
                _ownsRenderMaterial = !_particleMaterial.enableInstancing;
                _renderMaterial = _ownsRenderMaterial
                    ? new Material(_particleMaterial) { enableInstancing = true }
                    : _particleMaterial;
            }

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeNativeArrays;
#endif
        }

        private void Update()
        {
            if (_settings == null || !_particles.IsCreated || _particles.Length == 0) return;

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            Simulate(deltaTime);
        }

        private void LateUpdate()
        {
            FlushEvaporations();
            Draw();
        }

        private void OnValidate()
        {
            _surface?.MarkAllDirty();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DisposeNativeArrays;
#endif
            DisposeNativeArrays();

            if (_ownsRenderMaterial) Destroy(_renderMaterial);
        }

        /// <summary>
        /// NativeArray を解放する。
        /// 再生中にスクリプトが再コンパイルされると OnDestroy を経ずに C# 側の参照が失われ、解放漏れになる為、
        /// エディタではドメインリロードの直前にも呼ぶ。
        /// </summary>
        private void DisposeNativeArrays()
        {
            if (_particles.IsCreated) _particles.Dispose();
            if (_matrices.IsCreated) _matrices.Dispose();

            _surface?.Dispose();
            _surface = null;
        }

        /// <summary>
        /// 粒を 1 フレーム分進める。
        /// レイは進む前の位置から作り、冷却と移動の後に当たりを反映する。
        /// </summary>
        private void Simulate(float deltaTime)
        {
            var count = _particles.Length;
            var particles = _particles.AsArray();
            var commands = new NativeArray<RaycastCommand>(count, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var hits = new NativeArray<RaycastHit>(count, Allocator.TempJob);
            var evaporated = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var density = default(NativeParallelHashMap<int3, float>);
            var handle = default(JobHandle);

            // ジョブの実行中は粒の配列へ触れられない為、押し広げの格子の間隔はジョブを予約する前に求める
            var spreadCellSize = GetSpreadCellSize(count);
            if (_settings.SpreadSpeed > 0f && spreadCellSize > 0f)
            {
                density = new NativeParallelHashMap<int3, float>(count * 8, Allocator.TempJob);
            }

            try
            {
                ScheduleSimulation(particles, commands, hits, evaporated, density, spreadCellSize, deltaTime,
                    ref handle);
            }
            finally
            {
                // 予約の途中で例外が出ても、予約済みのジョブを必ず完了させる。
                // 完了させないと、以降のフレームで粒やボリュームの配列へ触れる処理が全て失敗し続ける
                handle.Complete();
            }

            if (density.IsCreated) density.Dispose();

            RemoveParticles(evaporated);

            commands.Dispose();
            hits.Dispose();
            evaporated.Dispose();
        }

        /// <summary>
        /// 粒を 1 フレーム分進めるジョブを順に予約する。handle は予約するたびに最後のジョブへ更新する。
        /// density が作られていなければ、押し広げは行わない。
        /// </summary>
        private void ScheduleSimulation(NativeArray<VoxelMeltParticle> particles,
            NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> hits, NativeArray<byte> evaporated,
            NativeParallelHashMap<int3, float> density, float spreadCellSize, float deltaTime, ref JobHandle handle)
        {
            var count = particles.Length;

            handle = new VoxelParticleRaycastBuildJob
            {
                Particles = particles,
                DeltaTime = deltaTime,
                Gravity = (float3)Physics.gravity * _settings.GravityScale,
                QueryParameters = new QueryParameters(_environmentLayers, false, QueryTriggerInteraction.Ignore, false),
                Commands = commands
            }.Schedule(count, JobBatchSize);

            handle = new VoxelParticleIntegrateJob
            {
                Particles = particles,
                DeltaTime = deltaTime,
                Gravity = (float3)Physics.gravity * _settings.GravityScale,
                MaxSpeed = _settings.MaxSpeed,
                CoolingPerSecond = _settings.ParticleCoolingPerSecond,
                FreezeTemperature = _settings.FreezeTemperature,
                EvaporationTemperature = _settings.EvaporationTemperature,
                Evaporated = evaporated
            }.Schedule(count, JobBatchSize, handle);

            handle = RaycastCommand.ScheduleBatch(commands, hits, JobBatchSize, 1, handle);

            handle = new VoxelParticleResolveHitJob
            {
                Particles = particles,
                Hits = hits,
                DeltaTime = deltaTime,
                HotDamping = _settings.HotTangentDamping,
                ColdDamping = _settings.ColdTangentDamping,
                FreezeTemperature = _settings.FreezeTemperature,
                EvaporationTemperature = _settings.EvaporationTemperature
            }.Schedule(count, JobBatchSize, handle);

            ScheduleCollisions(particles, deltaTime, ref handle);

            if (density.IsCreated) ScheduleSpread(particles, density, spreadCellSize, ref handle);
        }

        /// <summary>
        /// 粒の体積を密度の格子へ配り、密集している所の粒を押し広げるジョブを繋げる。
        /// handle は予約するたびに最後のジョブへ更新する。
        /// </summary>
        private void ScheduleSpread(NativeArray<VoxelMeltParticle> particles,
            NativeParallelHashMap<int3, float> density, float cellSize, ref JobHandle handle)
        {
            handle = new VoxelParticleDensityJob
            {
                Particles = particles,
                CellSize = cellSize,
                Density = density
            }.Schedule(handle);

            handle = new VoxelParticleSpreadJob
            {
                Particles = particles,
                Density = density,
                CellSize = cellSize,
                RestFill = _settings.SpreadRestFill,
                SpreadSpeed = _settings.SpreadSpeed,
                Seed = 0x9E3779B9u
            }.Schedule(particles.Length, JobBatchSize, handle);
        }

        /// <summary>
        /// 押し広げに使う密度の格子の間隔を、粒の平均の体積と等しい球の直径として求める。
        /// </summary>
        /// <returns>格子の間隔。粒が無い、または体積が 0 なら 0</returns>
        private float GetSpreadCellSize(int count)
        {
            var averageVolume = count > 0 ? FluidVolume / count : 0f;
            var cellSize = 2f * math.pow(math.max(averageVolume, 0f) * (3f / (4f * math.PI)), 1f / 3f);
            return cellSize > 1e-5f ? cellSize : 0f;
        }

        /// <summary>
        /// 登録済みの VoxelPiece ごとに、SDF との当たりを取るジョブを繋げる。
        /// 同じ粒の配列を書き換える為、ピースごとのジョブは並列にせず順に実行する。
        /// handle は予約するたびに最後のジョブへ更新する。
        /// </summary>
        private void ScheduleCollisions(NativeArray<VoxelMeltParticle> particles, float deltaTime,
            ref JobHandle handle)
        {
            foreach (var piece in _pieces)
            {
                var volume = piece.VolumeData;
                if (volume == null || !volume.IsCreated) continue;

                var pieceTransform = piece.transform;
                handle = new VoxelParticleCollideJob
                {
                    Particles = particles,
                    Samples = volume.Samples,
                    Layout = volume.Layout,
                    WorldToLocal = pieceTransform.worldToLocalMatrix,
                    LocalToWorld = pieceTransform.localToWorldMatrix,
                    Scale = pieceTransform.lossyScale.x,
                    DeltaTime = deltaTime,
                    HotDamping = _settings.HotTangentDamping,
                    ColdDamping = _settings.ColdTangentDamping,
                    FreezeTemperature = _settings.FreezeTemperature,
                    EvaporationTemperature = _settings.EvaporationTemperature
                }.Schedule(particles.Length, JobBatchSize, handle);
            }
        }

        /// <summary>
        /// 形状の内側にある粒を加熱する。
        /// </summary>
        private void HeatParticles<TShape>(in TShape shape, float amount, float falloff)
            where TShape : struct, IVoxelShape
        {
            if (_settings == null || !_particles.IsCreated || _particles.Length == 0) return;

            new VoxelParticleHeatJob<TShape>
            {
                Particles = _particles.AsArray(),
                Shape = shape,
                Amount = amount,
                Falloff = falloff,
                FreezeTemperature = _settings.FreezeTemperature
            }.Schedule(_particles.Length, JobBatchSize).Complete();
        }

        /// <summary>
        /// 蒸発した粒と、_destroyBelowY より下へ落ちた粒を取り除き、残った粒のうち止まっている数を数える。
        /// 落ちた粒は蒸発として扱わず、体積ごと捨てる。
        /// </summary>
        private void RemoveParticles(NativeArray<byte> evaporated)
        {
            FrozenCount = 0;

            // 末尾から見ることで、最後の粒を詰め替える RemoveAtSwapBack が、まだ見ていない粒を飛ばさないようにする
            for (var i = _particles.Length - 1; i >= 0; i--)
            {
                var particle = _particles[i];
                var isEvaporated = evaporated[i] != 0;
                if (isEvaporated || particle.Position.y < _destroyBelowY)
                {
                    if (isEvaporated) AddEvaporation(particle.Position, particle.Volume);

                    FluidVolume -= particle.Volume;
                    _particles.RemoveAtSwapBack(i);
                    _surface?.MarkRemoved(particle);
                    continue;
                }

                if (particle.IsFrozen) FrozenCount++;
            }
        }

        private void AddEvaporation(Vector3 position, float volume)
        {
            _pendingEvaporations.Add(new VoxelEvaporation(position, volume));
            EvaporatedVolume += volume;
        }

        private void FlushEvaporations()
        {
            if (_pendingEvaporations.Count == 0) return;

            _evaporated.Invoke(_pendingEvaporations);
            _pendingEvaporations.Clear();
        }

        private void Draw()
        {
            if (_renderMaterial == null) return;

            switch (_renderMode)
            {
                case VoxelMeltRenderMode.Surface:
                    DrawSurface();
                    break;

                case VoxelMeltRenderMode.Spheres:
                    DrawParticles();
                    break;
            }
        }

        /// <summary>
        /// 動いている粒・消えた粒・設定の変化で作り直しが必要な液面チャンクだけを作り直し、全チャンクを描く。
        /// </summary>
        private void DrawSurface()
        {
            if (_surface == null || !_particles.IsCreated) return;

            _surface.Configure(_surfaceVoxelSize, _surfaceRadiusScale, _surfaceBlend);
            _surface.Update(_particles.AsArray(), FrozenCount < _particles.Length);

            if (_surface.TriangleCount == 0) return;

            _surface.Draw(new RenderParams(_renderMaterial)
            {
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            });
        }

        /// <summary>
        /// 全ての粒を、体積の等しい球として GPU インスタンシングで描く。
        /// </summary>
        private void DrawParticles()
        {
            if (!_particles.IsCreated || _particles.Length == 0 || _renderMesh == null)
            {
                return;
            }

            var count = _particles.Length;
            if (!_matrices.IsCreated || _matrices.Length < count)
            {
                if (_matrices.IsCreated) _matrices.Dispose();
                _matrices = new NativeArray<Matrix4x4>(math.ceilpow2(count), Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            var bounds = new NativeArray<float3>(2, Allocator.TempJob);
            new VoxelMeltParticleMatrixJob
            {
                Particles = _particles.AsArray(),
                Matrices = _matrices,
                Bounds = bounds
            }.Schedule().Complete();

            var worldBounds = new Bounds();
            worldBounds.SetMinMax(bounds[0], bounds[1]);
            bounds.Dispose();

            var renderParams = new RenderParams(_renderMaterial)
            {
                layer = gameObject.layer,
                worldBounds = worldBounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            };

            for (var start = 0; start < count; start += MaxInstancesPerDraw)
            {
                Graphics.RenderMeshInstanced(renderParams, _renderMesh, 0, _matrices,
                    math.min(MaxInstancesPerDraw, count - start), start);
            }
        }

        private static Mesh GetBuiltinSphereMesh()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
            return mesh;
        }
    }
}
