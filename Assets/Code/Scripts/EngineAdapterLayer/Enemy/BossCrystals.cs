using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦の水晶の段階で、ボスがボクセルの水晶を投げてマップに置く。ボスの根に付ける。
    /// 水晶は置き場所の目印ごとに 1 つ、ボスの位置から放物線で飛ばし、着いたらその場に置く。
    /// 水晶はすべて、DestructionTarget の重要パーツが決めた割合まで削れたら壊れたとする。
    /// </summary>
    /// <remarks>
    /// 水晶のプレハブの根には VoxelModelLoader と DestructionTarget を付ける。
    /// StageClearAdapter はステージの初期化のときに破壊対象を集めるので、あとから投げた水晶はクリアの数に入らない。
    /// 次に投げるときに、前の周の水晶を消す。
    /// </remarks>
    public sealed class BossCrystals : MonoBehaviour
    {
        private readonly List<DestructionTarget> _crystals = new();
        private readonly List<Vector3> _origins = new();
        private readonly List<Vector3> _destinations = new();

        [SerializeField]
        [Tooltip("水晶のプレハブ。根に VoxelModelLoader と DestructionTarget を付ける")]
        private DestructionTarget _crystalPrefab;

        [SerializeField]
        [Tooltip("水晶を置く場所の目印。目印の数だけ水晶を投げる")]
        private Transform[] _placePoints;

        [SerializeField, Min(0.1f)]
        [Tooltip("投げてから着くまでの時間（秒）")]
        private float _flightTime = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("放物線の頂点の、投げた所と置く所を結ぶ線からの高さ（m）")]
        private float _arcHeight = 30f;

        [SerializeField]
        [Tooltip("投げ始める所の、ボスの根からの高さ（m）")]
        private float _throwHeight = 10f;

        /// <summary> 投げてからの割合（0〜1）。1 以上なら着いている </summary>
        private float _progress = 1f;

        /// <summary> 投げた水晶がすべて着いて、すべて壊れたか。投げられた水晶がなければ true </summary>
        public bool IsCleared
        {
            get
            {
                if (_progress < 1f) return false;

                foreach (var crystal in _crystals)
                {
                    if (crystal != null && !crystal.IsDestroyed) return false;
                }

                return true;
            }
        }

        /// <summary> 最後に投げた水晶を置いた場所の中心（ワールド座標）。投げていなければボスの位置 </summary>
        public Vector3 PlacedCenter
        {
            get
            {
                if (_destinations.Count == 0) return transform.position;

                var sum = Vector3.zero;
                foreach (var destination in _destinations)
                {
                    sum += destination;
                }

                return sum / _destinations.Count;
            }
        }

        /// <summary>
        /// 前の周の水晶を消し、置き場所の目印ごとに水晶を 1 つ投げる。
        /// </summary>
        public void Throw()
        {
            Clear();
            if (_crystalPrefab == null || _placePoints == null || _placePoints.Length == 0)
            {
                UsefulLogger.LogError("水晶のプレハブか置き場所の目印がない為、水晶を投げられません。", this);
                return;
            }

            var origin = transform.position + Vector3.up * _throwHeight;
            foreach (var point in _placePoints)
            {
                if (point == null) continue;

                var crystal = Instantiate(_crystalPrefab, origin, point.rotation);
                _crystals.Add(crystal);
                _origins.Add(origin);
                _destinations.Add(point.position);
            }

            _progress = 0f;
        }

        /// <summary>
        /// まだ壊れていない水晶の中心（ワールド座標）を書き足す。
        /// </summary>
        /// <param name="centers">書き足す先</param>
        public void AppendRemainingCenters(List<Vector3> centers)
        {
            foreach (var crystal in _crystals)
            {
                if (crystal != null && !crystal.IsDestroyed) centers.Add(crystal.Center);
            }
        }

        /// <summary>
        /// 水晶をすべて消す。
        /// </summary>
        public void Clear()
        {
            foreach (var crystal in _crystals)
            {
                if (crystal != null) Destroy(crystal.gameObject);
            }

            _crystals.Clear();
            _origins.Clear();
            _destinations.Clear();
            _progress = 1f;
        }

        /// <summary>
        /// 水晶ごとの、名前と壊れ具合を書き足す。
        /// </summary>
        public void AppendStatus(StringBuilder builder)
        {
            foreach (var crystal in _crystals)
            {
                if (crystal == null) continue;

                crystal.AppendStatus(builder);
                builder.Append(' ');
            }
        }

        private void Update()
        {
            if (_progress >= 1f) return;

            _progress = Mathf.Min(1f, _progress + Time.deltaTime / _flightTime);
            for (var i = 0; i < _crystals.Count; i++)
            {
                if (_crystals[i] == null) continue;

                var t = _progress;
                _crystals[i].transform.position = Vector3.Lerp(_origins[i], _destinations[i], t) +
                                                  Vector3.up * (4f * _arcHeight * t * (1f - t));
            }
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
