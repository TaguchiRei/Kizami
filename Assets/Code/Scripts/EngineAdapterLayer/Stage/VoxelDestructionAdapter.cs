using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UnityEngine.VFX;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// MainCamera を基準にした形（狙った所の球、視線の向きへ伸びるカプセル、カメラの位置を中心にした球）と、指定した点を中心にした球の範囲にあるボクセルのピースを、同じ形でまとめて削る Adapter。
    /// 範囲が複数のピースにまたがるときも、範囲内のすべてのピースを削る。手付かずの形のメッシュのまま置いたモデル（VoxelModelLoader.LoadStatic）に当たったら、ボクセルにしてから削る。
    /// 攻撃は強さと減衰率で当て、普通の強度の素材がちょうど指定の半径で削れる強さにする。柔い素材は深く、硬い素材は浅く削れる。
    /// 破壊タイプの攻撃として、装甲のパネルに当たったら1回当たったものとし、装甲の奥のピースは削らない。
    /// ガラスの板は削らずに割る。割る範囲は、攻撃の半径に、窓の周りの壁の厚さ分の余白を足したもの。周りの壁を削る攻撃でガラスも割れ、壁が消えてガラスだけ宙に残ることを防ぐ。
    /// 素材の強度で広げた削る範囲は使わない。柔い素材まで届く範囲は広すぎ、離れた窓まで割れる為。
    /// </summary>
    public sealed class VoxelDestructionAdapter : MonoBehaviour
    {
        /// <summary> 削る範囲から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 64;

        /// <summary> 削る範囲から集めるコライダーの数の上限。1つのピースが数千個の箱を持つので、足りなければここまで増やす </summary>
        private const int MAX_GROWN_HIT_COUNT = 16384;

        /// <summary> 破壊力を最大にしたときに強さへ足す値。硬い素材の融点 2 と普通の素材の融点 1 の差 </summary>
        private const float MAX_POWER_BONUS = 1f;

        private static readonly int _explosionEventId = Shader.PropertyToID("OnExplosion");
        private static readonly int _positionId = Shader.PropertyToID("position");
        private static readonly int _sizeId = Shader.PropertyToID("size");
        private static readonly int _spawnCountId = Shader.PropertyToID("spawnCount");

        private Collider[] _hitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly Collider[] _armorHitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly HashSet<VoxelPiece> _pieces = new();
        private readonly HashSet<ArmorPanel> _armorPanels = new();

        [SerializeField, Min(0f)]
        [Tooltip("狙える最大の距離（m）")]
        private float _maxDistance = 50f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る球の半径（m）")]
        private float _radius = 1.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("狙った所を削る攻撃の、1m あたりに減る強さ。小さいほど素材の強度による削れ方の差が大きい")]
        private float _aimFalloff = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("ビームの、1m あたりに減る強さ")]
        private float _beamFalloff = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("爆発の、1m あたりに減る強さ")]
        private float _explosionFalloff = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("ガラスの板を割る範囲に、攻撃の半径へ足す余白（m）。窓の周りの壁の厚さに合わせる")]
        private float _glassMargin = 1.2f;

        [SerializeField]
        [Tooltip("狙う先と、削るピースを探すレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField]
        [Tooltip("装甲のパネルのレイヤー。遮断の判定と、当たったパネルの収集に使う")]
        private LayerMask _armorLayers;

        [SerializeField]
        [Tooltip("爆発を描く VisualEffect。VFX Graph は Explosion.vfx。未設定なら爆発は削るだけで何も出さない")]
        private VisualEffect _explosionEffect;

        [SerializeField, Min(1)]
        [Tooltip("爆発の半径 1m あたりに出す粒の数")]
        private int _explosionParticlesPerMeter = 60;

        /// <summary> 爆発の位置・半径・粒の数を VFX Graph へ渡すイベント属性 </summary>
        private VFXEventAttribute _explosionEventAttribute;

        /// <summary> 1回の攻撃で壊したボクセルの体積（m3）を受け取る処理 </summary>
        private Action<float> _onCarved;

        /// <summary> 強さに足す値。破壊力を最大にしていなければ 0 </summary>
        private float _powerBonus;

        /// <summary>
        /// 普通の強度の素材を radius で削る強さのとき、融点0の素材まで届く半径。
        /// 強さ P は 普通の融点 + 減衰率 × radius で、届く半径は P ÷ 減衰率。
        /// </summary>
        private float ReachRadius(float radius, float falloff)
        {
            return radius + (VoxelThermalSettings.NORMAL_MELTING_POINT + _powerBonus) / falloff;
        }

        /// <summary>
        /// 破壊力を最大にするかを設定する。最大にすると、強さに硬い素材と普通の素材の融点の差を足し、硬い素材も普通の素材と同じ半径まで削れる。
        /// </summary>
        public void SetMaxPower(bool isMax)
        {
            _powerBonus = isMax ? MAX_POWER_BONUS : 0f;
        }

        /// <summary>
        /// 攻撃でボクセルを壊したときに呼ぶ処理を設定する。1回の攻撃で複数のピースを壊したら、体積を合計して1回だけ呼ぶ。壊れなかった攻撃では呼ばない。
        /// </summary>
        /// <param name="onCarved">壊したボクセルの体積（m3）を受け取る処理</param>
        public void SetOnCarved(Action<float> onCarved)
        {
            _onCarved = onCarved;
        }

        /// <summary>
        /// 狙った所を球で削る。狙える距離に何もなければ削らない。
        /// レイが最初に当たったのが装甲のパネルなら、そのパネルに1回当てて削らない。
        /// </summary>
        public void CarveAtAim()
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit, _maxDistance,
                    _targetLayers | _armorLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (hit.collider.TryGetComponent(out ArmorPanel armorPanel))
            {
                armorPanel.ApplyDamage(DamageType.Destroy);
                return;
            }

            GlassPane.ShatterInSphere(hit.point, _radius + _glassMargin, hit.point);

            var reach = ReachRadius(_radius, _aimFalloff);
            var hitCount = QuerySphere(hit.point, reach);
            Carve(new SphereShape(hit.point, reach), _aimFalloff, hitCount);
        }

        /// <summary>
        /// カメラの位置から視線の向きへ伸びるカプセルで削る。途中の壁を貫通する。
        /// 中心のレイが装甲のパネルに当たったら、そのパネルに1回当て、カプセルの先端がその点に届く所までで止める。
        /// </summary>
        /// <param name="length">カプセルの長さ（m）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        public void CarveBeam(float length, float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var start = cameraTransform.position;
            var forward = cameraTransform.forward;

            if (Physics.Raycast(start, forward, out var armorHit, length, _armorLayers, QueryTriggerInteraction.Ignore))
            {
                if (armorHit.collider.TryGetComponent(out ArmorPanel armorPanel))
                {
                    armorPanel.ApplyDamage(DamageType.Destroy);
                }

                // カプセルの端の半球が装甲を越えないよう、中心の線を半径の分だけ手前で止める
                length = armorHit.distance - radius;
                if (length <= 0f) return;
            }

            var end = start + forward * length;
            GlassPane.ShatterInCapsule(start, end, radius + _glassMargin, start);

            var reach = ReachRadius(radius, _beamFalloff);
            var hitCount = QueryCapsule(start, end, reach);
            Carve(new CapsuleShape(start, end, reach), _beamFalloff, hitCount);
        }

        /// <summary>
        /// カメラの位置を中心に球で削る。足場がボクセルなら足場も削れる。装甲への当て方は CarveExplosionAt と同じ。
        /// </summary>
        /// <param name="radius">球の半径（m）</param>
        public void CarveExplosion(float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            CarveExplosionAt(cameraTransform.position, radius);
        }

        /// <summary>
        /// カメラの正面に、水平の向きの横長のカプセルで削る。カメラの向きは水平に直して使う。装甲への当て方は CarveCapsuleWithArmor と同じ。
        /// </summary>
        /// <param name="width">横幅（m）</param>
        /// <param name="depth">奥行き（m）。カプセルの直径になり、カメラの位置から奥行きの分だけ前までを削る</param>
        /// <param name="falloff">1m あたりに減る強さ。1 ÷ 半径以下なら硬い素材は削れない</param>
        /// <param name="forwardOffset">範囲の中心を、さらに正面へずらす距離（m）</param>
        public void CarveSweep(float width, float depth, float falloff, float forwardOffset)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return;

            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            var radius = depth * 0.5f;
            var center = cameraTransform.position + forward * (radius + forwardOffset);
            var halfAxis = Mathf.Max(width * 0.5f - radius, 0f);

            CarveCapsuleWithArmor(center - right * halfAxis, center + right * halfAxis, radius, cameraTransform.position,
                falloff);
        }

        /// <summary>
        /// 指定した2点を結ぶカプセルで削る。ガラスの板は割る。
        /// カプセルに入った装甲のパネルすべてに1回ずつ当て、origin から見て装甲の奥にあるピースは削らない。
        /// 強さは普通の融点と減衰率から決め、破壊力を最大にしているときは上乗せを足す。
        /// </summary>
        /// <param name="start">カプセルの一方の端（ワールド空間）</param>
        /// <param name="end">カプセルのもう一方の端（ワールド空間）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        /// <param name="origin">装甲の遮断を判定するレイの始点</param>
        /// <param name="falloff">1m あたりに減る強さ。普通の素材は半径まで削れる。硬い素材は、減衰率 × 半径が 1 を超えると少しだけ削れる</param>
        public void CarveCapsuleWithArmor(Vector3 start, Vector3 end, float radius, Vector3 origin, float falloff)
        {
            CollectArmorPanels(Physics.OverlapCapsuleNonAlloc(start, end, radius, _armorHitBuffer, _armorLayers,
                QueryTriggerInteraction.Ignore));
            GlassPane.ShatterInCapsule(start, end, radius + _glassMargin, origin);

            var reach = ReachRadius(radius, falloff);
            var hitCount = QueryCapsule(start, end, reach);

            // 遮断の判定は、当てて壊れる前のパネルで行う
            Carve(new CapsuleShape(start, end, reach), falloff, hitCount, _armorPanels.Count > 0, origin);

            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyDamage(DamageType.Destroy);
            }

            _armorPanels.Clear();
        }

        /// <summary>
        /// 指定した点を中心に球で削る。
        /// 球に入った装甲のパネルすべてに1回ずつ当て、中心から見て装甲の奥にあるピースは削らない。
        /// 遮断は中心から各ピースのバウンディングボックスの中心へのレイで判定するので、ピースの一部だけが装甲の陰にあっても、ピース全体を削るか削らないかのどちらかになる。
        /// </summary>
        /// <param name="center">球の中心（ワールド空間）</param>
        /// <param name="radius">球の半径（m）</param>
        public void CarveExplosionAt(Vector3 center, float radius)
        {
            CollectArmorPanels(Physics.OverlapSphereNonAlloc(center, radius, _armorHitBuffer, _armorLayers,
                QueryTriggerInteraction.Ignore));
            GlassPane.ShatterInSphere(center, radius + _glassMargin, center);
            PlayExplosionEffect(center, radius);

            var reach = ReachRadius(radius, _explosionFalloff);
            var hitCount = QuerySphere(center, reach);

            // 遮断の判定は、当てて壊れる前のパネルで行う
            Carve(new SphereShape(center, reach), _explosionFalloff, hitCount, _armorPanels.Count > 0, center);

            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyDamage(DamageType.Destroy);
            }

            _armorPanels.Clear();
        }

        /// <summary>
        /// 羽天轟号の 1 回の判定で、center を中心に球で削る。CarveExplosionAt と違い、球に入った装甲（バリアを含む）は粉砕で一撃で壊し、装甲の奥も削る。
        /// 体力制の対象には当たりを報告しない（体力は羽天轟号が秒間のダメージで減らす）。
        /// </summary>
        /// <param name="center">球の中心（ワールド空間）</param>
        /// <param name="radius">球の半径（m）</param>
        public void CarveSkyBeamAt(Vector3 center, float radius)
        {
            CollectArmorPanels(Physics.OverlapSphereNonAlloc(center, radius, _armorHitBuffer, _armorLayers,
                QueryTriggerInteraction.Ignore));
            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyDamage(DamageType.Shatter);
            }

            _armorPanels.Clear();
            GlassPane.ShatterInSphere(center, radius + _glassMargin, center);

            var reach = ReachRadius(radius, _explosionFalloff);
            var hitCount = QuerySphere(center, reach);
            Carve(new SphereShape(center, reach), _explosionFalloff, hitCount, reportsHealthHit: false);
        }

        /// <summary>
        /// 爆発の火花を center に出す。VFX Graph の粒は、イベント属性の size を半径として、その大きさに広がる。
        /// </summary>
        private void PlayExplosionEffect(Vector3 center, float radius)
        {
            if (_explosionEffect == null) return;

            _explosionEventAttribute ??= _explosionEffect.CreateVFXEventAttribute();
            _explosionEventAttribute.SetVector3(_positionId, center);
            _explosionEventAttribute.SetFloat(_sizeId, radius);
            _explosionEventAttribute.SetFloat(_spawnCountId, radius * _explosionParticlesPerMeter);
            _explosionEffect.SendEvent(_explosionEventId, _explosionEventAttribute);
        }

        private bool TryGetCameraTransform(out Transform cameraTransform)
        {
            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、削れません。", this);
                cameraTransform = null;
                return false;
            }

            cameraTransform = cameraMain.transform;
            return true;
        }

        /// <summary>
        /// 指定した2点を結ぶカプセルで削る。ガラスの板は割る。装甲は扱わない。倒れた塔が通った跡を削るのに使う。
        /// </summary>
        /// <param name="start">カプセルの一方の端（ワールド空間）</param>
        /// <param name="end">カプセルのもう一方の端（ワールド空間）</param>
        /// <param name="radius">カプセルの半径（m）。普通の強度の素材がちょうどこの半径で削れる</param>
        /// <param name="notifiesCarved">削った体積を SetOnCarved の通知先へ伝えるか。ボスの演出のように、プレイヤーの手柄にしない破壊では false</param>
        public void CarveCapsuleAt(Vector3 start, Vector3 end, float radius, bool notifiesCarved = true)
        {
            GlassPane.ShatterInCapsule(start, end, radius + _glassMargin, start);

            var reach = ReachRadius(radius, _explosionFalloff);
            var hitCount = QueryCapsule(start, end, reach);
            Carve(new CapsuleShape(start, end, reach), _explosionFalloff, hitCount, notifiesCarved: notifiesCarved);
        }

        /// <summary>
        /// 球の範囲のコライダーを _hitBuffer に集める。バッファが埋まったら、上限まで倍にして集め直す。
        /// 上限が小さいと、1つのピースの箱だけで埋まり、範囲内の別のピースを削り損ねる為。
        /// </summary>
        private int QuerySphere(Vector3 center, float radius)
        {
            while (true)
            {
                var count = Physics.OverlapSphereNonAlloc(center, radius, _hitBuffer, _targetLayers,
                    QueryTriggerInteraction.Ignore);
                if (count < _hitBuffer.Length || _hitBuffer.Length >= MAX_GROWN_HIT_COUNT) return count;

                Array.Resize(ref _hitBuffer, _hitBuffer.Length * 2);
            }
        }

        /// <summary>
        /// カプセルの範囲のコライダーを _hitBuffer に集める。バッファの増やし方は QuerySphere と同じ。
        /// </summary>
        private int QueryCapsule(Vector3 start, Vector3 end, float radius)
        {
            while (true)
            {
                var count = Physics.OverlapCapsuleNonAlloc(start, end, radius, _hitBuffer, _targetLayers,
                    QueryTriggerInteraction.Ignore);
                if (count < _hitBuffer.Length || _hitBuffer.Length >= MAX_GROWN_HIT_COUNT) return count;

                Array.Resize(ref _hitBuffer, _hitBuffer.Length * 2);
            }
        }

        /// <summary>
        /// _armorHitBuffer に集めた装甲のコライダーから、装甲のパネルを _armorPanels に集める。
        /// </summary>
        /// <param name="hitCount">_armorHitBuffer に集めたコライダーの数</param>
        private void CollectArmorPanels(int hitCount)
        {
            _armorPanels.Clear();

            if (hitCount == _armorHitBuffer.Length)
            {
                UsefulLogger.LogWarning($"範囲内の装甲のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (_armorHitBuffer[i].TryGetComponent(out ArmorPanel armorPanel)) _armorPanels.Add(armorPanel);
            }
        }

        /// <summary>
        /// 範囲の問い合わせで集めたコライダーからピースを集め、強さと減衰率の攻撃を当てる。
        /// </summary>
        /// <param name="reach">融点0の素材まで届く範囲（ワールド空間）</param>
        /// <param name="falloff">1m あたりに減る強さ</param>
        /// <param name="hitCount">_hitBuffer に集めたコライダーの数</param>
        /// <param name="checkArmor">true なら、origin から見て装甲の奥にあるピースを削らない</param>
        /// <param name="origin">装甲の遮断を判定するレイの始点</param>
        /// <param name="reportsHealthHit">体力制の対象を削ったときに、スキルの当たりとして体力を減らすか。体力を別の口で減らす攻撃では false</param>
        private void Carve<TShape>(in TShape reach, float falloff, int hitCount, bool checkArmor = false,
            Vector3 origin = default, bool notifiesCarved = true, bool reportsHealthHit = true)
            where TShape : struct, ITransformableVoxelShape<TShape>
        {
            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"削る範囲内のコライダーが上限（{_hitBuffer.Length}）に達しました。", this);
            }

            _pieces.Clear();
            for (var i = 0; i < hitCount; i++)
            {
                var piece = _hitBuffer[i].GetComponentInParent<VoxelPiece>();
                if (piece != null)
                {
                    _pieces.Add(piece);
                    continue;
                }

                // 手付かずの形のメッシュのまま置いたモデルは、当たったときにボクセルにしてから削る
                var loader = _hitBuffer[i].GetComponentInParent<VoxelModelLoader>();
                if (loader == null || !loader.IsStatic) continue;

                loader.Load();
                foreach (var part in loader.Parts)
                {
                    _pieces.Add(part);
                }
            }

            var carvedVolume = 0f;
            var carvedPosition = Vector3.zero;
            HealthTarget hitHealthTarget = null;
            foreach (var piece in _pieces)
            {
                if (checkArmor && IsBehindArmor(origin, piece.WorldBounds.center)) continue;

                if (hitHealthTarget == null) hitHealthTarget = piece.GetComponentInParent<HealthTarget>();
                var pieceVolume = piece.ApplyStrike(reach, falloff, Space.World) * piece.VoxelCubicVolume;
                if (pieceVolume > 0f && carvedVolume <= 0f) carvedPosition = piece.WorldBounds.center;
                carvedVolume += pieceVolume;
            }

            _pieces.Clear();
            if (notifiesCarved && carvedVolume > 0f)
            {
                SoundPlayer.Play(SoundEffectKind.VoxelHit, carvedPosition);
                _onCarved?.Invoke(carvedVolume);
            }

            // 倒壊の跡を削る塔のように、プレイヤーの攻撃でない破壊は体力を減らさない
            if (notifiesCarved && reportsHealthHit && hitHealthTarget != null) hitHealthTarget.ReportSkillHit();
        }

        /// <summary>
        /// origin から target へのレイが、途中で装甲のパネルに当たるか。
        /// </summary>
        private bool IsBehindArmor(Vector3 origin, Vector3 target)
        {
            var toTarget = target - origin;
            return Physics.Raycast(origin, toTarget, toTarget.magnitude, _armorLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
