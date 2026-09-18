using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelModelAsset の各パーツを、同じ相対パスにある Transform の VoxelPiece へ読み込む。
    /// VoxelPiece が無ければ追加する。
    /// 読み込むと、読み込み前からこの階層にあった MeshRenderer（元のモデルの見た目）を全て非表示にする。
    ///
    /// パーツと、パーツから切り離された全てのピースを、モデル単位でまとめて扱える。
    /// </summary>
    public sealed class VoxelModelLoader : MonoBehaviour
    {
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

        private readonly List<VoxelPiece> _parts = new();
        private readonly List<VoxelPiece> _pieces = new();
        private readonly ActionChannel<VoxelModelLoader> _loaded = new();
        private readonly ActionChannel<VoxelShapeChange> _pieceShapeChanged = new();
        private readonly ActionChannel<VoxelPiece[]> _pieceSplit = new();
        private readonly ActionChannel<VoxelPiece> _pieceDestroyed = new();
        private MeshRenderer[] _sourceRenderers;

        public VoxelQualitySettings Quality => _quality;
        public VoxelMeltSystem MeltSystem => _meltSystem;
        public bool IsLoaded { get; private set; }

        /// <summary> 読み込んだパーツ（モデルのメッシュ 1 つに対応する、分離前からあるピース） </summary>
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

        /// <summary>
        /// 品質設定を差し替える。次の Load から反映される。
        /// </summary>
        public void SetQuality(VoxelQualitySettings quality)
        {
            _quality = quality;
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
        /// このモデルのいずれかのピースから塊が分離したときに呼ぶ処理を登録する。
        /// 引数の 0 番目は分離元のピース、1 番目以降は切り離された新しいピース。
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
                Debug.LogError("VoxelModelAsset または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            _parts.Clear();

            foreach (var partData in _model.Parts)
            {
                var target = string.IsNullOrEmpty(partData.Path) ? transform : transform.Find(partData.Path);
                if (target == null)
                {
                    Debug.LogWarning($"パーツの読み込み先 '{partData.Path}' が見つからない為、読み込みを飛ばします。", this);
                    continue;
                }

                if (!target.TryGetComponent<VoxelPiece>(out var piece))
                {
                    piece = target.gameObject.AddComponent<VoxelPiece>();
                }

                piece.SetQuality(_quality);
                piece.SetMaterial(ResolveMaterial(target));
                piece.SetSplittable(_splittable);
                piece.SetMeltSystem(_meltSystem);
                piece.BindToModel(this, partData.Path);
                piece.LoadSdf(partData);
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

        internal void NotifyShapeChanged(in VoxelShapeChange change)
        {
            _pieceShapeChanged.Invoke(change);
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
}
