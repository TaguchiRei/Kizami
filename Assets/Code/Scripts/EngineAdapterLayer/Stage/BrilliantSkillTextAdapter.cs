using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の言葉を、物理的な文字としてプレイヤーのカメラの前に出す Adapter。
    /// 同じフレームに受け取った言葉は 1 組にまとめ、言葉ごとに 1 行として上下に積む。
    /// 文字は 1 文字ずつ別のオブジェクトで、カメラの中央を起点に左右へ並べる。中央に近い文字ほどカメラから遠くに置き、各文字はカメラへ向ける。
    /// 出るときは拡大して少し行き過ぎてから戻る（BrilliantLetter）。使うまで消えず、組の数が上限を超えたら古い組から消す。
    /// 文字は Rigidbody を持つが、つかまれるまでは動かさない（Kinematic）。
    /// 投げられた文字は、何かにぶつかったら消える。
    /// </summary>
    public sealed class BrilliantSkillTextAdapter : MonoBehaviour
    {
        private readonly List<string> _pendingWords = new();
        private readonly List<GameObject> _groups = new();
        private readonly List<BrilliantLetter> _letters = new();

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
        private float _letterSpacing = 1.2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("行と行の間隔（m）")]
        private float _lineHeight = 2f;

        [SerializeField]
        [Tooltip("カメラの高さから見た、言葉全体の中心の高さ（m）")]
        private float _heightOffset = 1f;

        [SerializeField, Min(1f)]
        [Tooltip("文字の大きさ（TextMeshPro のフォントサイズ）")]
        private float _fontSize = 16f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("文字の輪郭線の太さ")]
        private float _outlineWidth = 0.35f;

        [SerializeField]
        [Tooltip("文字の色")]
        private Color _fillColor = new(1f, 0.85f, 0.1f);

        [SerializeField]
        [Tooltip("文字の輪郭線の色")]
        private Color _outlineColor = Color.black;

        [SerializeField, Min(0f)]
        [Tooltip("文字ごとの傾き（度）の幅。0 から ± この値の間でばらつかせる")]
        private float _rollRange = 8f;

        [SerializeField, Min(0.01f)]
        [Tooltip("文字が出る時間（秒）。ゲーム内の時間の倍率に関係なく進む")]
        private float _popDuration = 0.18f;

        [SerializeField, Min(0.01f)]
        [Tooltip("文字の厚み（m）。つかむ為の当たり判定の奥行き")]
        private float _letterThickness = 0.4f;

        [SerializeField, Min(1)]
        [Tooltip("同時に置いておく組の数の上限。超えたら古い組から消す")]
        private int _maxGroupCount = 3;

        /// <summary> 出ている文字。つかまれていない文字と、つかまれて運ばれている文字を含み、投げられて消えた文字は含まない </summary>
        public IReadOnlyList<BrilliantLetter> Letters => _letters;

        /// <summary>
        /// 言葉を出す予約をする。同じフレームに予約した言葉は、1 組にまとめて出す。
        /// </summary>
        /// <param name="word">出す言葉。空白は文字として出さない</param>
        public void Show(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;

            _pendingWords.Add(word);
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
        /// 文字を 1 つ作る。TextMeshPro の文字は +Z の向きに読めるので、カメラから見て奥へ向ける。
        /// </summary>
        private void CreateLetter(Transform parent, char character, Vector3 position, Vector3 cameraPosition)
        {
            var awayFromCamera = Vector3.ProjectOnPlane(position - cameraPosition, Vector3.up);
            var rotation = Quaternion.LookRotation(awayFromCamera) *
                           Quaternion.Euler(0f, 0f, Random.Range(-_rollRange, _rollRange));

            var letterObject = new GameObject(character.ToString());
            letterObject.transform.SetParent(parent, false);
            letterObject.transform.SetPositionAndRotation(position, rotation);

            var text = letterObject.AddComponent<TextMeshPro>();
            text.text = character.ToString();
            text.fontSize = _fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = _fillColor;
            text.outlineWidth = _outlineWidth;
            text.outlineColor = _outlineColor;
            text.ForceMeshUpdate();

            var bounds = text.textBounds;
            var box = letterObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = new Vector3(bounds.size.x, bounds.size.y, _letterThickness);

            var body = letterObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var letter = letterObject.AddComponent<BrilliantLetter>();
            letter.Touched += OnLetterTouched;
            letter.StartPop(_popDuration);
            _letters.Add(letter);
        }

        /// <summary>
        /// 投げられた文字がぶつかったら、その文字を消す。
        /// </summary>
        private void OnLetterTouched(BrilliantLetter letter, Collision collision)
        {
            _letters.Remove(letter);
            Destroy(letter.gameObject);
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
    }
}
