using UnityEngine;
using UnityEngine.VFX;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 爆発の見た目1つ。入れ子の球のアニメーション（子の Spheres の Animator）と、火花の VFX Graph を、位置と大きさを指定して再生する。
    /// 有効にすると Animator が既定のステートを頭から再生する。Animator はルートの拡大も動かすので、大きさは Animator の外側のこの GameObject で指定する。
    /// VoxelDestructionAdapter が初期化のときに作って使い回し、再生が終わると自分で非表示になる。
    /// </summary>
    public sealed class BlastEffect : MonoBehaviour
    {
        /// <summary> VFX Graph の粒の大きさに掛ける倍率。粒の大きさは GameObject の拡大に連動しない為、別に渡す </summary>
        private static readonly int _scaleId = Shader.PropertyToID("Scale");

        [SerializeField]
        [Tooltip("火花を出す VisualEffect。VFX Graph は BallBlast.vfx")]
        private VisualEffect _effect;

        [SerializeField, Min(0.01f)]
        [Tooltip("火花の粒の大きさを、GameObject の拡大に対して何倍にするか")]
        private float _particleSizeRatio = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("火花を出し続ける時間（秒）。VFX Graph が一定間隔で粒を出し続けるので、この時間で止める")]
        private float _spawnDuration = 0.6f;

        [SerializeField, Min(0.1f)]
        [Tooltip("再生を始めてから非表示にするまでの時間（秒）。アニメーションと、火花の粒が消えるまでより長くする")]
        private float _lifetime = 2.8f;

        /// <summary> 再生を始めてからの経過時間（秒） </summary>
        private float _elapsed;

        private bool _isSpawning;

        /// <summary> 再生中か </summary>
        public bool IsPlaying => gameObject.activeSelf;

        /// <summary>
        /// center に、大きさを scale 倍にして再生する。再生中なら頭から再生し直す。
        /// </summary>
        /// <param name="center">爆発の中心（ワールド空間）</param>
        /// <param name="scale">プレハブの大きさに掛ける倍率</param>
        public void Play(Vector3 center, float scale)
        {
            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one * scale;

            gameObject.SetActive(false);
            gameObject.SetActive(true);
            if (_effect != null)
            {
                _effect.SetFloat(_scaleId, scale * _particleSizeRatio);
                _effect.Reinit();
                _effect.Play();
            }

            _elapsed = 0f;
            _isSpawning = true;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (_isSpawning && _elapsed >= _spawnDuration)
            {
                _isSpawning = false;
                if (_effect != null) _effect.Stop();
            }

            if (_elapsed >= _lifetime) gameObject.SetActive(false);
        }
    }
}
