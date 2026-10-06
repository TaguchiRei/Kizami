using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵 1 体の体。敵のプレハブの根に付け、EnemySpawnAdapter のプールから使い回す。
    /// 出すたびに全部位を切断前の形に戻し、MeshDataCache へ登録し直す。
    /// プールで非アクティブのまま待つ部位は、MeshDataCache の初期登録に含まれない為。
    /// </summary>
    /// <remarks>
    /// 部位を切られたときは、接続部（基準点）の側を体に残し、反対側をかけらにする。
    /// 残す側はかけらの形を元の部位へ移してから、かけらをプールへ返す。かけらのまま体に付けておくと、プールの回収で消える為。
    /// 接続部の近くを切ったときと、親の部位と一緒に切り離された子の部位は、部位ごと失う。
    /// 体から外れる部位は、切断済みならオーブにし、切っていなければ見た目用の物で散らばらせる。
    /// </remarks>
    public sealed class EnemyBody : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("体を作る部位")]
        private EnemyPart[] _parts;

        [SerializeField, Min(0f)]
        [Tooltip("仮の移動で、プレイヤーへ近づく速さ（m/s）")]
        private float _moveSpeed = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("仮の移動で、プレイヤーとの水平距離がこの値（m）以下になったら止まる")]
        private float _stopDistance = 6f;

        [SerializeField, Min(1)]
        [Tooltip("壊れた移動部位がこの数に達すると、移動しなくなる")]
        private int _brokenMovePartLimit = 4;

        /// <summary> 部位ごとの、体から外れたか。並びは _parts と同じ </summary>
        private bool[] _isLost;

        /// <summary> 部位ごとの、壊れたことを役割へ伝えたか。並びは _parts と同じ </summary>
        private bool[] _isBroken;

        private int _brokenMovePartCount;

        /// <summary> 切断済みの部位をオーブにする関数。引数はオーブを出す位置 </summary>
        private Action<Vector3> _spawnOrb;

        /// <summary> 切っていない部位を見た目用の物で散らばらせる関数。引数は部位と、散らばる中心 </summary>
        private Action<CuttableObject, Vector3> _spawnDebris;

        /// <summary> ステージに出ているか </summary>
        public bool IsSpawned => gameObject.activeSelf;

        /// <summary> 壊れた移動部位の数が上限より少ないか </summary>
        public bool CanMove => _brokenMovePartCount < _brokenMovePartLimit;

        /// <summary> 体を作る部位 </summary>
        public IReadOnlyList<EnemyPart> Parts => _parts;

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
        /// 指定した位置と向きで体を出し、全部位を切断できる状態にする。
        /// </summary>
        /// <param name="cache">部位を登録し直す先</param>
        public void Spawn(Vector3 position, Quaternion rotation, MeshDataCache cache)
        {
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
            _brokenMovePartCount = 0;

            for (var i = 0; i < _parts.Length; i++)
            {
                _isLost[i] = false;
                _isBroken[i] = false;

                var cuttable = _parts[i].Cuttable;
                if (cuttable == null) continue;

                cuttable.gameObject.SetActive(true);
                cuttable.RestoreInitialShape();
                cache.Register(cuttable);
            }
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
            if (!IsSpawned) return;

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
            if (!IsSpawned) return;

            for (var i = 0; i < _parts.Length; i++)
            {
                if (!_isLost[i]) Discard(i);
            }

            foreach (var part in _parts)
            {
                if (part.Cuttable != null) part.Cuttable.RestoreInitialShape();
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 壊れた移動部位の数を 1 つ増やす。
        /// </summary>
        public void BreakMovePart()
        {
            _brokenMovePartCount++;
        }

        /// <summary>
        /// 目標へ水平にまっすぐ近づき、進む向きを向く。止まる距離より遠く、移動できる（CanMove）ときだけ動く。
        /// </summary>
        // TODO: 区間4B・4C の群衆 AI に置き換える
        public void MoveToward(Vector3 target, float deltaTime)
        {
            if (!CanMove) return;

            var offset = target - transform.position;
            offset.y = 0f;

            var distance = offset.magnitude;
            if (distance <= _stopDistance) return;

            var direction = offset / distance;
            var step = Mathf.Min(_moveSpeed * deltaTime, distance - _stopDistance);
            transform.SetPositionAndRotation(transform.position + direction * step, Quaternion.LookRotation(direction));
        }

        private void Awake()
        {
            _isLost = new bool[_parts.Length];
            _isBroken = new bool[_parts.Length];
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

                if (!IsSpawned) return;
            }
        }
    }
}
