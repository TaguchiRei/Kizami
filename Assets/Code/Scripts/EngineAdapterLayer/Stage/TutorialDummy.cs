using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// チュートリアルで切るダミー1体。敵の体のプレハブを見た目と切断の対象にだけ使い、敵の仕組み（移動・攻撃・シールド・ビーム）には入れない。ステージシーンへ非アクティブで置き、台本がアクティブにする。
    /// 部位は、遠くの敵を描くときと同じ待機姿勢で置く。アクティブになると、敵の死亡時と同じディゾルブを逆に進めて現れ、現れ終わってから切れるようになる。急所の部位を切ると破壊済みになり、残りの部位がディゾルブで消える。
    /// </summary>
    /// <remarks>
    /// 部位を切ると元の部位は非アクティブになる為、子の部位が一緒に消えないよう、部位はすべてダミーの直下へ移す。
    /// </remarks>
    public sealed class TutorialDummy : MonoBehaviour, IDestructionTarget
    {
        private static readonly int _dissolveAmountId = Shader.PropertyToID("_DissolveAmount");

        [SerializeField]
        [Tooltip("見た目と切断の対象にする敵の体のプレハブ。EnemyBody の部位の設定から、切れる部位と急所を読む")]
        private EnemyBody _bodyPrefab;

        [SerializeField]
        [Tooltip("現れるときと消えるときのディゾルブのマテリアル。シェーダーは float のプロパティ _DissolveAmount（0〜1）で消える")]
        private Material _dissolveMaterial;

        [SerializeField, Min(0f)]
        [Tooltip("現れ終わるまでと、消え終わるまでの時間（秒）")]
        private float _dissolveDuration = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("目標の表示で指す位置の、ダミーの根からの高さ（m）")]
        private float _centerHeight = 1.5f;

        private readonly List<CuttableObject> _parts = new();
        private readonly List<CuttableObject> _coreParts = new();

        /// <summary> ディゾルブのマテリアルに差し替えたレンダラー </summary>
        private readonly List<Renderer> _dissolvingRenderers = new();

        /// <summary> 差し替える前のマテリアル。並びは _dissolvingRenderers と同じ </summary>
        private readonly List<Material[]> _originalMaterials = new();

        private MaterialPropertyBlock _propertyBlock;
        private bool _isDissolving;
        private bool _isAppearing;
        private float _elapsed;

        public bool IsDestroyed { get; private set; }

        public Vector3 Center => transform.position + Vector3.up * _centerHeight;

        /// <summary>
        /// 通常攻撃の切断の結果を受け取れるようにする。非アクティブのうちに呼んでよい。
        /// </summary>
        /// <param name="cutter">通常攻撃の切断を行う Adapter</param>
        public void Initialize(MeleeCutAdapter cutter)
        {
            cutter.AddOnCut(ReceiveCutResults);
        }

        public void AppendStatus(StringBuilder builder)
        {
            builder.Append(name).Append(':').Append(IsDestroyed ? " 撃破" : " -");
        }

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            if (_bodyPrefab == null) return;

            var body = Instantiate(_bodyPrefab, transform, false);
            foreach (var part in body.Parts)
            {
                if (part.Cuttable == null) continue;

                _parts.Add(part.Cuttable);
                if (part.Role is CorePartRole) _coreParts.Add(part.Cuttable);
            }

            foreach (var part in _parts)
            {
                part.transform.SetParent(transform, true);
            }

            // 部位を移し終えてから置く。親の部位を動かすと子の部位も動く為
            var bodyTransform = body.transform;
            foreach (var part in body.Parts)
            {
                if (part.Cuttable == null || !part.HasIdlePose) continue;

                part.Cuttable.transform.SetPositionAndRotation(bodyTransform.TransformPoint(part.IdlePosition),
                    bodyTransform.rotation * part.IdleRotation);
            }

            if (body.TryGetComponent<EnemyLegs>(out var legs)) Destroy(legs);
            Destroy(body);
        }

        private void OnEnable()
        {
            if (IsDestroyed) return;

            BeginDissolve(true);
        }

        private void Update()
        {
            if (!_isDissolving) return;

            _elapsed += Time.deltaTime;
            var progress = _dissolveDuration > 0f ? Mathf.Clamp01(_elapsed / _dissolveDuration) : 1f;
            SetDissolveAmount(_isAppearing ? 1f - progress : progress);
            if (progress < 1f) return;

            EndDissolve();
            if (_isAppearing)
            {
                RegisterParts();
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 急所の部位の切断の結果があれば、破壊済みにして、残りの部位を消し始める。
        /// </summary>
        private void ReceiveCutResults(MultiCutResult[] results, Plane plane)
        {
            if (IsDestroyed || results == null) return;

            foreach (var result in results)
            {
                if (!_coreParts.Contains(result.Original)) continue;

                IsDestroyed = true;
                BeginDissolve(false);
                return;
            }
        }

        /// <summary>
        /// 残っている部位のマテリアルをディゾルブのものに差し替え、切れなくしてからディゾルブを始める。
        /// </summary>
        /// <param name="isAppearing">現れるときは true で、消えた状態から戻す。消えるときは false</param>
        private void BeginDissolve(bool isAppearing)
        {
            if (_isDissolving) EndDissolve();

            _isAppearing = isAppearing;
            _elapsed = 0f;
            if (_dissolveMaterial == null)
            {
                _isDissolving = true;
                return;
            }

            foreach (var part in _parts)
            {
                if (!part.gameObject.activeSelf || part.Renderer == null) continue;

                part.DisableCutting();
                var originals = part.Renderer.sharedMaterials;
                var replaced = new Material[originals.Length];
                System.Array.Fill(replaced, _dissolveMaterial);
                _dissolvingRenderers.Add(part.Renderer);
                _originalMaterials.Add(originals);
                part.Renderer.sharedMaterials = replaced;
            }

            _isDissolving = true;
            SetDissolveAmount(isAppearing ? 1f : 0f);
        }

        /// <summary>
        /// 差し替えたマテリアルを元へ戻し、消えた割合を消す。
        /// </summary>
        private void EndDissolve()
        {
            for (var i = 0; i < _dissolvingRenderers.Count; i++)
            {
                if (_dissolvingRenderers[i] == null) continue;

                _dissolvingRenderers[i].sharedMaterials = _originalMaterials[i];
                _dissolvingRenderers[i].SetPropertyBlock(null);
            }

            _dissolvingRenderers.Clear();
            _originalMaterials.Clear();
            _isDissolving = false;
        }

        private void SetDissolveAmount(float amount)
        {
            _propertyBlock.SetFloat(_dissolveAmountId, amount);
            foreach (var dissolvingRenderer in _dissolvingRenderers)
            {
                if (dissolvingRenderer != null) dissolvingRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// 残っている部位を MeshDataCache へ登録し、切れるようにする。プレハブから作った部位は、MeshDataCache の初期登録に含まれない為。
        /// </summary>
        private void RegisterParts()
        {
            var cache = MeshDataCache.Instance;
            if (cache == null || cache.Store == null) return;

            foreach (var part in _parts)
            {
                if (part.gameObject.activeSelf) cache.Register(part);
            }
        }
    }
}
