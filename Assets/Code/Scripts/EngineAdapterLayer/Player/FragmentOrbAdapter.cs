using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 切断で生まれたかけらを管理し、オーブに変えてプレイヤーに吸収させる。
    /// 切断の結果を受け取ると、切られた元の対象を管理から外し、表と裏のかけらを登録する。
    /// かけらがプールに回収されたとき（CuttableObject.ReuseAction）も管理から外す。
    /// かけらは、生まれてから猶予時間が過ぎた後に何かにぶつかるか、寿命が来るとオーブになる。
    /// オーブは MainCamera の位置へ向かい、届いたら吸収した数を初期化で受け取った関数へ渡す。
    /// オーブが上限の数だけあるときは、オーブを出さずにその場で吸収したものとして渡す。
    /// 非アクティブなかけらはオーブにしない。切り直されている途中のかけらは、ExecuteCut が先に非アクティブにし、
    /// 切断の結果が届くまで管理に残る為（結果が届いたときに管理から外れる）。
    /// かけらとオーブの時間は Time.time / Time.deltaTime で数える（スローモード中は一緒に遅くなる）。
    /// </summary>
    public sealed class FragmentOrbAdapter : InitializableMonoBehaviour
    {
        [SerializeField]
        [Tooltip("かけらのプール。オーブにしたかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("オーブのプレハブ")]
        private GameObject _orbPrefab;

        [SerializeField, Min(0f)]
        [Tooltip("かけらが生まれてから、ぶつかってもオーブにならない時間（秒）。0 で無効")]
        private float _contactGraceTime = 0.2f;

        [SerializeField, Min(0f)]
        [Tooltip("何にもぶつからないかけらがオーブになるまでの時間（秒）")]
        private float _fragmentLifetime = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("オーブがプレイヤーへ向かう速さ（m/s）")]
        private float _orbSpeed = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("オーブがプレイヤーにこの距離（m）まで近づいたら吸収する")]
        private float _absorbDistance = 0.5f;

        [SerializeField, Min(1)]
        [Tooltip("同時に存在するオーブの数の上限")]
        private int _maxOrbCount = 64;

        /// <summary> 管理中のかけらと、生まれた時刻（Time.time） </summary>
        private readonly Dictionary<CuttableObject, float> _fragments = new();

        /// <summary> 回収時と接触時の処理を登録済みのかけら </summary>
        private readonly HashSet<CuttableObject> _hookedFragments = new();

        /// <summary> 寿命が来たかけらを集める作業用の一覧 </summary>
        private readonly List<CuttableObject> _expiredFragments = new();

        /// <summary> 出ているオーブ </summary>
        private readonly List<Transform> _orbs = new();

        private ObjectPool<Transform> _orbPool;
        private Action<int> _onAbsorbed;

        /// <summary> 管理中のかけらの数 </summary>
        public int FragmentCount => _fragments.Count;

        /// <summary> 出ているオーブの数 </summary>
        public int OrbCount => _orbs.Count;

        /// <summary>
        /// PlayerInitializer から呼ばれる。
        /// </summary>
        /// <param name="onAbsorbed">かけらを吸収したときに呼ぶ関数。引数は吸収したかけらの数</param>
        public void Initialize(Action<int> onAbsorbed)
        {
            _onAbsorbed = onAbsorbed;

            if (_fragmentPool == null)
            {
                UsefulLogger.LogError("かけらのプールが設定されていません。", this);
            }

            if (_orbPrefab == null)
            {
                UsefulLogger.LogError("オーブのプレハブが設定されていません。", this);
            }

            _orbPool = new ObjectPool<Transform>(
                () => Instantiate(_orbPrefab, transform).transform,
                orb => orb.gameObject.SetActive(true),
                orb => orb.gameObject.SetActive(false),
                orb => Destroy(orb.gameObject),
                collectionCheck: false,
                defaultCapacity: _maxOrbCount,
                maxSize: _maxOrbCount);

            base.Initialize();
        }

        /// <summary>
        /// 切断の結果を受け取り、管理中のかけらを更新する。
        /// 元の対象を先にすべて外してから表と裏を登録する。
        /// プールが 1 回の切断の中で一周すると、元の対象が同じ切断の別の組の表や裏として使い回される為。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        public void ReceiveCutResults(MultiCutResult[] results)
        {
            if (!Initialized || results == null) return;

            foreach (var result in results)
            {
                _fragments.Remove(result.Original);
            }

            foreach (var result in results)
            {
                Track(result.Front);
                Track(result.Back);
            }
        }

        private void Update()
        {
            if (!Initialized) return;

            ConvertExpiredFragments();
            MoveOrbs();
        }

        /// <summary>
        /// かけらを管理に加える。初めて見るかけらには、回収されたときに管理から外す処理と、
        /// ぶつかったときにオーブにする処理を登録する。
        /// </summary>
        private void Track(CuttableObject fragment)
        {
            if (fragment == null || !fragment.gameObject.activeSelf) return;

            if (_hookedFragments.Add(fragment))
            {
                fragment.ReuseAction += () => _fragments.Remove(fragment);

                if (fragment.TryGetComponent(out FragmentContactReporter reporter))
                {
                    reporter.Touched += OnFragmentTouched;
                }
                else
                {
                    UsefulLogger.LogWarning("かけらに FragmentContactReporter がない為、ぶつかってもオーブになりません。", fragment);
                }
            }

            _fragments[fragment] = Time.time;
        }

        /// <summary>
        /// 管理中のかけらが、猶予時間を過ぎてからぶつかったときにオーブにする。
        /// </summary>
        private void OnFragmentTouched(CuttableObject fragment)
        {
            if (!_fragments.TryGetValue(fragment, out var spawnTime)) return;
            if (!fragment.gameObject.activeSelf) return;
            if (Time.time - spawnTime < _contactGraceTime) return;

            ConvertToOrb(fragment);
        }

        /// <summary>
        /// 寿命が来たかけらをオーブにする。
        /// </summary>
        private void ConvertExpiredFragments()
        {
            _expiredFragments.Clear();

            foreach (var (fragment, spawnTime) in _fragments)
            {
                if (!fragment.gameObject.activeSelf) continue;
                if (Time.time - spawnTime >= _fragmentLifetime) _expiredFragments.Add(fragment);
            }

            foreach (var fragment in _expiredFragments)
            {
                ConvertToOrb(fragment);
            }
        }

        /// <summary>
        /// かけらを管理から外してプールへ返し、その位置にオーブを出す。
        /// 管理から外すのは返す前に行う。返すときにも ReuseAction が呼ばれる為。
        /// オーブが上限の数だけあるときは、オーブを出さずにその場で吸収したものとして渡す。
        /// </summary>
        private void ConvertToOrb(CuttableObject fragment)
        {
            var position = fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;

            _fragments.Remove(fragment);
            if (_fragmentPool != null) _fragmentPool.ReleaseObject(fragment);

            if (_orbs.Count >= _maxOrbCount || _orbPrefab == null)
            {
                _onAbsorbed?.Invoke(1);
                return;
            }

            var orb = _orbPool.Get();
            orb.position = position;
            _orbs.Add(orb);
        }

        /// <summary>
        /// オーブを MainCamera の位置へ動かし、吸収する距離に入ったものを吸収する。
        /// </summary>
        private void MoveOrbs()
        {
            if (_orbs.Count == 0) return;

            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            var target = cameraMain.transform.position;
            var step = _orbSpeed * Time.deltaTime;
            var absorbed = 0;

            for (var i = _orbs.Count - 1; i >= 0; i--)
            {
                var orb = _orbs[i];
                orb.position = Vector3.MoveTowards(orb.position, target, step);

                if ((orb.position - target).sqrMagnitude > _absorbDistance * _absorbDistance) continue;

                _orbs[i] = _orbs[^1];
                _orbs.RemoveAt(_orbs.Count - 1);
                _orbPool.Release(orb);
                absorbed++;
            }

            if (absorbed > 0) _onAbsorbed?.Invoke(absorbed);
        }
    }
}
