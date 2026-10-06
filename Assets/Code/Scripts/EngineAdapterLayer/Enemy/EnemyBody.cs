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

        /// <summary> 部位ごとの、失ったか。並びは _parts と同じ </summary>
        private bool[] _isLost;

        /// <summary> ステージに出ているか </summary>
        public bool IsSpawned => gameObject.activeSelf;

        /// <summary> 体を作る部位 </summary>
        public IReadOnlyList<EnemyPart> Parts => _parts;

        /// <summary>
        /// 指定した位置と向きで体を出し、全部位を切断できる状態にする。
        /// </summary>
        /// <param name="cache">部位を登録し直す先</param>
        public void Spawn(Vector3 position, Quaternion rotation, MeshDataCache cache)
        {
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);

            for (var i = 0; i < _parts.Length; i++)
            {
                _isLost[i] = false;

                var cuttable = _parts[i].Cuttable;
                if (cuttable == null) continue;

                cuttable.gameObject.SetActive(true);
                cuttable.RestoreInitialShape();
                cache.Register(cuttable);
            }
        }

        /// <summary>
        /// 部位を切られた結果を受け取り、接続部の側を体に残す。接続部の近くを切ったときは、部位ごと失う。
        /// 切断面より向こう側にある子の部位は、部位ごと失う。
        /// </summary>
        /// <param name="result">この体の部位を元の対象とする切断の結果</param>
        /// <param name="plane">振ったときの切断面。法線は表のかけらの側を向く。切断が終わるまでに体が動いた分だけずれる</param>
        /// <param name="fragmentPool">体に残す側のかけらを返す先</param>
        public void ReceiveCut(MultiCutResult result, Plane plane, MeshCutObjectPool fragmentPool)
        {
            var index = IndexOf(result.Original);
            if (index < 0) return;

            var part = _parts[index];
            var cuttable = part.Cuttable;
            var jointPosition = cuttable.transform.position;

            if (Mathf.Abs(plane.GetDistanceToPoint(jointPosition)) <= part.JointDistance)
            {
                LosePart(index);
                return;
            }

            var jointSide = plane.GetSide(jointPosition);
            var remaining = jointSide ? result.Front : result.Back;

            cuttable.AdoptCutShape(remaining);
            cuttable.gameObject.SetActive(true);
            fragmentPool.ReleaseObject(remaining);

            for (var i = 0; i < _parts.Length; i++)
            {
                if (_isLost[i] || !IsChildPart(i, index)) continue;
                if (plane.GetSide(_parts[i].Cuttable.transform.position) != jointSide) LosePart(i);
            }
        }

        /// <summary>
        /// 目標へ水平にまっすぐ近づき、進む向きを向く。止まる距離の内側では動かない。
        /// </summary>
        // TODO: 区間4B・4C の群衆 AI に置き換える
        public void MoveToward(Vector3 target, float deltaTime)
        {
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
        /// 部位を失ったことにして隠す。その子の部位も一緒に失う。
        /// </summary>
        // TODO: 失った部位を、見た目用の物で落としてディゾルブで消す
        private void LosePart(int index)
        {
            _isLost[index] = true;
            _parts[index].Cuttable.gameObject.SetActive(false);

            for (var i = 0; i < _parts.Length; i++)
            {
                if (!_isLost[i] && IsChildPart(i, index)) LosePart(i);
            }
        }
    }
}
