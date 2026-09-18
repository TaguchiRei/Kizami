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
    /// 滑らかなボクセル（SDF）で表した、かけら 1 つ分のコンポーネント。
    ///
    /// ボリュームを保持し、編集されたチャンクのメッシュとコライダーを作り直す。
    /// 削られて内側が複数の塊に分かれたら、最も大きい塊を残し、
    /// 他の塊を Rigidbody 付きの新しい VoxelPiece として切り離す。
    ///
    /// 加熱されて融点以上になった部分は取り除き、VoxelMeltSystem へ融解した粒として渡す。
    ///
    /// ボクセル空間はこの Transform のローカル空間。Transform のスケールは均一である前提。
    /// 編集後の処理（冷却 → 体積の計測 → 分離 → コールバック → 再メッシュ化）は、編集したフレームの LateUpdate でまとめて行う。
    /// </summary>
    public sealed class VoxelPiece : MonoBehaviour
    {
        private const MeshColliderCookingOptions ColliderCookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;

        [Header("形状")]
        [SerializeField]
        [Tooltip("ボクセルの大きさ・チャンクの大きさ・再メッシュ化の上限")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("チャンクの MeshRenderer に割り当てるマテリアル")]
        private Material _material;

        [SerializeField]
        [Tooltip("当たり判定の作り方。動く Rigidbody を付けるなら ConvexHull にする")]
        private VoxelColliderMode _colliderMode = VoxelColliderMode.ChunkMesh;

        [Header("分離")]
        [SerializeField]
        [Tooltip("削られて分かれた塊を、別の VoxelPiece として切り離すか")]
        private bool _splittable = true;

        [SerializeField, Min(1)]
        [Tooltip("内側サンプルがこの数より少ない塊は、切り離さずに消す")]
        private int _minPieceSamples = 8;

        [SerializeField, Min(0.001f)]
        [Tooltip("切り離したピースの密度（kg/m³）。質量は 体積 × 密度")]
        private float _density = 500f;

        [SerializeField]
        [Tooltip("切り離したピースがこの高さ（ワールド空間の Y）より下に落ちたら破棄する")]
        private float _destroyBelowY = -20f;

        [Header("融解")]
        [SerializeField]
        [Tooltip("融解・蒸発した分の送り先。未設定なら加熱しても何も起きない")]
        private VoxelMeltSystem _meltSystem;

        private readonly Queue<int> _dirtyQueue = new();
        private readonly List<VoxelShapeChange> _pendingShapeChanges = new();
        private readonly ActionChannel<VoxelShapeChange> _shapeChanged = new();
        private readonly ActionChannel<VoxelPiece[]> _split = new();
        private readonly ActionChannel<VoxelPiece> _destroyed = new();

        private VoxelVolume _volume;
        private ChunkSlot[] _chunks;
        private bool[] _isDirty;
        private MeshCollider _hullCollider;
        private Mesh _hullMesh;
        private bool _needsMeasure;
        private bool _needsSplitCheck;
        private bool _hasInitialSampleCount;
        private bool _meltedSinceMeasure;
        private bool _hasWarnedMissingThermalSettings;

        public VoxelQualitySettings Quality => _quality;
        public Material Material => _material;
        public VoxelColliderMode ColliderMode => _colliderMode;
        public bool IsSplittable => _splittable;
        public VoxelMeltSystem MeltSystem => _meltSystem;

        /// <summary> 粒との当たり判定などで SDF を直接読む為のボリューム。まだ作られていなければ null </summary>
        internal VoxelVolume VolumeData => _volume;

        /// <summary> ボクセル 1 つの一辺の長さ（ワールド空間, m）。ボリュームが無ければ 0 </summary>
        public float VoxelSize => _volume != null ? _volume.Layout.VoxelSize * transform.lossyScale.x : 0f;

        /// <summary> 内側（距離が負）のサンプル数 </summary>
        public int SampleCount { get; private set; }

        /// <summary> このピースができた時点の内側サンプル数 </summary>
        public int InitialSampleCount { get; private set; }

        /// <summary> 体積（ワールド空間, m³）。内側サンプル数 × ボクセル 1 つの体積 </summary>
        public float Volume => SampleCount * VoxelCubicVolume;

        /// <summary> このピースができた時点の体積（ワールド空間, m³） </summary>
        public float InitialVolume => InitialSampleCount * VoxelCubicVolume;

        /// <summary> このピースができた時点の体積に対する、今の体積の割合 </summary>
        public float RelativeVolume => InitialSampleCount > 0 ? (float)SampleCount / InitialSampleCount : 0f;

        /// <summary> 直接の分離元。分離で生まれたピースでなければ null。分離元が破棄済みなら null と等しくなる </summary>
        public VoxelPiece Parent { get; private set; }

        /// <summary> 分離をさかのぼった最初のピース。分離で生まれたピースでなければ自分自身 </summary>
        public VoxelPiece Root { get; private set; }

        /// <summary> 何回目の分離で生まれたか。最初のピースは 0 </summary>
        public int Generation { get; private set; }

        /// <summary> 元をたどると属する VoxelModelLoader。モデルから読み込んだものでなければ null </summary>
        public VoxelModelLoader Model { get; private set; }

        /// <summary> 元をたどると属するモデルのパーツの、VoxelModelLoader からの相対パス。モデルと無関係なら null </summary>
        public string PartPath { get; private set; }

        /// <summary> 表示中の表面を囲むワールド空間の範囲。面が無ければ位置だけを持つ大きさ 0 の範囲 </summary>
        public Bounds WorldBounds
        {
            get
            {
                var bounds = new Bounds(transform.position, Vector3.zero);
                if (_chunks == null) return bounds;

                var hasBounds = false;
                foreach (var slot in _chunks)
                {
                    if (slot == null || slot.TriangleCount == 0) continue;

                    if (hasBounds)
                    {
                        bounds.Encapsulate(slot.Renderer.bounds);
                    }
                    else
                    {
                        bounds = slot.Renderer.bounds;
                        hasBounds = true;
                    }
                }

                return bounds;
            }
        }

        /// <summary> 再メッシュ化を待っているチャンク数 </summary>
        public int PendingChunkCount => _dirtyQueue.Count;

        /// <summary> 全チャンクの三角形数の合計 </summary>
        public int TriangleCount { get; private set; }

        /// <summary> 直近の再メッシュ化とコライダー更新にかかった時間 </summary>
        public double LastRemeshMilliseconds { get; private set; }

        private float VoxelCubicVolume
        {
            get
            {
                var size = VoxelSize;
                return size * size * size;
            }
        }

        /// <summary>
        /// 品質設定を差し替える。ボリュームを作る・読み込むより前に呼ぶこと。
        /// </summary>
        public void SetQuality(VoxelQualitySettings quality)
        {
            _quality = quality;
        }

        /// <summary>
        /// チャンクの MeshRenderer に割り当てるマテリアルを差し替える。これ以降に生成するチャンクから反映される。
        /// </summary>
        public void SetMaterial(Material material)
        {
            _material = material;
        }

        /// <summary>
        /// 当たり判定の作り方を差し替える。ボリュームを作る・読み込むより前に呼ぶこと。
        /// </summary>
        public void SetColliderMode(VoxelColliderMode colliderMode)
        {
            _colliderMode = colliderMode;
        }

        /// <summary>
        /// 削られて分かれた塊を、別のピースとして切り離すかを切り替える。
        /// </summary>
        public void SetSplittable(bool splittable)
        {
            _splittable = splittable;
        }

        /// <summary>
        /// 融解・蒸発した分の送り先を差し替える。
        /// </summary>
        public void SetMeltSystem(VoxelMeltSystem meltSystem)
        {
            if (_meltSystem == meltSystem) return;

            if (isActiveAndEnabled && _meltSystem != null) _meltSystem.Unregister(this);
            _meltSystem = meltSystem;
            if (isActiveAndEnabled && _meltSystem != null) _meltSystem.Register(this);
        }

        /// <summary>
        /// ローカル空間の境界を覆う空のボリュームを作る。既存のボリュームとチャンクは破棄する。
        /// ボクセルの大きさは、品質設定のワールド空間の長さをこの Transform のスケールで割ったもの。
        /// 初期の体積は、この後の編集を反映した最初の LateUpdate で測る。
        /// </summary>
        /// <param name="localBounds">ボリュームで覆うローカル空間の範囲</param>
        public void CreateVolume(VoxelBounds localBounds)
        {
            if (!TryCreateLayout(localBounds, out var layout)) return;

            AssignVolume(new VoxelVolume(layout));
        }

        /// <summary>
        /// 事前ベイクした SDF を、品質設定のボクセルの大きさへリサンプルして読み込む。
        /// 既存のボリュームとチャンクは破棄し、全チャンクを再メッシュ化の対象にする。体積はこの場で測る。
        /// </summary>
        /// <param name="source">この Transform のローカル空間でベイクした SDF</param>
        public void LoadSdf(VoxelSdfData source)
        {
            if (!TryCreateLayout(source.LocalBounds, out var layout)) return;

            if (source.MaxDistance < layout.VoxelSize * 2f)
            {
                Debug.LogWarning(
                    $"ベイク時の最大距離 {source.MaxDistance} がボクセル 2 つ分 {layout.VoxelSize * 2f} より短い為、法線が荒れます。" +
                    "最大距離を大きくしてベイクし直してください。", this);
            }

            var volume = new VoxelVolume(layout);
            volume.Resample(source);
            AssignVolume(volume);
            MarkAllDirty();
            Measure(false);
        }

        /// <summary>
        /// ローカル空間で表した形状をボリュームへ CSG 合成し、影響するチャンクを再メッシュ化の対象にする。
        /// </summary>
        /// <param name="localShape">このピースのローカル空間で表した形状</param>
        /// <param name="operation">埋めるか削るか</param>
        public void ApplyEdit<TShape>(in TShape localShape, VoxelCsgOperation operation)
            where TShape : struct, IVoxelShape
        {
            if (_volume == null)
            {
                Debug.LogError("ボリュームがありません。先に CreateVolume か LoadSdf を呼んでください。", this);
                return;
            }

            var result = _volume.ApplyEdit(localShape, operation);
            if (!result.HasChange) return;

            MarkSamplesDirty(result.SampleMin, result.SampleMax);
            _needsMeasure = true;
            if (operation == VoxelCsgOperation.Subtract) _needsSplitCheck = true;

            QueueShapeChange(operation, VoxelShapeChangeCause.Edit, result);
        }

        /// <summary>
        /// 指定した空間で表した形状をボリュームへ CSG 合成する。
        /// Space.World ならこの Transform のローカル空間へ変換してから合成する。
        /// </summary>
        /// <param name="shape">space で表した形状</param>
        /// <param name="operation">埋めるか削るか</param>
        /// <param name="space">shape を表している空間</param>
        public void ApplyEdit<TShape>(in TShape shape, VoxelCsgOperation operation, Space space)
            where TShape : struct, ITransformableVoxelShape<TShape>
        {
            var localShape = space == Space.World ? shape.Transformed(transform.worldToLocalMatrix) : shape;
            ApplyEdit(localShape, operation);
        }

        /// <summary>
        /// ローカル空間で表した形状の内側を加熱する。
        /// 温度が融点（1）以上になった部分は固体から取り除き、同じ体積を融解した粒として VoxelMeltSystem へ渡す。
        /// 取り除いた時点の温度が蒸発点以上なら、粒にせず蒸発させる。
        /// 融解で分かれた塊は削ったときと同じく切り離し、切り離すには小さすぎる塊は粒にする。
        /// VoxelMeltSystem か、その VoxelThermalSettings が未設定なら何もしない。
        /// </summary>
        /// <param name="localShape">このピースのローカル空間で表した、加熱する範囲</param>
        /// <param name="amount">形状の中心側で加える温度。負なら冷やす。温度は 0 未満にならない</param>
        /// <param name="falloff">形状の境界から内側へ、加える温度を 0 から amount まで強めていく幅（ローカル空間）。0 なら内側全体に amount を加える</param>
        public void ApplyHeat<TShape>(in TShape localShape, float amount, float falloff)
            where TShape : struct, IVoxelShape
        {
            if (_volume == null)
            {
                Debug.LogError("ボリュームがありません。先に CreateVolume か LoadSdf を呼んでください。", this);
                return;
            }

            if (!HasThermalSettings())
            {
                if (!_hasWarnedMissingThermalSettings)
                {
                    Debug.LogWarning("VoxelMeltSystem または VoxelThermalSettings が設定されていない為、加熱を無視します。", this);
                    _hasWarnedMissingThermalSettings = true;
                }

                return;
            }

            var meltedSamples = new NativeList<int>(Allocator.TempJob);
            try
            {
                var result = _volume.ApplyHeat(localShape, amount, falloff, meltedSamples);
                if (meltedSamples.Length > 0)
                {
                    EmitMelted(meltedSamples.AsArray());
                    _meltedSinceMeasure = true;
                    _needsMeasure = true;
                    _needsSplitCheck = true;
                }

                if (!result.HasChange) return;

                MarkSamplesDirty(result.SampleMin, result.SampleMax);
                QueueShapeChange(VoxelCsgOperation.Subtract, VoxelShapeChangeCause.Melt, result);
            }
            finally
            {
                meltedSamples.Dispose();
            }
        }

        /// <summary>
        /// 指定した空間で表した形状の内側を加熱する。
        /// Space.World なら、形状と falloff をこの Transform のローカル空間へ変換してから加熱する。
        /// </summary>
        /// <param name="shape">space で表した、加熱する範囲</param>
        /// <param name="amount">形状の中心側で加える温度。負なら冷やす</param>
        /// <param name="falloff">形状の境界から内側へ、加える温度を 0 から amount まで強めていく幅（space の長さ）</param>
        /// <param name="space">shape と falloff を表している空間</param>
        public void ApplyHeat<TShape>(in TShape shape, float amount, float falloff, Space space)
            where TShape : struct, ITransformableVoxelShape<TShape>
        {
            if (space == Space.World)
            {
                ApplyHeat(shape.Transformed(transform.worldToLocalMatrix), amount, WorldToLocalLength(falloff));
                return;
            }

            ApplyHeat(shape, amount, falloff);
        }

        /// <summary>
        /// 再メッシュ化を待っている全チャンクを、1 フレームあたりの上限を無視して今すぐ作り直す。
        /// </summary>
        public void FlushRemesh()
        {
            if (_volume == null || _dirtyQueue.Count == 0) return;

            RemeshPending(_dirtyQueue.Count);
        }

        /// <summary>
        /// ワールド空間の位置における、表面までの符号付き距離（ワールド空間, m）。負なら内側。
        /// 表面から切り詰め距離（ボクセル 4 つ分）より離れた位置では、その距離で頭打ちになる。
        /// ボリュームが無ければ正の無限大。
        /// </summary>
        public float SampleDistance(Vector3 worldPosition)
        {
            if (_volume == null) return float.PositiveInfinity;

            var localPosition = transform.InverseTransformPoint(worldPosition);
            return _volume.SampleDistance(localPosition) * transform.lossyScale.x;
        }

        /// <summary>
        /// ワールド空間の位置がピースの内側か。
        /// </summary>
        public bool Contains(Vector3 worldPosition)
        {
            return SampleDistance(worldPosition) < 0f;
        }

        /// <summary>
        /// ワールド空間の位置の温度をトリリニア補間で求める。格子の外と、加熱されたことが無いピースでは 0。
        /// </summary>
        public float SampleTemperature(Vector3 worldPosition)
        {
            return _volume != null ? _volume.SampleTemperature(transform.InverseTransformPoint(worldPosition)) : 0f;
        }

        /// <summary>
        /// 全チャンクのメッシュを 1 つにまとめて mesh へ書き込む。頂点はこのピースのローカル空間。
        /// 反映待ちの編集があれば、先に反映する。
        /// </summary>
        /// <param name="mesh">書き込み先。中身は置き換える</param>
        public void BakeMesh(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            FlushRemesh();

            var combine = new List<CombineInstance>();
            if (_chunks != null)
            {
                foreach (var slot in _chunks)
                {
                    if (slot == null || slot.TriangleCount == 0) continue;

                    combine.Add(new CombineInstance { mesh = slot.Mesh, transform = Matrix4x4.identity });
                }
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(combine.ToArray(), true, true);
        }

        /// <summary>
        /// 削る・盛る編集や、加熱による融解で形状が変わったときに呼ぶ処理を登録する。
        /// 編集したフレームの LateUpdate で、ApplyEdit・ApplyHeat 1 回につき 1 回呼ぶ。呼ばれた時点で体積は編集後の値になっている。
        /// 分離で塊が取り除かれた変化は含まない（RegisterOnSplit で通知する）。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnShapeChanged(Action<VoxelShapeChange> callback)
        {
            return _shapeChanged.Register(callback);
        }

        /// <summary>
        /// 削られた結果、塊が分離したときに呼ぶ処理を登録する。
        /// 引数の 0 番目はこのピース、1 番目以降は切り離された新しいピース。
        /// 呼ばれた時点で、新しいピースのメッシュとコライダーはできている。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnSplit(Action<VoxelPiece[]> callback)
        {
            return _split.Register(callback);
        }

        /// <summary>
        /// このピースが破棄されるときに呼ぶ処理を登録する。落下して消えたときも呼ぶ。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnDestroyed(Action<VoxelPiece> callback)
        {
            return _destroyed.Register(callback);
        }

        /// <summary>
        /// ワールド空間の長さをローカル空間の長さに変換する。
        /// </summary>
        public float WorldToLocalLength(float worldLength)
        {
            return worldLength / transform.lossyScale.x;
        }

        /// <summary>
        /// VoxelModelLoader のパーツとして読み込まれたことを記録する。
        /// </summary>
        internal void BindToModel(VoxelModelLoader model, string partPath)
        {
            Model = model;
            PartPath = partPath;
        }

        private void Awake()
        {
            Root = this;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeVolumeBeforeAssemblyReload;
#endif
        }

        private void OnEnable()
        {
            if (_meltSystem != null) _meltSystem.Register(this);
        }

        private void OnDisable()
        {
            if (_meltSystem != null) _meltSystem.Unregister(this);
        }

        private void LateUpdate()
        {
            if (_volume == null) return;

            if (Generation > 0 && transform.position.y < _destroyBelowY)
            {
                Destroy(gameObject);
                return;
            }

            if (_volume.HasHotChunks && HasThermalSettings())
            {
                _volume.Cool(_meltSystem.Settings.SolidCoolingPerSecond * Time.deltaTime);
            }

            VoxelPiece[] splitPieces = null;
            if (_needsMeasure)
            {
                splitPieces = Measure(_needsSplitCheck && _splittable);
                _needsMeasure = false;
                _needsSplitCheck = false;
                _meltedSinceMeasure = false;
            }

            InvokePendingShapeChanges();

            if (splitPieces != null)
            {
                _split.Invoke(splitPieces);
                if (Model != null) Model.NotifySplit(splitPieces);
            }

            if (_volume != null && _dirtyQueue.Count > 0)
            {
                RemeshPending(_quality.RemeshChunksPerFrame);
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DisposeVolumeBeforeAssemblyReload;
#endif
            _destroyed.Invoke(this);
            if (Model != null) Model.NotifyDestroyed(this);

            ReleaseVolume();

            if (_hullMesh != null) Destroy(_hullMesh);
        }

#if UNITY_EDITOR
        /// <summary>
        /// ボリュームの NativeArray を解放する。
        /// 再生中にスクリプトが再コンパイルされると OnDestroy を経ずに C# 側の参照が失われ、解放漏れになる為、
        /// ドメインリロードの直前に呼ぶ。
        /// </summary>
        private void DisposeVolumeBeforeAssemblyReload()
        {
            _volume?.Dispose();
            _volume = null;
        }
#endif

        /// <summary>
        /// 内側のサンプルを塊に分けて体積を測る。allowSplit なら、最も大きい塊以外を切り離す。
        /// 小さすぎる塊は、切り離さずに消す。前回の計測から融解していれば、消す塊は粒として VoxelMeltSystem へ渡す。
        /// </summary>
        /// <returns>分離したピース（0 番目が自分）。新しいピースが無ければ null</returns>
        private VoxelPiece[] Measure(bool allowSplit)
        {
            var labels = new NativeArray<int>(_volume.Layout.SampleTotal, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var components = new NativeList<VoxelComponent>(Allocator.TempJob);

            try
            {
                _volume.LabelComponents(labels, components);

                if (!allowSplit || components.Length <= 1)
                {
                    var total = 0;
                    for (var i = 0; i < components.Length; i++)
                    {
                        total += components[i].SampleCount;
                    }

                    SetMeasuredSampleCount(total);
                    return null;
                }

                var keepIndex = 0;
                for (var i = 1; i < components.Length; i++)
                {
                    if (components[i].SampleCount > components[keepIndex].SampleCount) keepIndex = i;
                }

                var pieces = new List<VoxelPiece> { this };
                for (var i = 0; i < components.Length; i++)
                {
                    if (i == keepIndex) continue;

                    var component = components[i];
                    if (component.SampleCount >= _minPieceSamples)
                    {
                        pieces.Add(SpawnPiece(_volume.ExtractComponent(labels, component), component.SampleCount));
                    }
                    else if (_meltedSinceMeasure)
                    {
                        EmitComponentAsMelted(labels, component);
                    }

                    _volume.EraseComponent(labels, component);
                    MarkSamplesDirty(component.SampleMin, component.SampleMax);
                }

                SetMeasuredSampleCount(components[keepIndex].SampleCount);
                FlushRemesh();

                return pieces.Count > 1 ? pieces.ToArray() : null;
            }
            finally
            {
                labels.Dispose();
                components.Dispose();
            }
        }

        /// <summary>
        /// 取り出したボリュームを持つピースを、この Transform と同じ位置・向き・大きさで生成する。
        /// 分離の設定と系譜を引き継ぎ、当たり判定は凸包にする。元に Rigidbody があれば、その速度を引き継ぐ。
        /// </summary>
        private VoxelPiece SpawnPiece(VoxelVolume volume, int sampleCount)
        {
            var baseName = Root != null ? Root.name : name;
            var pieceObject = new GameObject($"{baseName}_Piece")
            {
                layer = gameObject.layer
            };
            pieceObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            pieceObject.transform.localScale = transform.lossyScale;

            var piece = pieceObject.AddComponent<VoxelPiece>();
            piece._quality = _quality;
            piece._material = _material;
            piece._colliderMode = VoxelColliderMode.ConvexHull;
            piece._splittable = _splittable;
            piece._minPieceSamples = _minPieceSamples;
            piece._density = _density;
            piece._destroyBelowY = _destroyBelowY;
            piece.SetMeltSystem(_meltSystem);
            piece.Parent = this;
            piece.Root = Root;
            piece.Generation = Generation + 1;
            piece.Model = Model;
            piece.PartPath = PartPath;

            piece.AssignVolume(volume);
            piece.MarkAllDirty();
            piece.SetMeasuredSampleCount(sampleCount);

            // Rigidbody より先に凸包コライダーを作っておき、質量の中心と慣性を形状から求めさせる
            piece.FlushRemesh();

            var body = pieceObject.AddComponent<Rigidbody>();
            body.mass = math.max(piece.Volume * _density, 0.01f);
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var sourceBody = GetComponentInParent<Rigidbody>();
            if (sourceBody != null)
            {
                body.linearVelocity = sourceBody.GetPointVelocity(piece.WorldBounds.center);
                body.angularVelocity = sourceBody.angularVelocity;
            }

            return piece;
        }

        private void SetMeasuredSampleCount(int sampleCount)
        {
            SampleCount = sampleCount;
            if (_hasInitialSampleCount) return;

            InitialSampleCount = sampleCount;
            _hasInitialSampleCount = true;
        }

        private bool HasThermalSettings()
        {
            return _meltSystem != null && _meltSystem.Settings != null;
        }

        /// <summary>
        /// サンプルを粒 1 個分ずつにまとめ、ワールド空間の位置・温度・体積を VoxelMeltSystem へ渡す。
        /// </summary>
        private void EmitMelted(NativeArray<int> sampleIndices)
        {
            if (!HasThermalSettings()) return;

            var groups = new NativeList<VoxelMeltGroup>(Allocator.TempJob);
            _volume.GroupSamples(sampleIndices, _meltSystem.Settings.ParticleCoarseness, groups);

            var sampleVolume = VoxelCubicVolume;
            foreach (var group in groups)
            {
                _meltSystem.AddMelted(transform.TransformPoint(group.LocalPosition), group.Temperature,
                    group.SampleCount * sampleVolume);
            }

            groups.Dispose();
        }

        /// <summary>
        /// 1 つの塊の全サンプルを、融解した粒として VoxelMeltSystem へ渡す。塊はボリュームに残る。
        /// </summary>
        private void EmitComponentAsMelted(NativeArray<int> labels, in VoxelComponent component)
        {
            var sampleIndices = new NativeList<int>(component.SampleCount, Allocator.TempJob);
            _volume.CollectComponentSamples(labels, component, sampleIndices);
            EmitMelted(sampleIndices.AsArray());
            sampleIndices.Dispose();
        }

        private void QueueShapeChange(VoxelCsgOperation operation, VoxelShapeChangeCause cause,
            in VoxelEditResult result)
        {
            var layout = _volume.Layout;
            var localBounds = new Bounds();
            localBounds.SetMinMax(layout.ToLocalPosition(result.SampleMin), layout.ToLocalPosition(result.SampleMax));
            _pendingShapeChanges.Add(new VoxelShapeChange(this, operation, cause, localBounds));
        }

        private void InvokePendingShapeChanges()
        {
            if (_pendingShapeChanges.Count == 0) return;

            var changes = _pendingShapeChanges.ToArray();
            _pendingShapeChanges.Clear();

            foreach (var change in changes)
            {
                _shapeChanged.Invoke(change);
                if (Model != null) Model.NotifyShapeChanged(change);
            }
        }

        private bool TryCreateLayout(VoxelBounds localBounds, out VoxelGridLayout layout)
        {
            if (_quality == null)
            {
                Debug.LogError("VoxelQualitySettings が設定されていません。", this);
                layout = default;
                return false;
            }

            layout = VoxelGridLayout.FromBounds(localBounds, WorldToLocalLength(_quality.VoxelSize),
                _quality.ChunkSize);
            return true;
        }

        /// <summary>
        /// 既存のボリュームとチャンクを破棄し、新しいボリュームに差し替える。体積と編集の待ちも初期化する。
        /// </summary>
        private void AssignVolume(VoxelVolume volume)
        {
            ReleaseVolume();
            _volume = volume;
            _chunks = new ChunkSlot[volume.Layout.ChunkTotal];
            _isDirty = new bool[volume.Layout.ChunkTotal];

            SampleCount = 0;
            InitialSampleCount = 0;
            _hasInitialSampleCount = false;
            _needsMeasure = false;
            _needsSplitCheck = false;
            _meltedSinceMeasure = false;
            _pendingShapeChanges.Clear();
        }

        private void MarkAllDirty()
        {
            for (var i = 0; i < _volume.Layout.ChunkTotal; i++)
            {
                MarkDirty(i);
            }
        }

        /// <summary>
        /// サンプル範囲 [sampleMin, sampleMax] の変更で影響を受けるチャンクを、再メッシュ化の対象にする。
        /// </summary>
        private void MarkSamplesDirty(int3 sampleMin, int3 sampleMax)
        {
            var layout = _volume.Layout;
            layout.GetChunksAffectedBySamples(sampleMin, sampleMax, out var chunkMin, out var chunkMax);

            for (var z = chunkMin.z; z <= chunkMax.z; z++)
            for (var y = chunkMin.y; y <= chunkMax.y; y++)
            for (var x = chunkMin.x; x <= chunkMax.x; x++)
            {
                MarkDirty(layout.ToChunkIndex(new int3(x, y, z)));
            }
        }

        private void MarkDirty(int chunkIndex)
        {
            if (_isDirty[chunkIndex]) return;

            _isDirty[chunkIndex] = true;
            _dirtyQueue.Enqueue(chunkIndex);
        }

        /// <summary>
        /// 待ち行列の先頭から maxCount 個までのチャンクを並列にメッシュ化し、メッシュとコライダーへ反映する。
        /// </summary>
        private void RemeshPending(int maxCount)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var layout = _volume.Layout;
            var useHull = _colliderMode == VoxelColliderMode.ConvexHull;
            var count = math.min(maxCount, _dirtyQueue.Count);
            var chunkIndices = new int[count];
            var buffers = new MeshBuffers[count];
            var handles = new NativeArray<JobHandle>(count, Allocator.Temp);

            for (var i = 0; i < count; i++)
            {
                var chunkIndex = _dirtyQueue.Dequeue();
                _isDirty[chunkIndex] = false;
                chunkIndices[i] = chunkIndex;
                buffers[i] = new MeshBuffers(Allocator.TempJob);

                var meshingHandle = new SurfaceNetsJob
                {
                    Samples = _volume.Samples,
                    Layout = layout,
                    Chunk = layout.ToChunkCoord(chunkIndex),
                    Vertices = buffers[i].Vertices,
                    Normals = buffers[i].Normals,
                    Indices = buffers[i].Indices
                }.Schedule();

                handles[i] = useHull
                    ? new ExtremePointsJob
                    {
                        Points = buffers[i].Vertices.AsDeferredJobArray(),
                        Extremes = buffers[i].Extremes
                    }.Schedule(meshingHandle)
                    : meshingHandle;
            }

            JobHandle.CombineDependencies(handles).Complete();
            handles.Dispose();

            var colliderTargets = new List<ChunkSlot>(count);
            for (var i = 0; i < count; i++)
            {
                var slot = ApplyMesh(chunkIndices[i], buffers[i], useHull);
                if (slot?.Collider != null) colliderTargets.Add(slot);

                buffers[i].Dispose();
            }

            switch (_colliderMode)
            {
                case VoxelColliderMode.ChunkMesh:
                    BakeColliders(colliderTargets);
                    break;

                case VoxelColliderMode.ConvexHull:
                    RebuildHullCollider();
                    break;
            }

            LastRemeshMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// メッシュ化の結果をチャンクへ反映する。
        /// </summary>
        /// <returns>面を持つチャンク。面が無ければ null</returns>
        private ChunkSlot ApplyMesh(int chunkIndex, MeshBuffers buffers, bool useHull)
        {
            var slot = _chunks[chunkIndex];
            var triangleCount = buffers.Indices.Length / 3;

            if (triangleCount == 0)
            {
                if (slot == null) return null;

                TriangleCount -= slot.TriangleCount;
                slot.Clear();
                return null;
            }

            slot ??= _chunks[chunkIndex] = CreateChunkSlot(chunkIndex);
            TriangleCount += triangleCount - slot.TriangleCount;

            slot.SetMesh(buffers, triangleCount);
            slot.ExtremePoints = useHull ? buffers.Extremes.ToArray() : null;
            return slot;
        }

        private static void BakeColliders(List<ChunkSlot> slots)
        {
            if (slots.Count == 0) return;

            var meshIds = new NativeArray<EntityId>(slots.Count, Allocator.TempJob);
            for (var i = 0; i < slots.Count; i++)
            {
                meshIds[i] = slots[i].Mesh.GetEntityId();
            }

            new BakeColliderJob
            {
                MeshIds = meshIds,
                CookingOptions = ColliderCookingOptions
            }.Schedule(slots.Count, 1).Complete();
            meshIds.Dispose();

            foreach (var slot in slots)
            {
                slot.AssignCollider();
            }
        }

        /// <summary>
        /// 全チャンクの極点から、方向ごとに最も外側の点を選び直し、その点群の凸包をこの GameObject の MeshCollider にする。
        /// 点が 4 つ未満なら当たり判定を無効にする。
        /// </summary>
        private void RebuildHullCollider()
        {
            var directions = new float3[VoxelHullDirections.Count];
            var bestPoints = new float3[VoxelHullDirections.Count];
            var bestDots = new float[VoxelHullDirections.Count];
            for (var d = 0; d < directions.Length; d++)
            {
                directions[d] = VoxelHullDirections.Get(d);
                bestDots[d] = float.NegativeInfinity;
            }

            foreach (var slot in _chunks)
            {
                if (slot?.ExtremePoints == null) continue;

                foreach (var point in slot.ExtremePoints)
                {
                    for (var d = 0; d < directions.Length; d++)
                    {
                        var dot = math.dot(point, directions[d]);
                        if (dot <= bestDots[d]) continue;

                        bestDots[d] = dot;
                        bestPoints[d] = point;
                    }
                }
            }

            var vertices = new List<Vector3>(VoxelHullDirections.Count);
            for (var d = 0; d < bestPoints.Length; d++)
            {
                if (float.IsNegativeInfinity(bestDots[d])) continue;

                Vector3 point = bestPoints[d];
                if (!vertices.Contains(point)) vertices.Add(point);
            }

            if (vertices.Count < 4)
            {
                if (_hullCollider != null) _hullCollider.enabled = false;
                return;
            }

            if (_hullMesh == null) _hullMesh = new Mesh { name = $"{name}_Hull" };
            _hullMesh.Clear();
            _hullMesh.SetVertices(vertices);

            // 凸包の計算は頂点だけを使う。三角形は Mesh として有効にする為の仮のもの
            var triangles = new int[(vertices.Count - 2) * 3];
            for (var i = 0; i < vertices.Count - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            _hullMesh.SetTriangles(triangles, 0);

            if (_hullCollider == null)
            {
                _hullCollider = gameObject.AddComponent<MeshCollider>();
                _hullCollider.convex = true;
            }

            _hullCollider.sharedMesh = null;
            _hullCollider.sharedMesh = _hullMesh;
            _hullCollider.enabled = true;
        }

        private ChunkSlot CreateChunkSlot(int chunkIndex)
        {
            var coord = _volume.Layout.ToChunkCoord(chunkIndex);
            var chunkObject = new GameObject($"VoxelChunk_{coord.x}_{coord.y}_{coord.z}")
            {
                layer = gameObject.layer
            };
            chunkObject.transform.SetParent(transform, false);

            var mesh = new Mesh
            {
                name = chunkObject.name,
                indexFormat = IndexFormat.UInt32
            };
            mesh.MarkDynamic();

            var meshFilter = chunkObject.AddComponent<MeshFilter>();
            var meshRenderer = chunkObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;

            // MeshFilter に空の Mesh が入った状態で追加すると、空の Mesh をベイクしようとする為、先に追加する
            MeshCollider meshCollider = null;
            if (_colliderMode == VoxelColliderMode.ChunkMesh)
            {
                meshCollider = chunkObject.AddComponent<MeshCollider>();
                meshCollider.cookingOptions = ColliderCookingOptions;
            }

            meshFilter.sharedMesh = mesh;

            return new ChunkSlot(chunkObject, mesh, meshRenderer, meshCollider);
        }

        private void ReleaseVolume()
        {
            if (_chunks != null)
            {
                foreach (var slot in _chunks)
                {
                    if (slot == null) continue;

                    Destroy(slot.Mesh);
                    Destroy(slot.GameObject);
                }
            }

            if (_hullCollider != null)
            {
                _hullCollider.sharedMesh = null;
                _hullCollider.enabled = false;
            }

            _chunks = null;
            _isDirty = null;
            _dirtyQueue.Clear();
            TriangleCount = 0;

            _volume?.Dispose();
            _volume = null;
        }

        /// <summary>
        /// 1 チャンク分のメッシュ化の出力先。
        /// </summary>
        private struct MeshBuffers : IDisposable
        {
            public NativeList<float3> Vertices;
            public NativeList<float3> Normals;
            public NativeList<int> Indices;

            /// <summary> VoxelHullDirections の方向ごとの、頂点の中で最も外側の点 </summary>
            public NativeArray<float3> Extremes;

            public MeshBuffers(Allocator allocator)
            {
                Vertices = new NativeList<float3>(1024, allocator);
                Normals = new NativeList<float3>(1024, allocator);
                Indices = new NativeList<int>(4096, allocator);
                Extremes = new NativeArray<float3>(VoxelHullDirections.Count, allocator);
            }

            public void Dispose()
            {
                Vertices.Dispose();
                Normals.Dispose();
                Indices.Dispose();
                Extremes.Dispose();
            }
        }

        /// <summary>
        /// 1 チャンク分の GameObject・Mesh・コンポーネント。
        /// </summary>
        private sealed class ChunkSlot
        {
            public readonly GameObject GameObject;
            public readonly Mesh Mesh;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;

            public ChunkSlot(GameObject gameObject, Mesh mesh, MeshRenderer renderer, MeshCollider collider)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Renderer = renderer;
                Collider = collider;
            }

            public int TriangleCount { get; private set; }

            /// <summary> 凸包コライダー用の、方向ごとに最も外側の頂点。ConvexHull 以外や面が無いときは null </summary>
            public float3[] ExtremePoints { get; set; }

            public void SetMesh(MeshBuffers buffers, int triangleCount)
            {
                Mesh.Clear();
                Mesh.SetVertices(buffers.Vertices.AsArray());
                Mesh.SetNormals(buffers.Normals.AsArray());
                Mesh.SetIndices(buffers.Indices.AsArray(), MeshTopology.Triangles, 0);

                Renderer.enabled = true;
                TriangleCount = triangleCount;
            }

            /// <summary>
            /// ベイク済みの物理メッシュを MeshCollider へ割り当て直す。
            /// </summary>
            public void AssignCollider()
            {
                // 同じ Mesh を代入し直すだけでは物理メッシュが更新されないことがある為、一度外す
                Collider.sharedMesh = null;
                Collider.sharedMesh = Mesh;
                Collider.enabled = true;
            }

            /// <summary>
            /// 面が無くなったチャンクを非表示にし、当たり判定を外す。
            /// </summary>
            public void Clear()
            {
                Mesh.Clear();
                Renderer.enabled = false;
                TriangleCount = 0;
                ExtremePoints = null;

                if (Collider == null) return;

                Collider.sharedMesh = null;
                Collider.enabled = false;
            }
        }
    }
}
