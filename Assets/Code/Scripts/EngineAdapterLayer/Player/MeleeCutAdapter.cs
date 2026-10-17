using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 近接切断の振り（Swing）を受けて、カメラの位置と向き、切断面の角度から刃を配置し、
    /// カメラの前方へ伸びる薄い直方体の範囲にある切れる CuttableObject をまとめて切断する Adapter。
    /// 範囲にある装甲のパネルには、振りのダメージの種類で1回ずつ当てる。パネルは切断しないので、かけらは出ない。
    /// 破壊タイプを含む振りでは、範囲にあるボクセルのピースを刃の平面で切り分ける（VoxelPiece.Slice）。手付かずの形のメッシュのまま置いたモデルは、ボクセルにしてから切り分ける。
    /// カメラから見て装甲の奥にある対象（ディフェンダーのバリアの中の敵など）は切らない。
    /// </summary>
    public sealed class MeleeCutAdapter : InitializableMonoBehaviour
    {
        /// <summary> 範囲内から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 128;

        private readonly Collider[] _hitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly List<CuttableObject> _targets = new();
        private readonly HashSet<ArmorPanel> _armorPanels = new();
        private readonly HashSet<VoxelPiece> _pieces = new();

        [SerializeField]
        [Tooltip("切断に使う刃。プールと同じシーンに置かれたもの")]
        private MultiCutBlade _blade;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、カメラから前方への距離（m）")]
        private float _reach = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、刃に沿った横方向の幅（m）")]
        private float _width = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、刃の法線方向の厚み（m）")]
        private float _thickness = 0.2f;

        [SerializeField]
        [Tooltip("切断の対象を探すレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField]
        [Tooltip("装甲のレイヤー。カメラから見てこのレイヤーの当たり判定の奥にある対象は切らない")]
        private LayerMask _armorLayers;

        /// <summary> 切断の結果と、振ったときの切断面（ワールド座標）を渡す先 </summary>
        private Action<MultiCutResult[], Plane> _onCut;

        /// <summary> 切断を実行中か。実行中の振りは無視する </summary>
        private bool _isCutting;

        /// <summary>
        /// Renderer のバウンディングボックスが、指定した点を通り法線に垂直な平面をまたぐかどうか。
        /// 平面と交わらない対象を切ると、中身がすべて入ったかけらと空のかけらができる為、切る前に除く。
        /// </summary>
        private static bool IsStraddlingPlane(CuttableObject cuttable, Vector3 planePoint, Vector3 normal)
        {
            if (cuttable.Renderer == null) return false;

            var bounds = cuttable.Renderer.bounds;
            var extents = bounds.extents;
            var radius = Mathf.Abs(normal.x) * extents.x + Mathf.Abs(normal.y) * extents.y +
                         Mathf.Abs(normal.z) * extents.z;
            var distance = Vector3.Dot(normal, bounds.center - planePoint);

            return Mathf.Abs(distance) < radius;
        }

        /// <summary>
        /// 回転した箱を包む、軸平行の箱を返す。
        /// </summary>
        private static Bounds GetRotatedBounds(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            var axisX = rotation * new Vector3(halfExtents.x, 0f, 0f);
            var axisY = rotation * new Vector3(0f, halfExtents.y, 0f);
            var axisZ = rotation * new Vector3(0f, 0f, halfExtents.z);
            var extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        /// <summary> 切断を実行中か </summary>
        public bool IsCutting => _isCutting;

        /// <summary>
        /// 切断が終わったときに呼ぶ処理を足す。Initialize で渡した処理のあとに呼ばれる。
        /// </summary>
        /// <param name="onCut">切断の結果と切断面を受け取る処理。切断面の法線は表のかけらの側を向く</param>
        public void AddOnCut(Action<MultiCutResult[], Plane> onCut)
        {
            _onCut += onCut;
        }

        /// <param name="onCut">切断が終わったときに、切断の結果と切断面を渡す関数。切断面の法線は表のかけらの側を向く</param>
        public void Initialize(Action<MultiCutResult[], Plane> onCut)
        {
            _onCut = onCut;

            if (_blade == null)
            {
                UsefulLogger.LogError("MultiCutBlade が設定されていません。", this);
            }

            base.Initialize();
        }

        /// <summary>
        /// 振ったときの角度で刃を配置し、範囲内の切れる対象を切断する。範囲内の装甲のパネルには1回ずつ当てる。
        /// 破壊タイプを含むなら、範囲内のボクセルのピースを刃の平面で切り分ける。
        /// </summary>
        /// <param name="angle">切断面の角度（度）</param>
        /// <param name="rangeScale">範囲の幅と届く距離に掛ける倍率</param>
        /// <param name="damageType">振りが持つダメージの種類</param>
        public void Swing(float angle, float rangeScale, DamageType damageType)
        {
            if (!Initialized || _blade == null) return;

            if (_isCutting)
            {
                UsefulLogger.Log("前の切断を実行中の為、今回の振りでは切断しませんでした。", this);
                return;
            }

            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、切断できません。", this);
                return;
            }

            var cameraTransform = cameraMain.transform;
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;
            var normal = Quaternion.AngleAxis(angle, forward) * cameraTransform.up;
            var bladeRotation = Quaternion.LookRotation(forward, normal);

            var slicesVoxels = (damageType & DamageType.Destroy) != 0;
            CollectTargets(origin, forward, normal, bladeRotation, rangeScale, slicesVoxels);

            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyDamage(damageType);
            }

            _armorPanels.Clear();

            var planeNormal = bladeRotation * Vector3.up;
            foreach (var piece in _pieces)
            {
                piece.Slice(origin, planeNormal);
            }

            _pieces.Clear();
            if (_targets.Count == 0) return;

            _blade.transform.SetPositionAndRotation(origin, bladeRotation);
            CutAsync(_targets.ToArray(), new Plane(planeNormal, origin)).Forget();
        }

        /// <summary>
        /// 指定した対象を、指定した面で切断する。切断中なら終わるのを待ってから切る。
        /// 切断は 1 つの刃を共有していて同時に実行できない為、通常攻撃以外の切断もここを通す。結果は通常攻撃と同じく切断の処理に渡される。
        /// </summary>
        /// <param name="targets">切る対象</param>
        /// <param name="point">切断面が通る点（ワールド空間）</param>
        /// <param name="normal">切断面の法線（正規化済み）</param>
        public async UniTask CutTargets(CuttableObject[] targets, Vector3 point, Vector3 normal)
        {
            if (!Initialized || _blade == null || targets == null || targets.Length == 0) return;

            while (_isCutting)
            {
                await UniTask.Yield(destroyCancellationToken);
            }

            _blade.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, normal));
            await CutAsync(targets, new Plane(normal, point));
        }

        /// <summary>
        /// 範囲内にあり、今切れる状態で、Renderer のバウンディングボックスが刃の平面をまたぎ、装甲の奥にない CuttableObject を _targets に集める。
        /// Brilliant Skill の言葉（BrilliantWord）は文字が当たり判定を持たない為、言葉の箱が範囲に入っていたら、その文字を同じ条件で集める。
        /// 範囲内の装甲のパネルは _armorPanels に集める。collectsPieces なら、範囲内にあり装甲の奥にないボクセルのピースを _pieces に集める。
        /// </summary>
        private void CollectTargets(Vector3 origin, Vector3 forward, Vector3 normal, Quaternion bladeRotation,
            float rangeScale, bool collectsPieces)
        {
            _targets.Clear();
            _armorPanels.Clear();
            _pieces.Clear();

            var reach = _reach * rangeScale;
            var center = origin + forward * (reach * 0.5f);
            var halfExtents = new Vector3(_width * rangeScale * 0.5f, _thickness * 0.5f, reach * 0.5f);
            var hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _hitBuffer, bladeRotation,
                _targetLayers, QueryTriggerInteraction.Ignore);

            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"切断の範囲内のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (_hitBuffer[i].TryGetComponent(out ArmorPanel armorPanel))
                {
                    _armorPanels.Add(armorPanel);
                    continue;
                }

                if (collectsPieces && TryCollectPieces(_hitBuffer[i], origin, center, halfExtents, bladeRotation))
                {
                    continue;
                }

                if (_hitBuffer[i].TryGetComponent(out BrilliantWord word))
                {
                    foreach (var letter in word.Letters)
                    {
                        TryAddTarget(letter.Cuttable, origin, normal);
                    }

                    continue;
                }

                if (_hitBuffer[i].TryGetComponent(out CuttableObject cuttable)) TryAddTarget(cuttable, origin, normal);
            }
        }

        /// <summary>
        /// 今切れる状態で、Renderer のバウンディングボックスが刃の平面をまたぎ、装甲の奥にない CuttableObject を _targets に加える。
        /// </summary>
        private void TryAddTarget(CuttableObject cuttable, Vector3 origin, Vector3 normal)
        {
            if (cuttable == null || !cuttable.IsCuttable || _targets.Contains(cuttable)) return;
            if (!IsStraddlingPlane(cuttable, origin, normal) || IsBehindArmor(origin, cuttable)) return;

            _targets.Add(cuttable);
        }

        /// <summary>
        /// コライダーがボクセルのピースか、手付かずの形のメッシュのまま置いたモデルのものなら、切り分けるピースを _pieces に集める。
        /// モデルはボクセルにしてから、範囲の箱を包む軸平行の箱と交わる部分（パーツ）だけを集める。装甲の奥にあるピースは集めない。
        /// </summary>
        /// <returns>コライダーがボクセルのものなら true</returns>
        private bool TryCollectPieces(Collider hit, Vector3 origin, Vector3 center, Vector3 halfExtents,
            Quaternion bladeRotation)
        {
            var piece = hit.GetComponentInParent<VoxelPiece>();
            if (piece != null)
            {
                if (!IsBehindArmor(origin, piece.WorldBounds.center)) _pieces.Add(piece);
                return true;
            }

            var loader = hit.GetComponentInParent<VoxelModelLoader>();
            if (loader == null || !loader.IsStatic) return false;

            loader.Load();
            var range = GetRotatedBounds(center, halfExtents, bladeRotation);
            foreach (var part in loader.Parts)
            {
                if (part.WorldBounds.Intersects(range) && !IsBehindArmor(origin, part.WorldBounds.center))
                {
                    _pieces.Add(part);
                }
            }

            return true;
        }

        /// <summary>
        /// カメラの位置 origin から対象の Renderer のバウンディングボックスの中心までの間に、装甲の当たり判定があるか。
        /// </summary>
        private bool IsBehindArmor(Vector3 origin, CuttableObject cuttable)
        {
            return IsBehindArmor(origin, cuttable.Renderer.bounds.center);
        }

        /// <summary>
        /// カメラの位置 origin から target までの間に、装甲の当たり判定があるか。
        /// </summary>
        private bool IsBehindArmor(Vector3 origin, Vector3 target)
        {
            var toTarget = target - origin;
            return Physics.Raycast(origin, toTarget, toTarget.magnitude, _armorLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 対象を切断し、終わったら結果と切断面を渡す。
        /// </summary>
        private async UniTask CutAsync(CuttableObject[] targets, Plane plane)
        {
            _isCutting = true;

            try
            {
                var results = await _blade.ExecuteCut(targets);
                _onCut?.Invoke(results, plane);
            }
            finally
            {
                _isCutting = false;
            }
        }
    }
}
