using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 崩したあと、時間がたつと元の形に戻るボクセルの構造物。置く位置に付け、構造物のプレハブ（VoxelModelLoader をルートに持つもの）を指定する。
    /// 読み込んだ時点の体積から一定の割合以上が削れるか切り離されてから一定時間たつと、構造物と切り離されたピースをすべて消し、同じ位置にプレハブから置き直す。
    /// 壊れたものの上や横にプレイヤーがいると巻き込む為、カメラが一定の距離より離れるまで置き直しを待つ。
    /// </summary>
    public sealed class RegrowingStructure : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("構造物のプレハブ。ルートに VoxelModelLoader を付ける")]
        private GameObject _prefab;

        [SerializeField, Min(0f)]
        [Tooltip("壊れたとみなしてから、元に戻すまでの時間（秒）")]
        private float _regrowDelay = 60f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("読み込んだ時点の体積に対する、この割合を下回ったら壊れたとみなす")]
        private float _brokenRatio = 0.9f;

        [SerializeField, Min(0f)]
        [Tooltip("置き直すときに、カメラがこの距離（m）より離れている必要がある")]
        private float _clearDistance = 25f;

        private readonly List<VoxelPiece> _pieceBuffer = new();

        private VoxelModelLoader _loader;

        /// <summary> 壊れたとみなした時刻。壊れていなければ負 </summary>
        private float _brokenTime = -1f;

        private void Awake()
        {
            Spawn();
        }

        private void Update()
        {
            if (_loader == null || !_loader.IsLoaded || _loader.InitialVolume <= 0f) return;

            if (_brokenTime < 0f)
            {
                if (_loader.RelativeVolume < _brokenRatio) _brokenTime = Time.time;
                return;
            }

            if (Time.time - _brokenTime < _regrowDelay || !IsCameraClear()) return;

            Clear();
            Spawn();
        }

        private bool IsCameraClear()
        {
            var camera = Camera.main;
            return camera == null || (camera.transform.position - transform.position).sqrMagnitude >= _clearDistance * _clearDistance;
        }

        private void Spawn()
        {
            var instance = Instantiate(_prefab, transform);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _loader = instance.GetComponent<VoxelModelLoader>();
            _brokenTime = -1f;
        }

        /// <summary>
        /// 構造物から切り離されたピースと、構造物を消す。切り離されたピースは構造物の子ではない為、先に消す。
        /// </summary>
        private void Clear()
        {
            _pieceBuffer.AddRange(_loader.Pieces);
            foreach (var piece in _pieceBuffer)
            {
                if (piece != null) Destroy(piece.gameObject);
            }

            _pieceBuffer.Clear();
            Destroy(_loader.gameObject);
            _loader = null;
        }
    }
}
