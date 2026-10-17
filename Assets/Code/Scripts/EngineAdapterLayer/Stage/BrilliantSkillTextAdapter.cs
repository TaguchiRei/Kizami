using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の言葉を、ボクセル風の物理的な文字としてプレイヤーのカメラの前に出す Adapter。
    /// 同じフレームに受け取った言葉は 1 組にまとめ、言葉ごとに 1 行として上下に積む。
    /// 文字は 1 文字ずつ別のオブジェクトで（VoxelLetterMesh）、カメラの中央を起点に左右へ並べる。中央に近い文字ほどカメラから遠くに置き、各文字はカメラへ向ける。
    /// 出るときは拡大して少し行き過ぎてから戻る（BrilliantLetter）。使うまで消えず、組の数が上限を超えたら古い組から消す。
    /// 文字は Rigidbody を持つが、つかまれるまでは動かさない（Kinematic）。
    /// 投げられた文字は、何かにぶつかったらぶつかった点を通知して消える。
    /// 文字は通常攻撃のメッシュ切断で切れる。切った文字は、切った面に垂直で文字の中心を通る面で自動でもう 1 回切り、
    /// 4 つのかけらをプレイヤーの視点方向へ散らす。散ったかけらは、止まったら足場として残る。
    /// </summary>
    /// <remarks>
    /// 文字の CuttableObject は複数回の切断（2 回まで）を許可する必要があるが、その設定は Inspector 用の非公開フィールドにしかない為、JsonUtility で書き込む。
    /// 切断は 1 つの MultiCutBlade を共有する為、2 回目の切断は MeleeCutAdapter の切断が終わるのを待って、MeleeCutAdapter 経由で行う。
    /// </remarks>
    public sealed class BrilliantSkillTextAdapter : MonoBehaviour
    {
        /// <summary> 文字の CuttableObject に書き込む、複数回の切断の設定（Json）。2 回切れる </summary>
        private const string CUT_SETTINGS_JSON = "{\"_canMultiCut\":true,\"_maxCutCount\":2}";

        private readonly List<string> _pendingWords = new();
        private readonly List<GameObject> _groups = new();
        private readonly List<BrilliantLetter> _letters = new();

        /// <summary> 切れる文字の CuttableObject と、その文字 </summary>
        private readonly Dictionary<CuttableObject, BrilliantLetter> _cuttableLetters = new();

        /// <summary> 1 回切られて、2 回目の切断を待っているかけら </summary>
        private readonly HashSet<CuttableObject> _halves = new();

        /// <summary> 散らしたかけらの、散らす前のレイヤー。かけらが回収されたら戻す </summary>
        private readonly Dictionary<CuttableObject, int> _originalLayers = new();

        [SerializeField, Min(1f)]
        [Tooltip("言葉の中央の文字を置く、カメラからの距離（m）")]
        private float _centerDistance = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("中央から 1m 離れるごとに、カメラへ近づける距離（m）")]
        private float _depthSlope = 0.4f;

        [SerializeField, Min(1f)]
        [Tooltip("カメラからの距離の下限（m）")]
        private float _minDistance = 3f;

        [SerializeField, Min(0.1f)]
        [Tooltip("隣の文字との間隔（m）")]
        private float _letterSpacing = 1.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("行と行の間隔（m）")]
        private float _lineHeight = 2.2f;

        [SerializeField]
        [Tooltip("カメラの高さから見た、言葉全体の中心の高さ（m）")]
        private float _heightOffset = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("文字の高さ（m）。ドット 7 つぶん")]
        private float _letterHeight = 1.6f;

        [SerializeField, Min(1f)]
        [Tooltip("文字の奥行きの、ドット 1 つに対する倍率")]
        private float _letterDepthCells = 2f;

        [SerializeField]
        [Tooltip("文字の色")]
        private Color _fillColor = new(1f, 0.85f, 0.1f);

        [SerializeField]
        [Tooltip("切った面の色")]
        private Color _capColor = Color.white;

        [SerializeField]
        [Tooltip("文字の輪郭線の色")]
        private Color _outlineColor = Color.black;

        [SerializeField, Min(1f)]
        [Tooltip("輪郭線にする、文字より大きい殻の倍率")]
        private float _outlineScale = 1.08f;

        [SerializeField]
        [Tooltip("文字の色のマテリアル。未設定なら URP の Unlit シェーダーで作る")]
        private Material _fillMaterial;

        [SerializeField, Min(0f)]
        [Tooltip("文字ごとの傾き（度）の幅。0 から ± この値の間でばらつかせる")]
        private float _rollRange = 8f;

        [SerializeField, Min(0.01f)]
        [Tooltip("文字が出る時間（秒）。ゲーム内の時間の倍率に関係なく進む")]
        private float _popDuration = 0.18f;

        [SerializeField, Min(1)]
        [Tooltip("同時に置いておく組の数の上限。超えたら古い組から消す")]
        private int _maxGroupCount = 3;

        [SerializeField, Min(0f)]
        [Tooltip("切った文字のかけらを散らす初速（m/s）")]
        private float _scatterSpeed = 10f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("かけらを散らす向きの、視線からのばらつき（度）")]
        private float _scatterSpread = 25f;

        [SerializeField, Min(0f)]
        [Tooltip("かけらを散らす向きに足す、上向きの割合")]
        private float _scatterLift = 0.15f;

        [SerializeField, Min(0f)]
        [Tooltip("かけらの回転の速さ（rad/s）の上限")]
        private float _scatterSpin = 6f;

        [SerializeField, Range(0, 31)]
        [Tooltip("散らしたかけらを足場にする為のレイヤー。プレイヤーと当たるレイヤーにする")]
        private int _platformLayer;

        /// <summary> 投げられた文字がぶつかったときに呼ぶ処理 </summary>
        private System.Action<Vector3> _onLetterLanded;

        /// <summary> かけらを、オーブにする管理から外す関数。外せたかけらは、寿命が来てもオーブにならずチャージにもならない </summary>
        private System.Func<CuttableObject, bool> _untrackFragment;

        private MeleeCutAdapter _cutter;
        private Material _capMaterial;
        private Material _outlineMaterial;

        /// <summary> 出ている文字。つかまれていない文字と、つかまれて運ばれている文字を含み、投げられて消えた文字は含まない </summary>
        public IReadOnlyList<BrilliantLetter> Letters => _letters;

        private static Material CreateMaterial(Shader shader, Color color)
        {
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>
        /// 投げられた文字がぶつかったときに呼ぶ処理を設定する。
        /// </summary>
        /// <param name="onLanded">ぶつかった点（ワールド空間）を受け取る処理</param>
        public void SetOnLetterLanded(System.Action<Vector3> onLanded)
        {
            _onLetterLanded = onLanded;
        }

        /// <summary>
        /// 文字を切れるようにする。通常攻撃の切断の結果を受け取り、2 回目の切断を MeleeCutAdapter に頼む。
        /// </summary>
        /// <param name="cutter">通常攻撃の切断を行う Adapter</param>
        /// <param name="untrackFragment">かけらをオーブにする管理から外す関数</param>
        public void SetCutter(MeleeCutAdapter cutter, System.Func<CuttableObject, bool> untrackFragment)
        {
            _cutter = cutter;
            _untrackFragment = untrackFragment;
            _cutter.AddOnCut(ReceiveCutResults);
        }

        /// <summary>
        /// 言葉を出す予約をする。同じフレームに予約した言葉は、1 組にまとめて出す。
        /// </summary>
        /// <param name="word">出す言葉。空白は文字として出さない</param>
        public void Show(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;

            _pendingWords.Add(word);
        }

        /// <summary>
        /// 切断の結果のうち文字のものを処理する。
        /// 1 回目の切断なら、元の文字を消して、できた 2 つのかけらを 2 回目の切断に回す。
        /// 2 回目の切断なら、できたかけらをオーブにする管理から外して、視点方向へ散らす。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        /// <param name="plane">切断面。法線は表のかけらの側を向く</param>
        private void ReceiveCutResults(MultiCutResult[] results, Plane plane)
        {
            List<(CuttableObject[] Halves, Vector3 Center)> secondCuts = null;
            List<CuttableObject> pieces = null;

            foreach (var result in results)
            {
                if (_cuttableLetters.Remove(result.Original, out var letter))
                {
                    var center = letter.Center;
                    _letters.Remove(letter);
                    Destroy(letter.gameObject);

                    Untrack(result.Front);
                    Untrack(result.Back);
                    _halves.Add(result.Front);
                    _halves.Add(result.Back);
                    secondCuts ??= new List<(CuttableObject[], Vector3)>();
                    secondCuts.Add((new[] { result.Front, result.Back }, center));
                }
                else if (_halves.Remove(result.Original))
                {
                    pieces ??= new List<CuttableObject>();
                    pieces.Add(result.Front);
                    pieces.Add(result.Back);
                }
            }

            if (secondCuts != null) CutSecondTimeAsync(secondCuts, plane.normal).Forget();
            if (pieces != null) ScatterPieces(pieces);
        }

        /// <summary>
        /// 1 回目の切断でできたかけらを、1 回目の切断面に垂直で文字の中心を通る面で順に切る。
        /// 面の法線は、1 回目の面の法線と今の視線の両方に垂直な向きにする。
        /// </summary>
        private async UniTaskVoid CutSecondTimeAsync(List<(CuttableObject[] Halves, Vector3 Center)> secondCuts,
            Vector3 firstNormal)
        {
            foreach (var (halves, center) in secondCuts)
            {
                var forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
                var normal = Vector3.Cross(firstNormal, forward);
                if (normal.sqrMagnitude < 0.0001f) normal = Vector3.Cross(firstNormal, Vector3.up);

                await _cutter.CutTargets(halves, center, normal.normalized);
            }
        }

        /// <summary>
        /// 2 回目の切断でできたかけらを、オーブにならず足場として残るようにして、視点方向へ散らす。
        /// </summary>
        private void ScatterPieces(List<CuttableObject> pieces)
        {
            var forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            foreach (var piece in pieces)
            {
                if (piece == null || !piece.gameObject.activeSelf) continue;

                Untrack(piece);
                if (!_originalLayers.ContainsKey(piece))
                {
                    _originalLayers[piece] = piece.gameObject.layer;
                    piece.ReuseAction += () => RestoreLayer(piece);
                }

                piece.gameObject.layer = _platformLayer;

                var direction = Random.insideUnitSphere * Mathf.Tan(_scatterSpread * Mathf.Deg2Rad) + forward;
                direction += Vector3.up * _scatterLift;
                var body = piece.Rig;
                body.linearVelocity = direction.normalized * _scatterSpeed;
                body.angularVelocity = Random.insideUnitSphere * _scatterSpin;
            }
        }

        private void RestoreLayer(CuttableObject piece)
        {
            if (_originalLayers.Remove(piece, out var layer)) piece.gameObject.layer = layer;
        }

        /// <summary>
        /// かけらをオーブにする管理から外す。
        /// </summary>
        private void Untrack(CuttableObject fragment)
        {
            _untrackFragment?.Invoke(fragment);
        }

        private void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                UsefulLogger.LogError("Universal Render Pipeline/Unlit シェーダーが見つからない為、文字の色を付けられません。", this);
                return;
            }

            if (_fillMaterial == null) _fillMaterial = CreateMaterial(shader, _fillColor);
            _capMaterial = CreateMaterial(shader, _capColor);
            _outlineMaterial = CreateMaterial(shader, _outlineColor);
            _outlineMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        }

        private void LateUpdate()
        {
            if (_pendingWords.Count == 0) return;

            SpawnGroup();
            _pendingWords.Clear();
        }

        /// <summary>
        /// 予約した言葉を、1 組の文字として出す。カメラがなければ出さない。
        /// </summary>
        private void SpawnGroup()
        {
            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            var cameraPosition = cameraMain.transform.position;
            var forward = Vector3.ProjectOnPlane(cameraMain.transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);

            var group = new GameObject("BrilliantSkillGroup");
            group.transform.SetParent(transform, false);

            var lineCount = _pendingWords.Count;
            for (var line = 0; line < lineCount; line++)
            {
                var word = _pendingWords[line];
                var y = cameraPosition.y + _heightOffset + ((lineCount - 1) * 0.5f - line) * _lineHeight;
                for (var i = 0; i < word.Length; i++)
                {
                    if (char.IsWhiteSpace(word[i])) continue;

                    var offset = (i - (word.Length - 1) * 0.5f) * _letterSpacing;
                    var depth = Mathf.Max(_minDistance, _centerDistance - Mathf.Abs(offset) * _depthSlope);
                    var position = cameraPosition + forward * depth + right * offset;
                    position.y = y;
                    CreateLetter(group.transform, word[i], position, cameraPosition);
                }
            }

            _groups.Add(group);
            TrimGroups();
        }

        /// <summary>
        /// 文字を 1 つ作る。メッシュの表（-Z 側）がカメラへ向くよう、カメラから見て奥へ向ける。
        /// 切れるようにメッシュを登録し、つかむ為の当たり判定と、輪郭線の殻を付ける。メッシュを作れない文字は出さない。
        /// </summary>
        private void CreateLetter(Transform parent, char character, Vector3 position, Vector3 cameraPosition)
        {
            var cellSize = _letterHeight / VoxelLetterMesh.GLYPH_HEIGHT;
            if (!VoxelLetterMesh.TryGetMesh(character, cellSize, cellSize * _letterDepthCells, out var mesh)) return;

            var awayFromCamera = Vector3.ProjectOnPlane(position - cameraPosition, Vector3.up);
            var rotation = Quaternion.LookRotation(awayFromCamera) *
                           Quaternion.Euler(0f, 0f, Random.Range(-_rollRange, _rollRange));

            var letterObject = new GameObject(character.ToString());
            letterObject.transform.SetParent(parent, false);
            letterObject.transform.SetPositionAndRotation(position, rotation);

            letterObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            letterObject.AddComponent<MeshRenderer>().sharedMaterial = _fillMaterial;

            var box = letterObject.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;

            var body = letterObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var cuttable = letterObject.AddComponent<CuttableObject>();
            cuttable.CapMaterial = _capMaterial;
            JsonUtility.FromJsonOverwrite(CUT_SETTINGS_JSON, cuttable);
            var letter = letterObject.AddComponent<BrilliantLetter>();
            letter.Touched += OnLetterTouched;
            letter.StartPop(_popDuration);
            _letters.Add(letter);

            if (MeshDataCache.Instance != null && MeshDataCache.Instance.Store != null &&
                MeshDataCache.Instance.Register(cuttable))
            {
                _cuttableLetters[cuttable] = letter;
            }

            CreateOutline(letterObject.transform, mesh);
        }

        /// <summary>
        /// 輪郭線として、裏面だけ描く少し大きい殻を子に付ける。
        /// </summary>
        private void CreateOutline(Transform letterTransform, Mesh mesh)
        {
            var outline = new GameObject("Outline");
            outline.transform.SetParent(letterTransform, false);
            outline.transform.localScale = Vector3.one * _outlineScale;
            outline.AddComponent<MeshFilter>().sharedMesh = mesh;
            outline.AddComponent<MeshRenderer>().sharedMaterial = _outlineMaterial;
        }

        /// <summary>
        /// 投げられた文字がぶつかったら、ぶつかった点を通知して、その文字を消す。
        /// </summary>
        private void OnLetterTouched(BrilliantLetter letter, Collision collision)
        {
            var point = collision.contactCount > 0 ? collision.GetContact(0).point : letter.Center;
            _letters.Remove(letter);
            Destroy(letter.gameObject);
            _onLetterLanded?.Invoke(point);
        }

        /// <summary>
        /// 組の数が上限を超えている間、古い組から消す。外から先に消された組は数えない。
        /// </summary>
        private void TrimGroups()
        {
            _groups.RemoveAll(group => group == null);
            _letters.RemoveAll(letter => letter == null);
            while (_groups.Count > _maxGroupCount)
            {
                Destroy(_groups[0]);
                _groups.RemoveAt(0);
            }
        }

        private void OnDestroy()
        {
            if (_capMaterial != null) Destroy(_capMaterial);
            if (_outlineMaterial != null) Destroy(_outlineMaterial);
        }
    }
}
