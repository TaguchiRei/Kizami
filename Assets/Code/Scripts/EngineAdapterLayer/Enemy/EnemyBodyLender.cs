using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.MeshCut;
using Object = UnityEngine.Object;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 切断できる体（EnemyBody）を初期化のときに作って持ち、近くの敵へ貸して使い回す。EnemySpawnAdapter が敵の種類ごとに 1 つ持ち、その種類の敵にだけ貸す。
    /// 体はプレイヤーから貸す距離の中にいる敵へ近い順に貸し、返す距離より離れたら返す。返す距離は貸す距離より遠い。貸している体の脚は EnemyLegs で歩かせる。
    /// 体を返すときは、短くなった部位の形を EnemyShapeKeeper に預け、次に貸すときに戻す。預ける空きがなければ、その敵の体は返さない。
    /// 体に空きがないときは、切断の届きうる近さ（取り上げる距離）の敵を優先し、その敵より一定以上遠い敵のうち最も遠い敵から体を取り上げる。
    /// </summary>
    public sealed class EnemyBodyLender
    {
        private readonly List<EnemyBody> _bodies = new();

        /// <summary> 部位から、その部位を持つ体の _bodies での番号を引く表 </summary>
        private readonly Dictionary<CuttableObject, int> _partOwners = new();

        private readonly EnemyShapeKeeper _shapeKeeper;
        private readonly MeshCutObjectPool _fragmentPool;

        /// <summary> 体を貸す敵の種類 </summary>
        private readonly EnemyKind _kind;

        private readonly float _lendDistance;
        private readonly float _returnDistance;
        private readonly float _reclaimDistance;
        private readonly float _reclaimMargin;

        /// <summary> 体ごとの、貸している敵の番号。貸していなければ -1。並びは _bodies と同じ </summary>
        private readonly int[] _bodyAgents;

        /// <summary> 体ごとの脚。脚を持たない体では null。並びは _bodies と同じ </summary>
        private readonly EnemyLegs[] _bodyLegs;

        /// <summary> 体を貸す候補の敵の、プレイヤーとの距離の 2 乗。並べ替えに使う作業用の配列 </summary>
        private readonly float[] _lendCandidateDistances;

        /// <summary> 体を貸す候補の敵の番号。並びは _lendCandidateDistances と同じ </summary>
        private readonly int[] _lendCandidates;

        /// <summary> 敵に貸している体の数 </summary>
        public int LentBodyCount
        {
            get
            {
                var count = 0;
                foreach (var body in _bodies)
                {
                    if (body.IsLent) count++;
                }

                return count;
            }
        }

        /// <summary> 体の数 </summary>
        public int BodyCount => _bodies.Count;

        /// <summary> 体を返した敵から預かっている、短くなった部位の数 </summary>
        public int KeptShapeCount => _shapeKeeper.KeptCount;

        /// <param name="kind">体を貸す敵の種類</param>
        /// <param name="bodyPrefab">その種類の体のプレハブ</param>
        /// <param name="parent">体と、形を預かる保管用の物を置く親</param>
        /// <param name="bodyCount">作る体の数</param>
        /// <param name="agentCapacity">敵の状態の数</param>
        /// <param name="shapeKeeperCapacity">体を返した敵の、短くなった部位の形を預かれる数</param>
        /// <param name="fragmentPool">体に残す側のかけらを返す先</param>
        /// <param name="lendDistance">プレイヤーとの距離がこの値（m）以下の敵に、体を貸す</param>
        /// <param name="returnDistance">プレイヤーとの距離がこの値（m）より離れた敵から、体を返す</param>
        /// <param name="reclaimDistance">体の空きがないとき、プレイヤーとの距離がこの値（m）以下の敵には、より遠い敵から体を取り上げて貸す</param>
        /// <param name="reclaimMargin">体を取り上げる相手は、貸す敵よりこの値（m）以上遠い敵に限る</param>
        /// <param name="spawnOrb">倒れた体に残っていた切断済みの部位を、オーブにする関数。引数はオーブを出す位置</param>
        /// <param name="spawnDebris">体から外れた切っていない部位を、見た目用の物で散らばらせる関数。引数は部位と、散らばる中心</param>
        public EnemyBodyLender(EnemyKind kind, EnemyBody bodyPrefab, Transform parent, int bodyCount,
            int agentCapacity, int shapeKeeperCapacity, MeshCutObjectPool fragmentPool, float lendDistance,
            float returnDistance, float reclaimDistance, float reclaimMargin, Action<Vector3> spawnOrb,
            Action<CuttableObject, Vector3> spawnDebris)
        {
            _kind = kind;
            _shapeKeeper = new EnemyShapeKeeper(parent, shapeKeeperCapacity, agentCapacity, bodyPrefab.Parts.Count);
            _fragmentPool = fragmentPool;
            _lendDistance = lendDistance;
            _returnDistance = returnDistance;
            _reclaimDistance = reclaimDistance;
            _reclaimMargin = reclaimMargin;
            _lendCandidateDistances = new float[agentCapacity];
            _lendCandidates = new int[agentCapacity];
            _bodyAgents = new int[bodyCount];
            _bodyLegs = new EnemyLegs[bodyCount];

            for (var i = 0; i < bodyCount; i++)
            {
                var body = Object.Instantiate(bodyPrefab, parent);
                body.gameObject.SetActive(false);
                body.Initialize(spawnOrb, spawnDebris);
                _bodies.Add(body);
                _bodyAgents[i] = -1;
                _bodyLegs[i] = body.GetComponent<EnemyLegs>();

                foreach (var part in body.Parts)
                {
                    if (part.Cuttable != null) _partOwners.Add(part.Cuttable, i);
                }
            }
        }

        /// <summary>
        /// 切断の結果のうち、敵の部位を元の対象とするものを、その部位を持つ体へ渡し、体の部位の状態を敵の状態へ書き戻す。
        /// 体が倒れたら、敵をステージから消して体を空ける。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        /// <param name="plane">振ったときの切断面。法線は表のかけらの側を向く</param>
        /// <param name="agents">敵の状態</param>
        public void ReceiveCutResults(MultiCutResult[] results, Plane plane, NativeArray<EnemyAgent> agents)
        {
            foreach (var result in results)
            {
                if (result.Original == null || !_partOwners.TryGetValue(result.Original, out var bodyIndex)) continue;

                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var body = _bodies[bodyIndex];
                body.ReceiveCut(result, plane, _fragmentPool);

                var agent = agents[agentIndex];
                body.WriteState(ref agent);
                if (!agent.IsAlive)
                {
                    agent.BodyIndex = -1;
                    _bodyAgents[bodyIndex] = -1;
                }

                agents[agentIndex] = agent;
            }
        }

        /// <summary>
        /// 返す距離より離れた敵と、落ちてステージから消えた敵から、体を返す。
        /// </summary>
        /// <param name="agents">敵の状態</param>
        /// <param name="target">プレイヤーの位置</param>
        public void ReturnBodies(NativeArray<EnemyAgent> agents, float3 target)
        {
            var returnDistanceSq = _returnDistance * _returnDistance;

            for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
            {
                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var agent = agents[agentIndex];
                if (agent.IsAlive && math.distancesq(agent.Position, target) <= returnDistanceSq) continue;

                TryReturnBody(agents, bodyIndex);
            }
        }

        /// <summary>
        /// 貸す距離の中にいる、体を貸していないこの種類の敵へ、近い順に空いている体を貸す。
        /// 空きがなければ、取り上げる距離の中の敵に限り、その敵より取り上げの差以上遠い敵のうち最も遠い敵から体を返させて貸す。
        /// </summary>
        /// <param name="agents">敵の状態</param>
        /// <param name="target">プレイヤーの位置</param>
        /// <param name="cache">体の部位を登録し直す先</param>
        /// <param name="grid">足を置く高さを読む経路の格子</param>
        public void LendBodies(NativeArray<EnemyAgent> agents, float3 target, MeshDataCache cache,
            in EnemyNavigationGrid grid)
        {
            var lendDistanceSq = _lendDistance * _lendDistance;
            var reclaimDistanceSq = _reclaimDistance * _reclaimDistance;
            var candidateCount = 0;

            for (var i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (!agent.IsAlive || agent.Kind != _kind || agent.BodyIndex >= 0) continue;

                var distanceSq = math.distancesq(agent.Position, target);
                if (distanceSq > lendDistanceSq) continue;

                _lendCandidateDistances[candidateCount] = distanceSq;
                _lendCandidates[candidateCount] = i;
                candidateCount++;
            }

            if (candidateCount == 0) return;

            Array.Sort(_lendCandidateDistances, _lendCandidates, 0, candidateCount);

            var nextBody = 0;
            for (var c = 0; c < candidateCount; c++)
            {
                while (nextBody < _bodyAgents.Length && _bodyAgents[nextBody] >= 0) nextBody++;

                var bodyIndex = nextBody;
                if (bodyIndex == _bodyAgents.Length)
                {
                    if (_lendCandidateDistances[c] > reclaimDistanceSq) return;

                    var minDistance = math.sqrt(_lendCandidateDistances[c]) + _reclaimMargin;
                    bodyIndex = FindFarthestLentBody(agents, target, minDistance * minDistance);
                    if (bodyIndex < 0 || !TryReturnBody(agents, bodyIndex)) return;
                }

                LendBody(agents, bodyIndex, _lendCandidates[c], cache, grid);
            }
        }

        /// <summary>
        /// 貸している体の位置と向きを敵の状態に合わせ、脚を動かす。
        /// </summary>
        /// <param name="agents">敵の状態</param>
        /// <param name="deltaTime">脚を動かす経過時間（秒）</param>
        /// <param name="grid">足を置く高さを読む経路の格子</param>
        public void SyncBodyTransforms(NativeArray<EnemyAgent> agents, float deltaTime, in EnemyNavigationGrid grid)
        {
            for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
            {
                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var agent = agents[agentIndex];
                _bodies[bodyIndex].transform.SetPositionAndRotation(agent.Position,
                    Quaternion.Euler(0f, math.degrees(agent.Yaw), 0f));
                if (_bodyLegs[bodyIndex] != null) _bodyLegs[bodyIndex].UpdateLegs(deltaTime, grid);
            }
        }

        /// <summary>
        /// 体の脚を返す。脚を持たない体では null。
        /// </summary>
        /// <param name="bodyIndex">体の番号（EnemyAgent.BodyIndex）</param>
        public EnemyLegs GetLegs(int bodyIndex)
        {
            return _bodyLegs[bodyIndex];
        }

        /// <summary>
        /// 敵の分として預かっている形を捨てる。敵の状態を使い直すときに呼ぶ。
        /// </summary>
        /// <param name="agentIndex">使い直す敵の状態の番号</param>
        public void DiscardKeptShapes(int agentIndex)
        {
            _shapeKeeper.Discard(agentIndex);
        }

        /// <summary>
        /// 貸している体のうち、敵がプレイヤーから距離の 2 乗 minDistanceSq より遠く、最も遠いものを返す。なければ -1。
        /// </summary>
        private int FindFarthestLentBody(NativeArray<EnemyAgent> agents, float3 target, float minDistanceSq)
        {
            var farthest = -1;
            var farthestDistanceSq = minDistanceSq;

            for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
            {
                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var distanceSq = math.distancesq(agents[agentIndex].Position, target);
                if (distanceSq <= farthestDistanceSq) continue;

                farthest = bodyIndex;
                farthestDistanceSq = distanceSq;
            }

            return farthest;
        }

        /// <summary>
        /// 体を敵から返す。生きている敵なら、部位の状態を敵の状態へ書き戻し、短くなった部位の形を預けてから返す。
        /// 預ける空きがなければ返さず false を返す。
        /// </summary>
        private bool TryReturnBody(NativeArray<EnemyAgent> agents, int bodyIndex)
        {
            var agentIndex = _bodyAgents[bodyIndex];
            var agent = agents[agentIndex];
            var body = _bodies[bodyIndex];

            if (agent.IsAlive)
            {
                if (!_shapeKeeper.CanKeep(body)) return false;

                body.WriteState(ref agent);
                _shapeKeeper.Keep(agentIndex, body);
            }

            if (_bodyLegs[bodyIndex] != null) _bodyLegs[bodyIndex].ResetPose();
            body.Return();

            agent.BodyIndex = -1;
            agents[agentIndex] = agent;
            _bodyAgents[bodyIndex] = -1;
            return true;
        }

        /// <summary>
        /// 空いている体を敵に貸し、預けていた短くなった部位の形を戻す。足を基準の位置に置く。
        /// </summary>
        private void LendBody(NativeArray<EnemyAgent> agents, int bodyIndex, int agentIndex, MeshDataCache cache,
            in EnemyNavigationGrid grid)
        {
            var agent = agents[agentIndex];
            _bodies[bodyIndex].Lend(agent, cache);
            _shapeKeeper.Restore(agentIndex, _bodies[bodyIndex]);
            if (_bodyLegs[bodyIndex] != null) _bodyLegs[bodyIndex].ResetFeet(grid);

            agent.BodyIndex = bodyIndex;
            agents[agentIndex] = agent;
            _bodyAgents[bodyIndex] = agentIndex;
        }
    }
}
