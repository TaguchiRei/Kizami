using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 土遁・造山で、視線の先にボクセルを盛る Adapter。
    /// 押している間、一定の間隔でカメラの視線が当たった点に球を足し、1 つのピースを大きくしていく。
    /// ピースは起動時にプールへ用意し、同時に置ける数を超えたら最も古いものを使い回す。
    /// </summary>
    public sealed class VoxelMoundAdapter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("盛るピースのプレハブ。品質とマテリアルを設定し、通常攻撃で切れる印を付けておく")]
        private VoxelPiece _piecePrefab;

        [SerializeField, Min(1)]
        [Tooltip("同時に置ける数。超えたら最も古いものを使い回す")]
        private int _maxCount = 4;

        [SerializeField, Min(1f)]
        [Tooltip("1 つのピースが盛れる範囲の一辺（m）。当たった点を底の中心にする")]
        private float _boxSize = 8f;

        [SerializeField, Min(1f)]
        [Tooltip("視線が地形に当たる最大の距離（m）")]
        private float _maxDistance = 30f;

        [SerializeField]
        [Tooltip("視線が当たる対象のレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField, Min(0.01f)]
        [Tooltip("押している間に球を足す間隔（秒）")]
        private float _interval = 0.25f;

        [SerializeField, Min(0)]
        [Tooltip("押している間、球を足すたびに消費するチャージ量")]
        private int _tickCost = 2;

        private readonly List<VoxelPiece> _freePieces = new();
        private readonly List<VoxelPiece> _activePieces = new();
        private Func<int, bool> _tryConsume;
        private VoxelPiece _current;
        private Bounds _currentBox;
        private float _radius;
        private double _nextTime;

        /// <summary> 押している間か </summary>
        public bool IsMounding { get; private set; }

        /// <param name="tryConsume">チャージを消費する関数。引数は消費量で、足りなければ false を返す</param>
        public void Initialize(Func<int, bool> tryConsume)
        {
            _tryConsume = tryConsume;

            if (_piecePrefab == null)
            {
                UsefulLogger.LogError("造山のピースのプレハブが設定されていない為、盛れません。", this);
                return;
            }

            for (var i = 0; i < _maxCount; i++)
            {
                var piece = Instantiate(_piecePrefab, transform);
                piece.gameObject.SetActive(false);
                _freePieces.Add(piece);
            }
        }

        /// <summary>
        /// 視線の先に盛れる地形があれば、盛り始める。押した瞬間に呼ぶ。ピースと最初の球は、次の Update で足す。
        /// </summary>
        /// <param name="radius">足す球の半径（m）</param>
        /// <returns>盛り始められたら true。視線が何にも当たらなければ false</returns>
        public bool Begin(float radius)
        {
            if (_piecePrefab == null || IsMounding || !TryRaycast(out _)) return false;

            IsMounding = true;
            _current = null;
            _radius = radius;
            _nextTime = 0d;
            return true;
        }

        /// <summary>
        /// 盛るのを止める。盛ったピースは残る。
        /// </summary>
        public void End()
        {
            IsMounding = false;
            _current = null;
        }

        private void Update()
        {
            if (!IsMounding || Time.unscaledTimeAsDouble < _nextTime) return;
            if (!TryRaycast(out var hit)) return;

            if (_current == null)
            {
                _current = TakePiece(hit.point);
                _nextTime = Time.unscaledTimeAsDouble + _interval;
                AddSphere(hit.point);
                return;
            }

            if (!_currentBox.Contains(hit.point)) return;

            if (_tryConsume != null && !_tryConsume(_tickCost))
            {
                End();
                return;
            }

            _nextTime = Time.unscaledTimeAsDouble + _interval;
            AddSphere(hit.point);
        }

        private bool TryRaycast(out RaycastHit hit)
        {
            hit = default;
            var cameraMain = Camera.main;
            if (cameraMain == null) return false;

            var cameraTransform = cameraMain.transform;
            return Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, _maxDistance,
                _targetLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 当たった点を底の中心にした箱のピースを、空のボリュームで用意する。空きがなければ最も古いものを使い回す。
        /// </summary>
        private VoxelPiece TakePiece(Vector3 groundPoint)
        {
            VoxelPiece piece;
            if (_freePieces.Count > 0)
            {
                piece = _freePieces[^1];
                _freePieces.RemoveAt(_freePieces.Count - 1);
            }
            else
            {
                piece = _activePieces[0];
                _activePieces.RemoveAt(0);
                piece.gameObject.SetActive(false);
            }

            var center = groundPoint + Vector3.up * (_boxSize * 0.5f);
            piece.transform.SetPositionAndRotation(center, Quaternion.identity);
            piece.transform.localScale = Vector3.one;
            piece.gameObject.SetActive(true);
            piece.CreateVolume(VoxelBounds.FromCenterExtents(Unity.Mathematics.float3.zero,
                new Unity.Mathematics.float3(_boxSize * 0.5f)));

            _activePieces.Add(piece);
            _currentBox = new Bounds(center, Vector3.one * _boxSize);
            return piece;
        }

        private void AddSphere(Vector3 worldPoint)
        {
            _current.ApplyEdit(new SphereShape(worldPoint, _radius), VoxelCsgOperation.Union, Space.World);
        }
    }
}
