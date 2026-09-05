using Unity.Cinemachine;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の視点（上下方向）反映。
    /// Cinemachine カメラの Tilt 軸を直接書き換える。可動域は CinemachinePanTilt.TiltAxis.Range（Inspector）で管理する。
    ///
    /// 視点入力はマウスや指の移動量として扱う為、届いたその場で回転へ加算する。
    /// 経過時間では割らない（移動量そのものが既にそのフレーム分の量である為）。
    ///
    /// Look 入力は Pointer/delta（OS カーソルの移動量）を使う為、カーソルが画面外へ出ると
    /// それ以上デルタが得られない。これを避ける為、有効化中はカーソルを中央にロックし非表示にする。
    /// タッチ操作のスマホではカーソル自体が存在しない為、この設定は実質何もしない。
    /// </summary>
    public sealed class StandardPlayerCameraAdapter : PlayerCameraAdapterBase
    {
        [SerializeField]
        [Tooltip("上下方向（Tilt）の回転を反映するコンポーネント。")]
        private CinemachinePanTilt _panTilt;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 のときの、入力 1 単位あたりの回転角（度）")]
        private float _degreesPerInput = 0.1f;

        public override void Initialize()
        {
            base.Initialize();

            if (_panTilt == null)
            {
                UsefulLogger.LogError("CinemachinePanTilt が設定されていません。", this);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        protected override void OnLookInputChanged(Vector2 lookInput)
        {
            if (_panTilt == null) return;
            if (lookInput.y == 0f) return;

            // 入力の上方向へ視点を向ける = カメラの Tilt は負方向へ回る。
            // TiltAxis.Value は MutateCameraState 内でクランプされない為、ここで TiltAxis.Range に収める
            _panTilt.TiltAxis.Value = _panTilt.TiltAxis.ClampValue(_panTilt.TiltAxis.Value - lookInput.y * _degreesPerInput);
        }
    }
}
