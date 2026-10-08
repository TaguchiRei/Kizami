using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体を返した敵の、切られて短くなった部位の形を預かり、次に体を貸すときに体へ戻す。
    /// 形は、初期化のときに作った保管用の CuttableObject（非アクティブのまま使う）に、CuttableObject.AdoptCutShape で移して持つ。
    /// </summary>
    /// <remarks>
    /// 形を移すと、メッシュの持ち主、マテリアル（切断面を含む）、切断回数、切れるかどうかが一緒に移る。
    /// 保管用の物は移した先として MeshDataCache の利用者に登録されるので、ストアを作り直しても形のデータは残る。
    /// 実行中は保管用の物を作らないので、空きが足りないときは預からない（呼び出し側が体を返さない）。
    /// </remarks>
    public sealed class EnemyShapeKeeper
    {
        private readonly List<CuttableObject> _keepers = new();
        private readonly Stack<int> _freeKeepers = new();

        /// <summary> 敵と部位ごとの、形を預けている保管用の物の _keepers での番号。預けていなければ -1。番号は 敵の番号 × 部位の数 ＋ 部位の番号 </summary>
        private readonly int[] _keptShapes;

        private readonly int _partCount;

        /// <summary> 預かっている部位の数 </summary>
        public int KeptCount => _keepers.Count - _freeKeepers.Count;

        /// <param name="parent">保管用の物を置く親</param>
        /// <param name="capacity">保管用の物の数（同時に預かれる部位の数）</param>
        /// <param name="agentCapacity">敵の状態の数</param>
        /// <param name="partCount">体の部位の数</param>
        public EnemyShapeKeeper(Transform parent, int capacity, int agentCapacity, int partCount)
        {
            _partCount = partCount;
            _keptShapes = new int[agentCapacity * partCount];
            for (var i = 0; i < _keptShapes.Length; i++) _keptShapes[i] = -1;

            for (var i = 0; i < capacity; i++)
            {
                var keeper = new GameObject("EnemyShapeKeeper");
                keeper.SetActive(false);
                keeper.transform.SetParent(parent, false);
                keeper.AddComponent<MeshFilter>();
                keeper.AddComponent<MeshRenderer>();

                _keepers.Add(keeper.AddComponent<CuttableObject>());
                _freeKeepers.Push(i);
            }
        }

        private static bool IsShortened(CuttableObject cuttable)
        {
            return cuttable != null && cuttable.gameObject.activeSelf && cuttable.CutCount > 0;
        }

        /// <summary>
        /// 体の短くなった部位（切断済みで体に残っている部位）を、すべて預かれるだけの空きがあるか。
        /// </summary>
        public bool CanKeep(EnemyBody body)
        {
            return CountShortenedParts(body) <= _freeKeepers.Count;
        }

        /// <summary>
        /// 体の短くなった部位の形を、敵の分として預かる。体は形を移したあと、切れない状態になる。先に CanKeep で空きを確かめる。
        /// </summary>
        public void Keep(int agentIndex, EnemyBody body)
        {
            for (var part = 0; part < _partCount; part++)
            {
                var cuttable = body.Parts[part].Cuttable;
                if (!IsShortened(cuttable)) continue;

                var keeperIndex = _freeKeepers.Pop();
                _keepers[keeperIndex].AdoptCutShape(cuttable);
                _keptShapes[agentIndex * _partCount + part] = keeperIndex;
            }
        }

        /// <summary>
        /// 敵の分として預かっている形を体の部位へ戻し、保管用の物を空ける。体を貸した直後に呼ぶ。
        /// </summary>
        public void Restore(int agentIndex, EnemyBody body)
        {
            for (var part = 0; part < _partCount; part++)
            {
                var slot = agentIndex * _partCount + part;
                var keeperIndex = _keptShapes[slot];
                if (keeperIndex < 0) continue;

                // 写した球コライダーは長い部位で隙間ができ、刃が隙間を通ると切り直せない為、元のコライダーを残した形に合わせる
                body.Parts[part].Cuttable.AdoptCutShape(_keepers[keeperIndex], AdoptColliderMode.FitOwnColliders);
                ReleaseKeeper(slot);
            }
        }

        /// <summary>
        /// 敵の分として預かっている形を捨てる。敵の状態を使い直すときに呼ぶ。
        /// </summary>
        public void Discard(int agentIndex)
        {
            for (var part = 0; part < _partCount; part++)
            {
                var slot = agentIndex * _partCount + part;
                if (_keptShapes[slot] >= 0) ReleaseKeeper(slot);
            }
        }

        private int CountShortenedParts(EnemyBody body)
        {
            var count = 0;
            foreach (var part in body.Parts)
            {
                if (IsShortened(part.Cuttable)) count++;
            }

            return count;
        }

        /// <summary>
        /// 保管用の物を切断前の形（メッシュなし）に戻し、持っていた切断後のメッシュを捨てて空ける。
        /// </summary>
        private void ReleaseKeeper(int slot)
        {
            var keeperIndex = _keptShapes[slot];
            _keepers[keeperIndex].RestoreInitialShape();
            _freeKeepers.Push(keeperIndex);
            _keptShapes[slot] = -1;
        }
    }
}
