using UnityEngine;
using UnityEngine.Playables;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスが水晶のあった所へ飛んでくる飛来を扱う。ボスの根に付ける。
    /// ムービー（Timeline の PlayableDirector）があれば再生し、終わったらボスを着く所へ置く。なければ、ボスを放物線で着く所まで飛ばす。
    /// 着く所の高さは、飛ぶ前の地面からの高さを保つ。
    /// </summary>
    public sealed class BossArrival : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("飛来のムービー。なければ放物線で飛ばす")]
        private PlayableDirector _director;

        [SerializeField]
        [Tooltip("地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(0.1f)]
        [Tooltip("ムービーがないときに、飛んでから着くまでの時間（秒）")]
        private float _flightTime = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ムービーがないときの、放物線の頂点の高さ（m）")]
        private float _arcHeight = 40f;

        [SerializeField, Min(0.1f)]
        [Tooltip("地面を探すレイの長さ（m）")]
        private float _groundRayLength = 200f;

        /// <summary> 飛ぶ前の位置（ワールド座標） </summary>
        private Vector3 _from;

        /// <summary> 着く所（ワールド座標） </summary>
        private Vector3 _to;

        /// <summary> 飛び始めてからの時間（秒） </summary>
        private float _time;

        /// <summary> 飛んでいるか </summary>
        public bool IsArriving { get; private set; }

        /// <summary>
        /// destination の上へ飛び始める。
        /// </summary>
        /// <param name="destination">着く所の水平の位置（ワールド座標）。高さは地面から決める</param>
        public void Begin(Vector3 destination)
        {
            _from = transform.position;
            _to = destination;
            _to.y = GetGroundHeight(destination) + (_from.y - GetGroundHeight(_from));
            _time = 0f;
            IsArriving = true;

            if (_director != null) _director.Play();
        }

        private void Update()
        {
            if (!IsArriving) return;

            _time += Time.deltaTime;
            if (_director != null)
            {
                if (_director.state == PlayState.Playing && _time < _director.duration) return;

                Finish();
                return;
            }

            var t = Mathf.Clamp01(_time / _flightTime);
            transform.position = Vector3.Lerp(_from, _to, t) + Vector3.up * (4f * _arcHeight * t * (1f - t));
            if (t >= 1f) Finish();
        }

        private void Finish()
        {
            transform.position = _to;
            IsArriving = false;
        }

        /// <summary>
        /// 位置 position の真下の地面の高さ。地面がなければ位置の高さ。
        /// </summary>
        private float GetGroundHeight(Vector3 position)
        {
            var origin = position + Vector3.up * (_groundRayLength * 0.5f);
            return BossGroundProbe.TryGetGround(transform, origin, _groundRayLength, _groundLayers, out var ground)
                ? ground.y
                : position.y;
        }
    }
}
