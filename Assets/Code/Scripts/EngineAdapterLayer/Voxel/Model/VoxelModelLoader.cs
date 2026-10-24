using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelModelAsset の各パーツを、同じ相対パスにある Transform の VoxelPiece へ読み込む。
    /// VoxelPiece が無ければ追加する。
    /// 読み込むと、読み込み前からこの階層にあった MeshRenderer（元のモデルの見た目）を全て非表示にする。
    /// パーツと、パーツから切り離された全てのピースを、モデル単位でまとめて扱える。
    /// LoadStatic なら、ボクセルを作らずに手付かずの形のメッシュと BoxCollider だけを置き、Load を呼んだときにボクセルへ差し替える。
    /// 遠景用の代わりの見た目（SetStaticProxy）があれば、手付かずの間だけ LODGroup で、遠くでは手付かずの形のメッシュの代わりにそれを描く。
    /// 手付かずの形のメッシュはボクセルの表面なのでポリゴンが多く、たくさん並べると描画が重くなる為。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    public sealed class VoxelModelLoader : MonoBehaviour
    {
        private static readonly ProfilerMarker _loadMarker = new("Voxel.ModelLoad");

        private readonly List<VoxelPiece> _parts = new();
        private readonly List<VoxelPiece> _pieces = new();
        private readonly ActionChannel<VoxelModelLoader> _loaded = new();
        private readonly ActionChannel<VoxelShapeChange> _pieceShapeChanged = new();
        private readonly ActionChannel<VoxelPiece[]> _pieceSplit = new();
        private readonly ActionChannel<VoxelPiece> _pieceDestroyed = new();
        private readonly ActionChannel<(VoxelPiece Piece, VoxelStrikeRecord Record)> _pieceStruck = new();
        private readonly ActionChannel<(VoxelPiece Piece, VoxelSliceRecord Record)> _pieceSliced = new();

        private readonly List<GameObject> _staticObjects = new();
        private readonly List<VoxelStaticPart> _staticParts = new();

        [SerializeField]
        [Tooltip("読み込む事前ベイク済みのモデル")]
        private VoxelModelAsset _model;

        [SerializeField]
        [Tooltip("読み込み時のボクセルの大きさ・チャンクの大きさ")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("ボクセルのマテリアル。未設定なら、各パーツの元の MeshRenderer のマテリアルを使う")]
        private Material _materialOverride;

        [SerializeField]
        [Tooltip("削られて分かれた塊を、別の VoxelPiece として切り離すか")]
        private bool _splittable = true;

        [SerializeField]
        [Tooltip("融解・蒸発した分の送り先。未設定なら加熱しても何も起きない")]
        private VoxelMeltSystem _meltSystem;

        [SerializeField]
        [Tooltip("Start で読み込むか")]
        private bool _loadOnStart = true;

        private MeshRenderer[] _sourceRenderers;
        private VoxelMaterialEntry[] _materialEntries;

        /// <summary> 手付かずの間に遠くで描く代わりの見た目 </summary>
        private GameObject _staticProxy;

        /// <summary> 手付かずの形のメッシュから代わりの見た目に切り替える、画面に対する高さの割合 </summary>
        private float _proxyTransitionHeight;

        private LODGroup _staticLod;

        public VoxelQualitySettings Quality => _quality;
        public VoxelMeltSystem MeltSystem => _meltSystem;
        public bool IsLoaded { get; private set; }

        /// <summary> LoadStatic で、手付かずの形のメッシュだけを置いているか </summary>
        public bool IsStatic => _staticObjects.Count > 0;

        /// <summary> LoadStatic で置いたパーツの、読み込み先と形 </summary>
        internal IReadOnlyList<VoxelStaticPart> StaticParts => _staticParts;

        /// <summary> 読み込んだパーツ（モデルのメッシュ1つに対応する、分離前からあるピース） </summary>
        public IReadOnlyList<VoxelPiece> Parts => _parts;

        /// <summary> パーツと、パーツから切り離された全てのピース。破棄されたものは含まない </summary>
        public IReadOnlyList<VoxelPiece> Pieces => _pieces;

        /// <summary> パーツの体積の合計（ワールド空間, m³）。切り離されたピースは含まない </summary>
        public float Volume
        {
            get
            {
                var volume = 0f;
                foreach (var part in _parts)
                {
                    volume += part.Volume;
                }

                return volume;
            }
        }

        /// <summary> 読み込んだ時点のパーツの体積の合計（ワールド空間, m³） </summary>
        public float InitialVolume
        {
            get
            {
                var volume = 0f;
                foreach (var part in _parts)
                {
                    volume += part.InitialVolume;
                }

                return volume;
            }
        }

        /// <summary> 読み込んだ時点に対する、パーツの体積の合計の割合 </summary>
        public float RelativeVolume
        {
            get
            {
                var initialVolume = InitialVolume;
                return initialVolume > 0f ? Volume / initialVolume : 0f;
            }
        }

        /// <summary> モデルの素材の番号ごとの強度。SetMaterialEntries で渡されていればその強度 </summary>
        internal VoxelStrength GetMaterialStrength(int materialIndex)
        {
            return _materialEntries != null ? _materialEntries[materialIndex].Strength : _model.Materials[materialIndex].Strength;
        }

        /// <summary>
        /// 読み込むモデルを差し替える。次の Load から反映される。
        /// </summary>
        public void SetModel(VoxelModelAsset model)
        {
            _model = model;
        }

        /// <summary>
        /// 品質設定を差し替える。次の Load から反映される。
        /// </summary>
        public void SetQuality(VoxelQualitySettings quality)
        {
            _quality = quality;
        }

        /// <summary>
        /// Start で読み込むかを切り替える。Start より前に呼ぶこと。
        /// </summary>
        public void SetLoadOnStart(bool loadOnStart)
        {
            _loadOnStart = loadOnStart;
        }

        /// <summary>
        /// 手付かずの間に遠くで描く代わりの見た目を渡す。proxy はこの階層の子に置いたもので、手付かずの間だけ表示する。
        /// 画面に対する高さの割合が transitionHeight を下回ると、手付かずの形のメッシュから proxy に切り替える。次の LoadStatic から反映される。
        /// </summary>
        public void SetStaticProxy(GameObject proxy, float transitionHeight)
        {
            _staticProxy = proxy;
            _proxyTransitionHeight = transitionHeight;
            _staticProxy.SetActive(IsStatic);
        }

        /// <summary>
        /// 素材の番号ごとのマテリアルと強度を、モデルの素材の一覧の代わりに使う。次の Load・LoadStatic から反映される。
        /// </summary>
        public void SetMaterialEntries(VoxelMaterialEntry[] entries)
        {
            _materialEntries = entries;
        }

        /// <summary>
        /// 融解・蒸発した分の送り先を差し替える。読み込み済みのパーツにも反映する。
        /// </summary>
        public void SetMeltSystem(VoxelMeltSystem meltSystem)
        {
            _meltSystem = meltSystem;

            foreach (var part in _parts)
            {
                part.SetMeltSystem(meltSystem);
            }
        }

        /// <summary>
        /// パーツを、VoxelModelLoader からの相対パスで探す。
        /// </summary>
        public bool TryGetPart(string path, out VoxelPiece part)
        {
            foreach (var candidate in _parts)
            {
                if (candidate.PartPath != path) continue;

                part = candidate;
                return true;
            }

            part = null;
            return false;
        }

        /// <summary>
        /// 読み込みが終わったときに呼ぶ処理を登録する。読み込み直したときも呼ぶ。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnLoaded(Action<VoxelModelLoader> callback)
        {
            return _loaded.Register(callback);
        }

        /// <summary>
        /// このモデルのいずれかのピースの形状が、削る・盛る編集や融解で変わったときに呼ぶ処理を登録する。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnPieceShapeChanged(Action<VoxelShapeChange> callback)
        {
            return _pieceShapeChanged.Register(callback);
        }

        /// <summary>
        /// このモデルのいずれかのピースに強さと減衰率の攻撃が当たったときに呼ぶ処理を登録する。記録はそのピースのローカル空間で表す。
        /// </summary>
        public IDisposable RegisterOnPieceStruck(Action<(VoxelPiece Piece, VoxelStrikeRecord Record)> callback)
        {
            return _pieceStruck.Register(callback);
        }

        /// <summary>
        /// このモデルのいずれかのピースが平面で切られたときに呼ぶ処理を登録する。記録はそのピースのローカル空間で表す。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnPieceSliced(Action<(VoxelPiece Piece, VoxelSliceRecord Record)> callback)
        {
            return _pieceSliced.Register(callback);
        }

        /// <summary>
        /// このモデルのいずれかのピースから塊が分離したときに呼ぶ処理を登録する。
        /// 引数の0番目は分離元のピース、1番目以降は切り離された新しいピース。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnPieceSplit(Action<VoxelPiece[]> callback)
        {
            return _pieceSplit.Register(callback);
        }

        /// <summary>
        /// このモデルのいずれかのピースが破棄されるときに呼ぶ処理を登録する。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnPieceDestroyed(Action<VoxelPiece> callback)
        {
            return _pieceDestroyed.Register(callback);
        }

        /// <summary>
        /// 全パーツを現在の品質設定で読み込む。読み込み済みなら作り直す。
        /// 既に切り離されたピースはそのまま残る。
        /// </summary>
        public void Load()
        {
            if (_model == null || _quality == null)
            {
                UsefulLogger.LogError("VoxelModelAsset または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            using var loadScope = _loadMarker.Auto();
            var wasStatic = IsStatic;
            ClearStatic();
            _parts.Clear();

            foreach (var partData in _model.Parts)
            {
                var target = string.IsNullOrEmpty(partData.Path) ? transform : transform.Find(partData.Path);
                if (target == null)
                {
                    UsefulLogger.LogWarning($"パーツの読み込み先 '{partData.Path}' が見つからない為、読み込みを飛ばします。", this);
                    continue;
                }

                if (!target.TryGetComponent<VoxelPiece>(out var piece))
                {
                    piece = target.gameObject.AddComponent<VoxelPiece>();
                }

                piece.SetQuality(_quality);
                // 角ばった描き方の形は、中身の点をまとめた箱と一致する
                if (_quality.UseBlockShape) piece.SetColliderMode(VoxelColliderMode.Boxes);
                piece.SetMaterial(ResolveMaterial(target));
                if (partData.HasMaterials) piece.SetMaterials(ResolveMaterials(target));
                piece.SetSplittable(_splittable);
                piece.SetMeltSystem(_meltSystem);
                piece.BindToModel(this, partData.Path);
                piece.LoadSdf(partData);
                // メッシュのままの形から差し替えたときは、見た目が欠けるフレームを作らない
                if (wasStatic) piece.FlushRemesh();
                _parts.Add(piece);
            }

            var separatedPieces = _pieces.FindAll(piece => piece != null && piece.Generation > 0);
            _pieces.Clear();
            _pieces.AddRange(_parts);
            _pieces.AddRange(separatedPieces);

            foreach (var sourceRenderer in _sourceRenderers)
            {
                if (sourceRenderer != null) sourceRenderer.enabled = false;
            }

            IsLoaded = true;
            _loaded.Invoke(this);
        }

        /// <summary>
        /// ボクセルにしたパーツを消し、手付かずの形のメッシュに戻す。
        /// 切り離されたピースのうち、自分の Rigidbody を持たない物（ほかの物にくっつけた塊）も消す。落ちているかけらは残す。
        /// </summary>
        public void Unload()
        {
            if (!IsLoaded) return;

            foreach (var piece in _pieces)
            {
                if (piece == null) continue;

                if (piece.Generation == 0) Destroy(piece);
                else if (!piece.TryGetComponent<Rigidbody>(out _)) Destroy(piece.gameObject);
            }

            _parts.Clear();
            _pieces.RemoveAll(piece => piece == null || piece.Generation == 0 || !piece.TryGetComponent<Rigidbody>(out _));
            IsLoaded = false;
            LoadStatic();
        }

        /// <summary>
        /// ボクセルを作らずに、各パーツの手付かずの形のメッシュと BoxCollider を置く。元のモデルの MeshRenderer は非表示にする。
        /// 形は VoxelStaticShape を使い回す。Load を呼ぶと消してボクセルへ差し替える。
        /// </summary>
        public void LoadStatic()
        {
            if (_model == null || _quality == null)
            {
                UsefulLogger.LogError("VoxelModelAsset または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            ClearStatic();

            foreach (var partData in _model.Parts)
            {
                var target = string.IsNullOrEmpty(partData.Path) ? transform : transform.Find(partData.Path);
                if (target == null)
                {
                    UsefulLogger.LogWarning($"パーツの読み込み先 '{partData.Path}' が見つからない為、読み込みを飛ばします。", this);
                    continue;
                }

                var materials = partData.HasMaterials ? ResolveMaterials(target) : null;
                var materialCount = materials?.Length ?? 1;
                var localVoxelSize = _quality.VoxelSize / target.lossyScale.x;
                var shape = VoxelStaticShape.GetOrCreate(partData, localVoxelSize, _quality.ChunkSize, materialCount);

                var staticObject = new GameObject("VoxelStatic")
                {
                    layer = target.gameObject.layer
                };
                staticObject.transform.SetParent(target, false);
                staticObject.AddComponent<MeshFilter>().sharedMesh = shape.Mesh;
                var meshRenderer = staticObject.AddComponent<MeshRenderer>();
                if (materials != null)
                {
                    var rendererMaterials = new Material[materials.Length];
                    for (var i = 0; i < materials.Length; i++)
                    {
                        rendererMaterials[i] = materials[i].Material;
                    }

                    meshRenderer.sharedMaterials = rendererMaterials;
                }
                else
                {
                    meshRenderer.sharedMaterial = ResolveMaterial(target);
                }

                for (var i = 0; i < shape.BoxCenters.Count; i++)
                {
                    var box = staticObject.AddComponent<BoxCollider>();
                    box.center = shape.BoxCenters[i];
                    box.size = shape.BoxSizes[i];
                }

                _staticObjects.Add(staticObject);
                _staticParts.Add(new VoxelStaticPart(target, shape));
            }

            foreach (var sourceRenderer in _sourceRenderers)
            {
                if (sourceRenderer != null) sourceRenderer.enabled = false;
            }

            EnableStaticProxy();
        }

        internal void NotifyShapeChanged(in VoxelShapeChange change)
        {
            _pieceShapeChanged.Invoke(change);
        }

        internal void NotifyStruck(VoxelPiece piece, in VoxelStrikeRecord record)
        {
            _pieceStruck.Invoke((piece, record));
        }

        internal void NotifySliced(VoxelPiece piece, in VoxelSliceRecord record)
        {
            _pieceSliced.Invoke((piece, record));
        }

        internal void NotifySplit(VoxelPiece[] pieces)
        {
            for (var i = 1; i < pieces.Length; i++)
            {
                _pieces.Add(pieces[i]);
            }

            _pieceSplit.Invoke(pieces);
        }

        internal void NotifyDestroyed(VoxelPiece piece)
        {
            _parts.Remove(piece);
            _pieces.Remove(piece);
            _pieceDestroyed.Invoke(piece);
        }

        private void Awake()
        {
            // チャンクの MeshRenderer が生成される前に、元のモデルの MeshRenderer だけを控えておく
            _sourceRenderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        private void Start()
        {
            if (_loadOnStart) Load();
        }

        private void ClearStatic()
        {
            if (_staticLod != null) _staticLod.enabled = false;
            if (_staticProxy != null) _staticProxy.SetActive(false);

            foreach (var staticObject in _staticObjects)
            {
                if (staticObject == null) continue;

                // Destroy はフレームの終わりまで遅れる為、差し替えたボクセルと当たり判定が重ならないよう先に無効にする
                staticObject.SetActive(false);
                Destroy(staticObject);
            }

            _staticObjects.Clear();
            _staticParts.Clear();
        }

        /// <summary>
        /// 手付かずの形のメッシュを近く、代わりの見た目を遠くの段にした LODGroup を有効にする。代わりの見た目がなければ何もしない。
        /// </summary>
        private void EnableStaticProxy()
        {
            if (_staticProxy == null) return;

            _staticProxy.SetActive(true);
            var nearRenderers = new List<Renderer>();
            foreach (var staticObject in _staticObjects)
            {
                nearRenderers.Add(staticObject.GetComponent<MeshRenderer>());
            }

            if (_staticLod == null) _staticLod = gameObject.AddComponent<LODGroup>();
            _staticLod.SetLODs(new[]
            {
                new LOD(_proxyTransitionHeight, nearRenderers.ToArray()),
                new LOD(0f, _staticProxy.GetComponentsInChildren<Renderer>()),
            });
            _staticLod.RecalculateBounds();
            _staticLod.enabled = true;
        }

        /// <summary>
        /// 素材の番号ごとのマテリアルと強度。SetMaterialEntries で渡されていればそれを使う。上書き指定があれば全素材をそれで塗り、素材のマテリアルが空なら ResolveMaterial で補う。
        /// </summary>
        private VoxelMaterialEntry[] ResolveMaterials(Transform target)
        {
            if (_materialEntries != null) return _materialEntries;

            var materials = new VoxelMaterialEntry[_model.Materials.Count];
            for (var i = 0; i < materials.Length; i++)
            {
                var entry = _model.Materials[i];
                var material = _materialOverride != null ? _materialOverride : entry.Material;
                materials[i] = new VoxelMaterialEntry(material != null ? material : ResolveMaterial(target),
                    entry.Strength);
            }

            return materials;
        }

        /// <summary>
        /// 上書き指定 → 読み込み先の元の MeshRenderer → 階層内で最初の元の MeshRenderer の順にマテリアルを探す。
        /// </summary>
        private Material ResolveMaterial(Transform target)
        {
            if (_materialOverride != null) return _materialOverride;

            if (target.TryGetComponent<MeshRenderer>(out var targetRenderer)) return targetRenderer.sharedMaterial;

            return _sourceRenderers.Length > 0 ? _sourceRenderers[0].sharedMaterial : null;
        }
    }

    /// <summary>
    /// LoadStatic で置いたパーツの、読み込み先の Transform と手付かずの形。
    /// </summary>
    internal readonly struct VoxelStaticPart
    {
        public readonly Transform Target;
        public readonly VoxelStaticShape Shape;

        public VoxelStaticPart(Transform target, VoxelStaticShape shape)
        {
            Target = target;
            Shape = shape;
        }
    }
}
