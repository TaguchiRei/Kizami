using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kizami.EngineAdapter.Voxel.DebugTools
{
    /// <summary>
    /// マウスでボクセルを削る・盛る・加熱する検証用ツール。カメラに付ける。
    ///
    /// 左ボタン: 削る（熱: 加熱） / 右ボタン: 盛る（熱: 冷却） / ホイール: 道具の大きさ / 1・2・3・4: 球・箱・刃・熱
    /// 中ボタンドラッグ または Alt + 左ドラッグ: 注視点の周りを回る / F1: 検証用の画面表示を切り替える
    /// 熱の道具は、シーンに VoxelMeltSystem があればそれを通して範囲内の全ピースを、無ければ当たったピースだけを加熱する。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VoxelDebugCarveTool : MonoBehaviour
    {
        private enum ToolShape
        {
            Sphere,
            Box,
            Blade,
            Heat
        }

        [SerializeField, Min(0.001f)]
        [Tooltip("道具の半径（ワールド空間, m）")]
        private float _radius = 0.08f;

        [SerializeField]
        [Tooltip("ホイールで変えられる半径の範囲（m）")]
        private Vector2 _radiusRange = new(0.02f, 0.5f);

        [SerializeField]
        [Tooltip("ボタンを押している間、編集し続けるか。false ならボタンを押した瞬間に 1 回だけ編集する。熱の道具には効かない")]
        private bool _continuous = true;

        [SerializeField, Min(1f)]
        [Tooltip("ボタンを押し続けたときの 1 秒あたりの編集回数")]
        private float _editsPerSecond = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("熱の道具で、ボタンを押している間に 1 秒あたりに加える温度")]
        private float _heatPerSecond = 1.5f;

        [SerializeField]
        [Tooltip("ドラッグで回るときの中心")]
        private Transform _orbitTarget;

        [SerializeField, Min(0f)]
        [Tooltip("マウス移動 1 ピクセルあたりの回転角（度）")]
        private float _orbitDegreesPerPixel = 0.3f;

        private Camera _camera;
        private VoxelMeltSystem _meltSystem;
        private ToolShape _shape;
        private VoxelPiece _statsTarget;
        private float _yaw;
        private float _pitch;
        private float _distance;
        private float _smoothedDeltaTime = 1f / 60f;
        private float _nextEditTime;
        private bool _hasCursorTemperature;
        private float _cursorTemperature;

        private void Start()
        {
            _camera = GetComponent<Camera>();
            _meltSystem = FindAnyObjectByType<VoxelMeltSystem>();
            _statsTarget = FindAnyObjectByType<VoxelPiece>();

            if (_orbitTarget == null) return;

            var offset = transform.position - _orbitTarget.position;
            _distance = offset.magnitude;
            var euler = Quaternion.LookRotation(-offset).eulerAngles;
            _pitch = Mathf.DeltaAngle(0f, euler.x);
            _yaw = euler.y;
        }

        private void Update()
        {
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null) return;

            UpdateShape(keyboard);
            UpdateRadius(mouse);

            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                VoxelDebugHud.IsVisible = !VoxelDebugHud.IsVisible;
            }

            var isAltPressed = keyboard != null && keyboard.altKey.isPressed;
            if (mouse.middleButton.isPressed || (isAltPressed && mouse.leftButton.isPressed))
            {
                Orbit(mouse.delta.ReadValue());
                return;
            }

            var target = RaycastPiece(mouse, out var hit);
            _hasCursorTemperature = target != null;
            if (target == null) return;

            _cursorTemperature = target.SampleTemperature(hit.point);

            if (_shape == ToolShape.Heat)
            {
                UpdateHeat(mouse, target, hit.point);
                return;
            }

            var left = _continuous ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame;
            var right = _continuous ? mouse.rightButton.isPressed : mouse.rightButton.wasPressedThisFrame;
            if (!left && !right) return;
            if (_continuous && Time.unscaledTime < _nextEditTime) return;

            _statsTarget = target;
            _nextEditTime = Time.unscaledTime + 1f / _editsPerSecond;
            Edit(target, hit.point, left ? VoxelCsgOperation.Subtract : VoxelCsgOperation.Union);
        }

        private void OnGUI()
        {
            if (!VoxelDebugHud.IsVisible) return;

            GUILayout.BeginArea(new Rect(10f, 10f, 560f, 150f), GUI.skin.box);
            GUILayout.Label("左: 削る（熱: 加熱）  右: 盛る（熱: 冷却）  ホイール: 大きさ  " +
                            "中ドラッグ or Alt+左ドラッグ: 回転  F1: 表示の切り替え");
            GUILayout.Label($"1/2/3/4: 球/箱/刃/熱  道具: {_shape}  半径: {_radius:0.000} m  FPS: {1f / _smoothedDeltaTime:0}");

            if (_statsTarget != null)
            {
                GUILayout.Label($"三角形: {_statsTarget.TriangleCount:N0}  " +
                                $"待ちチャンク: {_statsTarget.PendingChunkCount}  " +
                                $"直近の再メッシュ化: {_statsTarget.LastRemeshMilliseconds:0.00} ms");
                GUILayout.Label($"ピース: {_statsTarget.name}  世代: {_statsTarget.Generation}  " +
                                $"体積: {_statsTarget.Volume:0.0000} m³（初期比 {_statsTarget.RelativeVolume:P0}）");
            }

            var temperature = _hasCursorTemperature ? $"{_cursorTemperature:0.00}" : "-";
            var melt = _meltSystem != null
                ? $"  粒: {_meltSystem.ParticleCount:N0} 個（{_meltSystem.FluidVolume * 1000f:0.00} L, " +
                  $"固まり {_meltSystem.FrozenCount:N0} 個）  蒸発: {_meltSystem.EvaporatedVolume * 1000f:0.00} L"
                : "";
            GUILayout.Label($"カーソル位置の温度: {temperature}{melt}");

            GUILayout.EndArea();
        }

        private VoxelPiece RaycastPiece(Mouse mouse, out RaycastHit hit)
        {
            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            return Physics.Raycast(ray, out hit, 100f) ? hit.collider.GetComponentInParent<VoxelPiece>() : null;
        }

        /// <summary>
        /// ボタンを押している間、当たった位置を中心とする球の範囲を、左なら加熱、右なら冷却する。
        /// 熱は球の境界で 0、境界から半径の半分だけ内側で最大になる。
        /// </summary>
        private void UpdateHeat(Mouse mouse, VoxelPiece target, Vector3 worldPoint)
        {
            var direction = (mouse.leftButton.isPressed ? 1f : 0f) - (mouse.rightButton.isPressed ? 1f : 0f);
            if (direction == 0f) return;

            _statsTarget = target;
            var shape = new SphereShape(worldPoint, _radius);
            var amount = direction * _heatPerSecond * Time.deltaTime;
            var falloff = _radius * 0.5f;

            if (_meltSystem != null)
            {
                _meltSystem.ApplyHeat(shape, amount, falloff);
            }
            else
            {
                target.ApplyHeat(shape, amount, falloff, Space.World);
            }
        }

        /// <summary>
        /// ワールド空間の当たった位置を中心に、道具の形状をワールド空間のまま合成する。
        /// 箱と刃はカメラと同じ向きに置く。刃はカメラの上方向と前方向に広がる、縦向きの薄い板。
        /// </summary>
        private void Edit(VoxelPiece target, Vector3 worldPoint, VoxelCsgOperation operation)
        {
            switch (_shape)
            {
                case ToolShape.Sphere:
                    target.ApplyEdit(new SphereShape(worldPoint, _radius), operation, Space.World);
                    break;

                case ToolShape.Box:
                    target.ApplyEdit(new BoxShape(worldPoint, _radius, transform.rotation), operation, Space.World);
                    break;

                case ToolShape.Blade:
                    // 厚みがボクセル 1.5 個分より薄いと、刃の間に外側のサンプルが並ばず塊が分かれない
                    var halfThickness = target.VoxelSize * 1.5f;
                    target.ApplyEdit(
                        new BoxShape(worldPoint, new float3(halfThickness, _radius * 4f, _radius * 4f), transform.rotation),
                        operation, Space.World);
                    break;
            }
        }

        private void UpdateShape(Keyboard keyboard)
        {
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) _shape = ToolShape.Sphere;
            if (keyboard.digit2Key.wasPressedThisFrame) _shape = ToolShape.Box;
            if (keyboard.digit3Key.wasPressedThisFrame) _shape = ToolShape.Blade;
            if (keyboard.digit4Key.wasPressedThisFrame) _shape = ToolShape.Heat;
        }

        private void UpdateRadius(Mouse mouse)
        {
            var scroll = mouse.scroll.ReadValue().y;
            if (scroll == 0f) return;

            var scale = scroll > 0f ? 1.1f : 1f / 1.1f;
            _radius = Mathf.Clamp(_radius * scale, _radiusRange.x, _radiusRange.y);
        }

        private void Orbit(Vector2 mouseDelta)
        {
            if (_orbitTarget == null) return;

            _yaw += mouseDelta.x * _orbitDegreesPerPixel;
            _pitch = Mathf.Clamp(_pitch - mouseDelta.y * _orbitDegreesPerPixel, -89f, 89f);

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_orbitTarget.position - rotation * Vector3.forward * _distance, rotation);
        }
    }
}
