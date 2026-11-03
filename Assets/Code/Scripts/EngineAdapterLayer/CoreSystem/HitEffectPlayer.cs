using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 剣とスキルが当たった所に、白い閃光と火花を出す Adapter。インゲームのシーンへ置く。
    /// 閃光と火花はそれぞれ 1 つのパーティクルで、粒の使い回しとその上限は ParticleSystem の maxParticles に任せる。
    /// 範囲の広いスキルで重くならないよう、1 フレームに出す回数に上限を付ける。
    /// </summary>
    public sealed class HitEffectPlayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("閃光と火花に使う加算のマテリアル")]
        private Material _glowMaterial;

        [SerializeField, Min(1)]
        [Tooltip("1 フレームに出せる当たりの数。超えた分は出さない")]
        private int _maxHitsPerFrame = 8;

        [SerializeField, Min(0.01f)]
        [Tooltip("閃光と火花が消えるまでの時間（秒）")]
        private float _lifetime = 0.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("閃光の大きさ（m）")]
        private float _flashSize = 1.2f;

        [SerializeField, Min(0)]
        [Tooltip("1 回の当たりで散らす火花の数")]
        private int _sparkCount = 8;

        [SerializeField, Min(0.01f)]
        [Tooltip("火花の大きさ（m）")]
        private float _sparkSize = 0.12f;

        [SerializeField, Min(0f)]
        [Tooltip("火花の速さの最小（m/s）")]
        private float _sparkMinSpeed = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("火花の速さの最大（m/s）")]
        private float _sparkMaxSpeed = 14f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("火花が当たった面の法線から散らばる角度（度）")]
        private float _sparkSpread = 60f;

        private ParticleSystem _flash;
        private ParticleSystem _sparks;
        private int _hitFrame = -1;
        private int _hitCountInFrame;

        /// <summary> インゲームのシーンにある HitEffectPlayer。置かれていなければ null </summary>
        public static HitEffectPlayer Instance { get; private set; }

        /// <summary>
        /// 当たった所に閃光と火花を出す。HitEffectPlayer が無い、または 1 フレームの上限を超えたら何もしない。
        /// </summary>
        /// <param name="position">当たった位置（ワールド空間）</param>
        /// <param name="normal">当たった面の法線。火花はこの向きへ散る</param>
        public static void Play(Vector3 position, Vector3 normal)
        {
            if (Instance != null) Instance.Emit(position, normal);
        }

        private void Awake()
        {
            Instance = this;
            if (_glowMaterial == null)
            {
                UsefulLogger.LogError("ヒットエフェクトのマテリアルが設定されていない為、出せません。", this);
                return;
            }

            _flash = CreateParticle("HitFlash", ParticleSystemRenderMode.Billboard);
            _sparks = CreateParticle("HitSparks", ParticleSystemRenderMode.Stretch);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Emit(Vector3 position, Vector3 normal)
        {
            if (_flash == null) return;

            if (_hitFrame != Time.frameCount)
            {
                _hitFrame = Time.frameCount;
                _hitCountInFrame = 0;
            }

            if (_hitCountInFrame >= _maxHitsPerFrame) return;

            _hitCountInFrame++;

            var flashParams = new ParticleSystem.EmitParams
            {
                position = position,
                startSize = _flashSize,
                startLifetime = _lifetime
            };
            _flash.Emit(flashParams, 1);

            var direction = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up;
            for (var i = 0; i < _sparkCount; i++)
            {
                var sparkDirection = Vector3.Slerp(direction, Random.onUnitSphere, _sparkSpread / 180f);
                var sparkParams = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = sparkDirection.normalized * Random.Range(_sparkMinSpeed, _sparkMaxSpeed),
                    startSize = _sparkSize,
                    startLifetime = _lifetime * Random.Range(0.6f, 1f)
                };
                _sparks.Emit(sparkParams, 1);
            }
        }

        /// <summary>
        /// 自動では出さず、Emit で出した粒だけをワールド座標で動かすパーティクルを作る。出た粒は白から透明へ消える。
        /// Emit が止まらず効くよう、ループで再生し続ける。
        /// </summary>
        private ParticleSystem CreateParticle(string objectName, ParticleSystemRenderMode renderMode)
        {
            var particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(transform, false);
            var particle = particleObject.AddComponent<ParticleSystem>();
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particle.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _maxHitsPerFrame * Mathf.Max(_sparkCount, 1) * 12;

            var emission = particle.emission;
            emission.enabled = false;

            var shape = particle.shape;
            shape.enabled = false;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = particle.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = fade;

            var particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = renderMode;
            if (renderMode == ParticleSystemRenderMode.Stretch)
            {
                particleRenderer.velocityScale = 0.04f;
                particleRenderer.lengthScale = 1f;
            }

            particleRenderer.sharedMaterial = _glowMaterial;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;

            particle.Play();
            return particle;
        }
    }
}
