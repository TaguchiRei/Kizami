using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵 1 体の切断できる体。敵のプレハブの根に付け、EnemySpawnAdapter が近くの敵（EnemyAgent）に貸して使い回す。
    /// 貸すたびに、敵の状態にある部位の状態（失った部位、壊れた部位）を反映し、残っている部位を MeshDataCache へ登録し直す。
    /// プールで非アクティブのまま待つ部位は、MeshDataCache の初期登録に含まれない為。
    /// 返すときと倒れたときは、全部位を切断前の形に戻してからプールへ戻る。
    /// フィニッシャーに吸収されるときは、マテリアルをディゾルブのものに差し替えて消し、返すときに元へ戻す。
    /// </summary>
    /// <remarks>
    /// 体に残す側は、かけらの形を元の部位へ移してから、かけらをプールへ返す。かけらのまま体に付けておくと、プールの回収で消える為。
    /// </remarks>
    public sealed class EnemyBody : MonoBehaviour
    {
        /// <summary> 部位の状態を敵の状態のビットに持つので、部位はこの数まで </summary>
        private const int MAX_PARTS = 32;

        private static readonly int _dissolveAmountId = Shader.PropertyToID("_DissolveAmount");

        [SerializeField]
        [Tooltip("体を作る部位")]
        private EnemyPart[] _parts;

        [SerializeField, Min(1)]
        [Tooltip("壊れた移動部位がこの数に達すると、移動しなくなる")]
        private int _brokenMovePartLimit = 4;

        [SerializeField]
        [Tooltip("宙に浮いているか。浮いている敵は、足場が壊れるとゆっくり落ち、落ちても倒れない")]
        private bool _isFloating;

        /// <summary> 部位ごとの、体から外れたか。並びは _parts と同じ </summary>
        private bool[] _isLost;

        /// <summary> 部位ごとの、壊れたことを役割へ伝えたか。並びは _parts と同じ </summary>
        private bool[] _isBroken;

        private int _brokenMovePartCount;

        /// <summary> 切断済みの部位をオーブにする関数。引数はオーブを出す位置 </summary>
        private Action<Vector3> _spawnOrb;

        /// <summary> 切っていない部位を見た目用の物で散らばらせる関数。引数は部位と、散らばる中心 </summary>
        private Action<CuttableObject, Vector3> _spawnDebris;

        /// <summary> 体のレンダラー（非アクティブのものも含む） </summary>
        private Renderer[] _renderers;

        /// <summary> レンダラーごとの、ディゾルブで差し替える前のマテリアル。並びは _renderers と同じ </summary>
        private Material[][] _originalMaterials;

        /// <summary> ディゾルブで消えた割合を渡す入れ物。MonoBehaviour のフィールドの初期化子では作れないので Awake で作る </summary>
        private MaterialPropertyBlock _propertyBlock;

        /// <summary> 敵に貸しているか。倒れるか返すと false になる </summary>
        public bool IsLent => gameObject.activeSelf;

        /// <summary> 壊れた移動部位がこの数に達すると、移動しなくなる </summary>
        public int BrokenMovePartLimit => _brokenMovePartLimit;

        /// <summary> 宙に浮いているか。浮いている敵は、足場が壊れるとゆっくり落ち、落ちても倒れない </summary>
        public bool IsFloating => _isFloating;

        /// <summary> 体を作る部位 </summary>
        public IReadOnlyList<EnemyPart> Parts => _parts;

        /// <summary> ディゾルブで消えている途中か。返すと false に戻る </summary>
        public bool IsDissolving { get; private set; }

        /// <summary>
        /// EnemySpawnAdapter が体を作ったときに 1 回だけ呼ぶ。
        /// </summary>
        /// <param name="spawnOrb">切断済みの部位をオーブにする関数。引数はオーブを出す位置</param>
        /// <param name="spawnDebris">切っていない部位を見た目用の物で散らばらせる関数。引数は部位と、散らばる中心</param>
        public void Initialize(Action<Vector3> spawnOrb, Action<CuttableObject, Vector3> spawnDebris)
        {
            _spawnOrb = spawnOrb;
            _spawnDebris = spawnDebris;
        }

        /// <summary>
        /// 敵の位置と向きで体を出し、敵の部位の状態を反映する。失った部位は隠し、残っている部位を切断できる状態にする。
        /// </summary>
        /// <param name="agent">体を貸す敵</param>
        /// <param name="cache">部位を登録し直す先</param>
        public void Lend(in EnemyAgent agent, MeshDataCache cache)
        {
            transform.SetPositionAndRotation(agent.Position, Quaternion.Euler(0f, math.degrees(agent.Yaw), 0f));
            gameObject.SetActive(true);
            _brokenMovePartCount = agent.BrokenMovePartCount;

            for (var i = 0; i < _parts.Length; i++)
            {
                _isLost[i] = (agent.LostParts & (1u << i)) != 0;
                _isBroken[i] = (agent.BrokenParts & (1u << i)) != 0;

                var cuttable = _parts[i].Cuttable;
                if (cuttable == null) continue;

                cuttable.gameObject.SetActive(!_isLost[i]);
                if (!_isLost[i]) cache.Register(cuttable);
            }
        }

        /// <summary>
        /// 体の部位の状態を、敵の状態へ書き戻す。倒れた体なら、敵をステージから消す。
        /// </summary>
        public void WriteState(ref EnemyAgent agent)
        {
            agent.LostParts = 0u;
            agent.BrokenParts = 0u;
            for (var i = 0; i < _parts.Length; i++)
            {
                if (_isLost[i]) agent.LostParts |= 1u << i;
                if (_isBroken[i]) agent.BrokenParts |= 1u << i;
            }

            agent.BrokenMovePartCount = _brokenMovePartCount;
            if (!IsLent) agent.IsAlive = false;
        }

        /// <summary>
        /// 体を敵から返す。ディゾルブの途中ならマテリアルを元へ戻し、全部位を切断前の形に戻して、非アクティブにしてプールへ戻す。
        /// 部位の状態は先に WriteState で敵へ書き戻し、短くなった部位の形は先に EnemyShapeKeeper へ預けておく。
        /// </summary>
        public void Return()
        {
            // 部位のマテリアルは RestoreInitialShape が初期のものに戻すので、その前に戻す
            if (IsDissolving) EndDissolve();

            foreach (var part in _parts)
            {
                if (part.Cuttable != null) part.Cuttable.RestoreInitialShape();
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 部位を切られた結果を受け取り、接続部の側を体に残す。接続部の近くを切ったときは、部位ごと失う。
        /// 切断面より向こう側にある子の部位は、部位ごと失う。そのあと、壊れた部位の役割の処理を呼ぶ。
        /// 倒れたあとの体と、すでに失った部位への結果は受け取らない。その表と裏のかけらは、そのままチャージになる。
        /// </summary>
        /// <param name="result">この体の部位を元の対象とする切断の結果</param>
        /// <param name="plane">振ったときの切断面。法線は表のかけらの側を向く。切断が終わるまでに体が動いた分だけずれる</param>
        /// <param name="fragmentPool">体に残す側のかけらを返す先</param>
        public void ReceiveCut(MultiCutResult result, Plane plane, MeshCutObjectPool fragmentPool)
        {
            if (!IsLent) return;

            var index = IndexOf(result.Original);
            if (index < 0 || _isLost[index]) return;

            var part = _parts[index];
            if (Mathf.Abs(plane.GetDistanceToPoint(part.Cuttable.transform.position)) <= part.JointDistance)
            {
                LosePart(index);
            }
            else
            {
                KeepJointSide(index, result, plane, fragmentPool);
            }

            NotifyBrokenParts(index);
        }

        /// <summary>
        /// 体を倒す。体に残った部位を切断済みならオーブに、切っていなければ見た目用の物にしてから、
        /// 全部位を切断前の形に戻し、体を非アクティブにしてプールへ返す。
        /// </summary>
        public void Defeat()
        {
            if (!IsLent) return;

            for (var i = 0; i < _parts.Length; i++)
            {
                if (!_isLost[i]) Discard(i);
            }

            Return();
        }

        /// <summary>
        /// 体をディゾルブで消し始める。全レンダラーのマテリアルをディゾルブのマテリアルに差し替え、部位を切れなくする。
        /// 消えている途中の部位を切ると、かけらにディゾルブのマテリアルが写る為。元のマテリアルは Return で戻す。
        /// </summary>
        /// <param name="material">ディゾルブのマテリアル。シェーダーは float のプロパティ _DissolveAmount（0〜1）で消える</param>
        public void BeginDissolve(Material material)
        {
            if (!IsLent || IsDissolving || material == null) return;

            IsDissolving = true;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var originals = _renderers[i].sharedMaterials;
                var replaced = new Material[originals.Length];
                Array.Fill(replaced, material);
                _originalMaterials[i] = originals;
                _renderers[i].sharedMaterials = replaced;
            }

            foreach (var part in _parts)
            {
                if (part.Cuttable != null) part.Cuttable.DisableCutting();
            }

            SetDissolveAmount(0f);
        }

        /// <summary>
        /// ディゾルブで消えた割合を変える。消えている途中でなければ何もしない。
        /// </summary>
        /// <param name="amount">消えた割合（0〜1）</param>
        public void SetDissolveAmount(float amount)
        {
            if (!IsDissolving) return;

            _propertyBlock.SetFloat(_dissolveAmountId, Mathf.Clamp01(amount));
            foreach (var bodyRenderer in _renderers)
            {
                bodyRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// 体に残っている部位の、メッシュの範囲の中心（ワールド座標）を positions に足す。
        /// </summary>
        public void CollectPartCenters(List<Vector3> positions)
        {
            foreach (var part in _parts)
            {
                var cuttable = part.Cuttable;
                if (cuttable == null || !cuttable.gameObject.activeInHierarchy || cuttable.Renderer == null) continue;

                positions.Add(cuttable.Renderer.bounds.center);
            }
        }

        /// <summary>
        /// 壊れた移動部位の数を 1 つ増やす。
        /// </summary>
        public void BreakMovePart()
        {
            _brokenMovePartCount++;
        }

        private void Awake()
        {
            if (_parts.Length > MAX_PARTS)
            {
                UsefulLogger.LogError($"敵の体の部位は {MAX_PARTS} 個までです（{_parts.Length} 個）。", this);
            }

            _isLost = new bool[_parts.Length];
            _isBroken = new bool[_parts.Length];
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalMaterials = new Material[_renderers.Length][];
            _propertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// ディゾルブで差し替えたマテリアルを元へ戻し、消えた割合を消す。
        /// </summary>
        private void EndDissolve()
        {
            for (var i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].sharedMaterials = _originalMaterials[i];
                _renderers[i].SetPropertyBlock(null);
                _originalMaterials[i] = null;
            }

            IsDissolving = false;
        }

        private int IndexOf(CuttableObject cuttable)
        {
            for (var i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].Cuttable == cuttable) return i;
            }

            return -1;
        }

        /// <summary>
        /// 部位 childIndex の Transform が、部位 parentIndex の Transform の直下にあるか。
        /// </summary>
        private bool IsChildPart(int childIndex, int parentIndex)
        {
            var child = _parts[childIndex].Cuttable;
            return child != null && child.transform.parent == _parts[parentIndex].Cuttable.transform;
        }

        /// <summary>
        /// 接続部の側のかけらの形を元の部位へ移して表示し直し、かけらはプールへ返す。当たり判定は、部位の元のコライダーを残した形に合わせる。
        /// 子の部位のうち、切断面に対して接続部と反対側にあるものは失う。
        /// </summary>
        private void KeepJointSide(int index, MultiCutResult result, Plane plane, MeshCutObjectPool fragmentPool)
        {
            var cuttable = _parts[index].Cuttable;
            var jointSide = plane.GetSide(cuttable.transform.position);
            var remaining = jointSide ? result.Front : result.Back;

            // 写した球コライダーは長い部位で隙間ができ、刃が隙間を通ると切り直せない為、元のコライダーを残した形に合わせる
            cuttable.AdoptCutShape(remaining, AdoptColliderMode.FitOwnColliders);
            cuttable.gameObject.SetActive(true);
            fragmentPool.ReleaseObject(remaining);

            for (var i = 0; i < _parts.Length; i++)
            {
                if (_isLost[i] || !IsChildPart(i, index)) continue;
                if (plane.GetSide(_parts[i].Cuttable.transform.position) != jointSide) LosePart(i);
            }
        }

        /// <summary>
        /// 部位を失ったことにして片付け、隠す。その子の部位も一緒に失う。
        /// </summary>
        private void LosePart(int index)
        {
            _isLost[index] = true;
            Discard(index);
            _parts[index].Cuttable.gameObject.SetActive(false);

            for (var i = 0; i < _parts.Length; i++)
            {
                if (!_isLost[i] && IsChildPart(i, index)) LosePart(i);
            }
        }

        /// <summary>
        /// 体から外れる部位を、切断済みならオーブに、切っていなければ見た目用の物にする。
        /// 非アクティブの部位は切断の途中で、その表と裏のかけらがチャージになるので、何も出さない。
        /// </summary>
        private void Discard(int index)
        {
            var cuttable = _parts[index].Cuttable;
            if (cuttable == null || !cuttable.gameObject.activeSelf) return;

            if (cuttable.CutCount > 0)
            {
                // 親の部位が先に隠れていても求められるよう、メッシュの範囲から中心を求める
                var center = cuttable.Mesh != null && cuttable.Mesh.sharedMesh != null
                    ? cuttable.transform.TransformPoint(cuttable.Mesh.sharedMesh.bounds.center)
                    : cuttable.transform.position;
                _spawnOrb?.Invoke(center);
            }
            else
            {
                _spawnDebris?.Invoke(cuttable, transform.position);
            }
        }

        /// <summary>
        /// 今回の切断で壊れた部位（切られた部位と、失った部位）の役割へ、壊れたことを 1 回だけ伝える。体が倒れたら、そこで止める。
        /// </summary>
        private void NotifyBrokenParts(int cutIndex)
        {
            for (var i = 0; i < _parts.Length; i++)
            {
                if (_isBroken[i] || (i != cutIndex && !_isLost[i])) continue;

                _isBroken[i] = true;
                _parts[i].Role?.OnBroken(this);

                if (!IsLent) return;
            }
        }
    }
}
