using UnityEngine;
using UsefulToolkit.MeshCut;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体から外れた、切っていない部位の見た目用の物 1 つ。コライダーを持たず、初速と重力で飛びながら、ディゾルブで消える。
    /// EnemySpawnAdapter が初期化のときに作り、RecycleBuffer で使い回す。
    /// マテリアルのシェーダーは、消えた割合（0〜1）を float のプロパティ _DissolveAmount で受け取る。
    /// </summary>
    public sealed class EnemyDebris : IRecyclable
    {
        private static readonly int _dissolveAmountId = Shader.PropertyToID("_DissolveAmount");

        private readonly GameObject _gameObject;
        private readonly Transform _transform;
        private readonly MeshFilter _meshFilter;
        private readonly MeshRenderer _renderer;
        private readonly MaterialPropertyBlock _propertyBlock = new();

        /// <summary> 速度（m/s） </summary>
        private Vector3 _velocity;

        /// <summary> 角速度。向きが回転軸、大きさが回転の速さ（度/秒） </summary>
        private Vector3 _angularVelocity;

        /// <summary> 出してからの経過時間（秒） </summary>
        private float _elapsed;

        public int RecycleId { get; set; }

        /// <summary> 出ているか </summary>
        public bool IsActive => _gameObject.activeSelf;

        /// <param name="parent">親。拡大率を 1 に保つこと（部位の拡大率をそのまま写す為）</param>
        /// <param name="material">ディゾルブのマテリアル</param>
        public EnemyDebris(Transform parent, Material material)
        {
            _gameObject = new GameObject(nameof(EnemyDebris));
            _transform = _gameObject.transform;
            _transform.SetParent(parent, false);
            _meshFilter = _gameObject.AddComponent<MeshFilter>();
            _renderer = _gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _gameObject.SetActive(false);
        }

        /// <summary>
        /// 部位と同じ位置・向き・大きさ・メッシュで出し、飛ばし始める。
        /// </summary>
        /// <param name="part">写す部位。切断前のメッシュを表示していること</param>
        /// <param name="velocity">初速（m/s）</param>
        /// <param name="angularVelocity">角速度。向きが回転軸、大きさが回転の速さ（度/秒）</param>
        public void Show(CuttableObject part, Vector3 velocity, Vector3 angularVelocity)
        {
            var partTransform = part.transform;
            _transform.SetPositionAndRotation(partTransform.position, partTransform.rotation);
            _transform.localScale = partTransform.lossyScale;
            _meshFilter.sharedMesh = part.Mesh != null ? part.Mesh.sharedMesh : null;

            _velocity = velocity;
            _angularVelocity = angularVelocity;
            _elapsed = 0f;
            SetDissolveAmount(0f);

            _gameObject.SetActive(true);
        }

        /// <summary>
        /// 重力で飛ばして回し、ディゾルブを進める。
        /// </summary>
        /// <param name="lifetime">出してから消え終わるまでの時間（秒）</param>
        /// <returns>消え終わるまでの間は true</returns>
        public bool Tick(float deltaTime, float lifetime)
        {
            _elapsed += deltaTime;
            _velocity += Physics.gravity * deltaTime;

            var rotation = Quaternion.AngleAxis(_angularVelocity.magnitude * deltaTime, _angularVelocity) * _transform.rotation;
            _transform.SetPositionAndRotation(_transform.position + _velocity * deltaTime, rotation);

            SetDissolveAmount(lifetime > 0f ? _elapsed / lifetime : 1f);
            return _elapsed < lifetime;
        }

        private void SetDissolveAmount(float amount)
        {
            _propertyBlock.SetFloat(_dissolveAmountId, Mathf.Clamp01(amount));
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        public void OnRecycle()
        {
            _gameObject.SetActive(false);
        }
    }
}
