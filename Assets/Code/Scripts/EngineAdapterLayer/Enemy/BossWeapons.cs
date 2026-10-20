using System;
using System.Collections.Generic;
using System.Text;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの武装ごとの破壊と、同じ種類の武装を使う順番を扱う。ボスのボクセルのモデル（VoxelModelLoader）と同じ GameObject に付ける。
    /// 武装は、パーツの体積の合計のうち、読み込んだ時点から必要な割合以上が削れたら破壊とし、一度壊れたら戻らない。
    /// 本体から分離した塊はパーツの体積に含まれない為、切り離した分も削れた分に数える（DestructionTarget と同じ測り方）。
    /// </summary>
    /// <remarks>
    /// ダウンの間に壊すときは BreakBest で 1 つ選び、そのパーツを本体から外して落とす（体積の割合によらず壊れたとする）。
    /// 使う順番は種類ごとに回り、壊れた武装の番も飛ばさない。壊れた武装の番では撃たずにクールタイムだけを発生させる仕様の為で、
    /// 撃つかどうかとクールタイムは、TakeNext で受け取った武装の IsBroken を見て呼び出し側が決める。
    /// </remarks>
    [RequireComponent(typeof(VoxelModelLoader))]
    public sealed class BossWeapons : MonoBehaviour
    {
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _colorId = Shader.PropertyToID("_Color");

        [SerializeField]
        [Tooltip("武装")]
        private BossWeapon[] _weapons = Array.Empty<BossWeapon>();

        [SerializeField, Range(0f, 1f)]
        [Tooltip("武装のパーツの体積の合計のうち、読み込んだ時点から削れたら破壊とする割合")]
        private float _brokenRatio = 0.05f;

        [SerializeField]
        [Tooltip("ダウンの間に、壊せる武装を塗る色")]
        private Color _highlightColor = new(1f, 0.15f, 0.15f, 1f);

        [SerializeField, Min(0f)]
        [Tooltip("壊す武装を選ぶとき、画面の中央からの角度の差がこの値（度）以内なら、プレイヤーに近い方を選ぶ")]
        private float _tieAngle = 10f;

        [SerializeField, Min(0.01f)]
        [Tooltip("外れて落ちる武装の、体積1m³あたりの質量")]
        private float _detachedDensity = 1f;

        /// <summary> 武装ごとの、読み込んだパーツ </summary>
        private List<VoxelPiece>[] _parts;

        /// <summary> 武装ごとの、読み込んだ時点のパーツの体積の合計（m³）。まだ測っていなければ 0 </summary>
        private float[] _initialVolumes;

        /// <summary> 武装ごとの、壊れたか </summary>
        private bool[] _isBroken;

        /// <summary> 種類ごとの、次に使う武装を探し始める番号 </summary>
        private int[] _cursors;

        private IDisposable _loadedSubscription;
        private MaterialPropertyBlock _highlightBlock;

        /// <summary> 武装の数 </summary>
        public int Count => _weapons.Length;

        /// <summary>
        /// 種類 kind の武装の数を返す。
        /// </summary>
        public int CountOf(BossWeaponKind kind)
        {
            var count = 0;
            foreach (var weapon in _weapons)
            {
                if (weapon.Kind == kind) count++;
            }

            return count;
        }

        /// <summary>
        /// 壊れている武装の数を返す。壊れたかを、今の体積で確かめ直してから数える。
        /// </summary>
        public int CountBroken()
        {
            var count = 0;
            for (var i = 0; i < _weapons.Length; i++)
            {
                if (IsBroken(i)) count++;
            }

            return count;
        }

        /// <summary>
        /// 武装 index が壊れているか。壊れたかを、今の体積で確かめ直してから返す。
        /// </summary>
        /// <param name="index">武装の番号（設定の並び）</param>
        public bool IsBroken(int index)
        {
            if (!_isBroken[index] && TryGetDestroyedRatio(index, out var ratio) && ratio >= _brokenRatio)
            {
                _isBroken[index] = true;
            }

            return _isBroken[index];
        }

        /// <summary>
        /// 武装 index の最初のパーツの位置（ワールド座標）を返す。パーツが残っていなければ、ボスの根の位置。
        /// </summary>
        /// <param name="index">武装の番号（設定の並び）</param>
        public Vector3 GetPosition(int index)
        {
            foreach (var part in _parts[index])
            {
                if (part != null) return part.transform.position;
            }

            return transform.position;
        }

        /// <summary>
        /// 種類 kind の武装のうち、順番で次に使う武装の番号を返し、順番を1つ進める。壊れた武装も番が来れば返す。
        /// その種類の武装がなければ -1。
        /// </summary>
        public int TakeNext(BossWeaponKind kind)
        {
            var count = _weapons.Length;
            var cursor = _cursors[(int)kind];

            for (var offset = 0; offset < count; offset++)
            {
                var index = (cursor + offset) % count;
                if (_weapons[index].Kind != kind) continue;

                _cursors[(int)kind] = (index + 1) % count;
                return index;
            }

            return -1;
        }

        /// <summary>
        /// 壊れていない武装のうち、画面の中央に最も近い武装を 1 つ選んで壊す。画面の中央からの角度の差が _tieAngle 以内なら、プレイヤーに近い方を選ぶ。
        /// </summary>
        /// <param name="viewOrigin">カメラの位置（ワールド座標）</param>
        /// <param name="viewForward">カメラの正面の向き</param>
        /// <param name="playerPosition">プレイヤーの位置（ワールド座標）</param>
        /// <returns>壊した武装の番号。壊せる武装がなければ -1</returns>
        public int BreakBest(Vector3 viewOrigin, Vector3 viewForward, Vector3 playerPosition)
        {
            var best = -1;
            var bestAngle = 0f;
            var bestDistance = 0f;
            for (var i = 0; i < _weapons.Length; i++)
            {
                if (IsBroken(i) || _parts[i].Count == 0) continue;

                var position = GetPosition(i);
                var angle = Vector3.Angle(viewForward, position - viewOrigin);
                var distance = (position - playerPosition).sqrMagnitude;
                var isBetter = best < 0 || angle < bestAngle - _tieAngle ||
                               (angle <= bestAngle + _tieAngle && distance < bestDistance);
                if (!isBetter) continue;

                best = i;
                bestAngle = angle;
                bestDistance = distance;
            }

            if (best >= 0) Break(best);
            return best;
        }

        /// <summary>
        /// 壊れていない武装のパーツを、_highlightColor で塗るか、元の色に戻す。
        /// </summary>
        public void SetHighlight(bool isHighlighted)
        {
            for (var i = 0; i < _weapons.Length; i++)
            {
                if (!_isBroken[i]) SetHighlight(i, isHighlighted);
            }
        }

        /// <summary>
        /// 武装ごとの、種類・削れた割合・壊れたかを書き足す。
        /// </summary>
        public void AppendStatus(StringBuilder builder)
        {
            for (var i = 0; i < _weapons.Length; i++)
            {
                if (i > 0) builder.Append(" | ");
                builder.Append(_weapons[i].Kind).Append(i).Append(' ');
                builder.Append(TryGetDestroyedRatio(i, out var ratio) ? ratio.ToString("F2") : "-");
                if (IsBroken(i)) builder.Append(" broken");
            }
        }

        private void Awake()
        {
            _parts = new List<VoxelPiece>[_weapons.Length];
            _initialVolumes = new float[_weapons.Length];
            _isBroken = new bool[_weapons.Length];
            _cursors = new int[Enum.GetValues(typeof(BossWeaponKind)).Length];
            for (var i = 0; i < _parts.Length; i++) _parts[i] = new List<VoxelPiece>();

            // VoxelModelLoader は Start で読み込むので、Awake で登録すれば読み込みの通知を逃さない
            _loadedSubscription = GetComponent<VoxelModelLoader>().RegisterOnLoaded(OnLoaded);
        }

        private void OnDestroy()
        {
            _loadedSubscription?.Dispose();
        }

        /// <summary>
        /// 武装 index を壊したことにし、そのパーツを本体から外して落とす。
        /// パーツの当たり判定はメッシュそのままで動く Rigidbody に使えない為、無効にして、範囲を包む箱の当たり判定を付ける。
        /// </summary>
        private void Break(int index)
        {
            SetHighlight(index, false);
            _isBroken[index] = true;

            foreach (var part in _parts[index])
            {
                if (part == null) continue;

                var bounds = part.WorldBounds;
                var partTransform = part.transform;
                partTransform.SetParent(null, true);
                foreach (var meshCollider in part.GetComponentsInChildren<MeshCollider>())
                {
                    meshCollider.enabled = false;
                }

                var box = part.gameObject.AddComponent<BoxCollider>();
                var scale = partTransform.lossyScale;
                box.center = partTransform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(bounds.size.x / scale.x, bounds.size.y / scale.y, bounds.size.z / scale.z);

                var body = part.gameObject.AddComponent<Rigidbody>();
                body.mass = Mathf.Max(part.Volume * _detachedDensity, 0.01f);
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        /// <summary>
        /// 武装 index のパーツのチャンクの Renderer に、_highlightColor を塗るか、塗った色を外す。
        /// </summary>
        private void SetHighlight(int index, bool isHighlighted)
        {
            _highlightBlock ??= new MaterialPropertyBlock();
            _highlightBlock.Clear();
            if (isHighlighted)
            {
                _highlightBlock.SetColor(_baseColorId, _highlightColor);
                _highlightBlock.SetColor(_colorId, _highlightColor);
            }

            foreach (var part in _parts[index])
            {
                if (part == null) continue;

                foreach (var chunkRenderer in part.GetComponentsInChildren<MeshRenderer>())
                {
                    chunkRenderer.SetPropertyBlock(isHighlighted ? _highlightBlock : null);
                }
            }
        }

        /// <summary>
        /// 武装 index のパーツの体積の合計のうち、読み込んだ時点から削れた割合を返す。破棄されたパーツは体積 0 として数える。
        /// 読み込む前と、まだ体積を測っていないパーツがあるときは false。
        /// </summary>
        private bool TryGetDestroyedRatio(int index, out float ratio)
        {
            ratio = 0f;
            var parts = _parts[index];
            if (parts.Count == 0) return false;

            // 破棄されたパーツの体積は後から読めない為、すべてのパーツを測り終えた時点の合計を覚えておく
            if (_initialVolumes[index] <= 0f)
            {
                var initial = 0f;
                foreach (var part in parts)
                {
                    if (part == null || part.InitialSampleCount == 0) return false;
                    initial += part.InitialVolume;
                }

                _initialVolumes[index] = initial;
            }

            var current = 0f;
            foreach (var part in parts)
            {
                if (part != null) current += part.Volume;
            }

            ratio = _initialVolumes[index] > 0f ? 1f - current / _initialVolumes[index] : 1f;
            return true;
        }

        /// <summary>
        /// 武装ごとに、パーツのパスから読み込んだパーツを引き直す。
        /// </summary>
        private void OnLoaded(VoxelModelLoader loader)
        {
            for (var i = 0; i < _weapons.Length; i++)
            {
                _parts[i].Clear();
                _initialVolumes[i] = 0f;
                foreach (var path in _weapons[i].PartPaths)
                {
                    if (loader.TryGetPart(path, out var part))
                    {
                        _parts[i].Add(part);
                    }
                    else
                    {
                        UsefulLogger.LogError($"ボスの武装 {i} のパーツ '{path}' が {loader.name} のパーツに見つかりません。", this);
                    }
                }
            }
        }
    }
}
