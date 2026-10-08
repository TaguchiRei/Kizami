using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボクセルでできた破壊対象。VoxelModelLoader と同じ GameObject に付け、ステージシーンへ置く。
    /// 重要パーツが 1 つずつ、読み込んだ時点の体積から必要な割合以上削れたら破壊済みとする。
    /// 本体から分離した塊はパーツの体積に含まれない為、切り離した分も削れた分に数える。
    /// </summary>
    [RequireComponent(typeof(VoxelModelLoader))]
    public sealed class DestructionTarget : MonoBehaviour
    {
        private readonly List<VoxelPiece> _importantParts = new();

        [SerializeField]
        [Tooltip("重要パーツの、VoxelModelLoader からの相対パス")]
        private string[] _importantPartPaths = Array.Empty<string>();

        [SerializeField, Range(0f, 1f)]
        [Tooltip("重要パーツごとに、読み込んだ時点の体積のうち削れている必要がある割合")]
        private float _requiredRatio = 0.7f;

        private IDisposable _loadedSubscription;

        /// <summary> 重要パーツごとに削れている必要がある割合 </summary>
        public float RequiredRatio => _requiredRatio;

        /// <summary> 読み込めた重要パーツの数 </summary>
        public int ImportantPartCount => _importantParts.Count;

        /// <summary> 重要パーツがすべて必要な割合以上削れているか。読み込む前と体積を測る前は false </summary>
        public bool IsDestroyed
        {
            get
            {
                if (_importantParts.Count == 0) return false;

                for (var i = 0; i < _importantParts.Count; i++)
                {
                    if (!TryGetDestroyedRatio(i, out var ratio) || ratio < _requiredRatio) return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 重要パーツの、読み込んだ時点の体積のうち削れた割合を返す。パーツが破棄されていれば 1。
        /// 読み込んだあと、まだ体積を測っていなければ false。
        /// </summary>
        /// <param name="index">重要パーツの番号。0 から ImportantPartCount - 1</param>
        /// <param name="ratio">削れた割合（0〜1）</param>
        public bool TryGetDestroyedRatio(int index, out float ratio)
        {
            var part = _importantParts[index];
            if (part == null)
            {
                ratio = 1f;
                return true;
            }

            ratio = 1f - part.RelativeVolume;
            return part.InitialSampleCount > 0;
        }

        private void Awake()
        {
            // VoxelModelLoader は Start で読み込むので、Awake で登録すれば読み込みの通知を逃さない
            _loadedSubscription = GetComponent<VoxelModelLoader>().RegisterOnLoaded(OnLoaded);
        }

        private void OnDestroy()
        {
            _loadedSubscription?.Dispose();
        }

        /// <summary>
        /// 重要パーツのパスから、読み込んだパーツを引き直す。
        /// </summary>
        private void OnLoaded(VoxelModelLoader loader)
        {
            _importantParts.Clear();

            foreach (var path in _importantPartPaths)
            {
                if (loader.TryGetPart(path, out var part))
                {
                    _importantParts.Add(part);
                }
                else
                {
                    UsefulLogger.LogError($"重要パーツ '{path}' が {loader.name} のパーツに見つかりません。", this);
                }
            }

            if (_importantParts.Count == 0)
            {
                UsefulLogger.LogError($"{name} の重要パーツが 1 つもない為、破壊済みになりません。", this);
            }
        }
    }
}
