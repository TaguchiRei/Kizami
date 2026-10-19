using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 倒れて止まった塔（TowerTopple）を渡る進み具合を測り、画面の端から中央へ暗くして、渡りきりのラインを越えた瞬間に真っ暗にしてクリアを知らせる Adapter。
    /// 進み具合は、プレイヤーが塔に乗った位置を 0、塔の軸に沿った渡りきりのラインを 1 とした、プレイヤー位置の軸への射影。戻れば暗さも戻る。
    /// 暗転は URP の Volume（Vignette と Color Adjustments の露出）で行う。Vignette だけでは中央が残る為、終盤は露出を下げて真っ暗にする。
    /// 暗転用の Volume は初期化でシーンへ作り、破棄で消す。
    /// </summary>
    public sealed class TowerCrossingAdapter : InitializableMonoBehaviour
    {
        /// <summary> 足元の判定に使う、プレイヤーの位置から下へ向ける Ray の始点の持ち上げ量（m） </summary>
        private const float RAY_LIFT = 0.5f;

        /// <summary> 足元の判定に使う Ray の長さ（m） </summary>
        private const float RAY_LENGTH = 3f;

        /// <summary> 完全に暗くしたときの露出（EV）。ここまで下げると何も見えない </summary>
        private const float BLACKOUT_EXPOSURE = -12f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("渡りきりのラインの位置。塔の軸に沿った長さに対する割合で、根元が 0、先端が 1。崖を越えた位置に合わせる")]
        private float _goalRatio = 0.8f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("露出を下げ始める進み具合。これより手前は、画面の端の暗さだけで暗くする")]
        private float _exposureStart = 0.6f;

        private TowerTopple _tower;
        private Transform _player;
        private Action _onCrossed;
        private Volume _volume;
        private VolumeProfile _profile;
        private Vignette _vignette;
        private ColorAdjustments _colorAdjustments;

        /// <summary> 塔に乗った位置の、軸に沿った距離（m）。まだ乗っていなければ NaN </summary>
        private float _boardedDistance = float.NaN;

        private bool _hasCrossed;

        /// <summary>
        /// 暗転用の Volume を作り、測る対象を設定する。ステージシーンを読み込んだあとに呼ぶ。
        /// </summary>
        /// <param name="tower">渡る塔</param>
        /// <param name="player">進み具合を測るプレイヤーの Transform</param>
        /// <param name="onCrossed">渡りきりのラインを越えたときに、1 回だけ呼ぶ処理</param>
        public void Initialize(TowerTopple tower, Transform player, Action onCrossed)
        {
            _tower = tower;
            _player = player;
            _onCrossed = onCrossed;
            CreateVolume();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized || _hasCrossed) return;

            var body = _tower.SettledBody;
            if (body == null) return;

            var distance = Vector3.Dot(_player.position - body.position, body.transform.up);
            if (float.IsNaN(_boardedDistance))
            {
                if (!IsStandingOn(body)) return;

                _boardedDistance = distance;
                EnablePostProcessing();
            }

            var goalDistance = _tower.AxisLength * _goalRatio;
            if (goalDistance <= _boardedDistance + 1f) return;

            var progress = Mathf.Clamp01((distance - _boardedDistance) / (goalDistance - _boardedDistance));
            ApplyDarkness(progress);
            if (progress < 1f) return;

            _hasCrossed = true;
            UsefulLogger.Log("塔を渡りきった為、クリアにします。", this);
            _onCrossed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_volume != null) Destroy(_volume.gameObject);
            if (_profile != null) Destroy(_profile);
        }

        /// <summary>
        /// プレイヤーの足元の下向きの Ray が、倒れた塔のまとまりのコライダーに当たるか。
        /// </summary>
        private bool IsStandingOn(Rigidbody body)
        {
            var origin = _player.position + Vector3.up * RAY_LIFT;
            return Physics.Raycast(origin, Vector3.down, out var hit, RAY_LENGTH, ~0, QueryTriggerInteraction.Ignore) &&
                   hit.collider.attachedRigidbody == body;
        }

        /// <summary>
        /// 進み具合に応じて、画面の端から暗くする。終盤は露出を下げて、進み具合が 1 のときに何も見えなくする。
        /// </summary>
        private void ApplyDarkness(float progress)
        {
            _vignette.intensity.value = progress;
            var exposureProgress = Mathf.InverseLerp(_exposureStart, 1f, progress);
            _colorAdjustments.postExposure.value = Mathf.Lerp(0f, BLACKOUT_EXPOSURE, exposureProgress * exposureProgress);
        }

        private void CreateVolume()
        {
            var volumeObject = new GameObject("TowerCrossingVolume");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(volumeObject, gameObject.scene);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _vignette = _profile.Add<Vignette>();
            _vignette.intensity.Override(0f);
            _vignette.smoothness.Override(1f);
            _vignette.color.Override(Color.black);
            _vignette.rounded.Override(false);
            _colorAdjustments = _profile.Add<ColorAdjustments>();
            _colorAdjustments.postExposure.Override(0f);

            _volume = volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.profile = _profile;
        }

        /// <summary>
        /// メインカメラの後処理が切れていれば入れる。切れていると Volume の効果が出ない為。
        /// </summary>
        private void EnablePostProcessing()
        {
            var cameraMain = Camera.main;
            if (cameraMain == null || !cameraMain.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData)) return;

            if (cameraData.renderPostProcessing) return;

            cameraData.renderPostProcessing = true;
            UsefulLogger.LogWarning("メインカメラの後処理が切れていた為、入れました。", this);
        }
    }
}
