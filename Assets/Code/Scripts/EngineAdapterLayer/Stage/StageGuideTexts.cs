using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージ1つ分の案内の文。大目標と、段階ごとの小目標・会話を持つ。
    /// 段階の番号は、StageGuideScript の段階の番号と同じ並びで対応する。
    /// </summary>
    [CreateAssetMenu(fileName = "StageGuideTexts", menuName = "Kizami/Stage/StageGuideTexts")]
    public sealed class StageGuideTexts : ScriptableObject
    {
        [SerializeField]
        [Tooltip("ステージ全体の目標。プレイを始めたときに出す")]
        private string _objective;

        [SerializeField]
        [Tooltip("段階ごとの文。上から順に、StageGuideScript の段階と対応する")]
        private StepText[] _steps = Array.Empty<StepText>();

        /// <summary> ステージ全体の目標 </summary>
        public string Objective => _objective;

        /// <summary> 文を持つ段階の数 </summary>
        public int StepCount => _steps.Length;

        /// <summary>
        /// 段階の小目標と会話を返す。
        /// </summary>
        /// <param name="index">段階の番号</param>
        /// <param name="subObjective">その段階の小目標</param>
        /// <param name="dialogue">その段階の会話</param>
        /// <returns>段階があれば true</returns>
        public bool TryGetStep(int index, out string subObjective, out string dialogue)
        {
            if (index < 0 || index >= _steps.Length)
            {
                subObjective = string.Empty;
                dialogue = string.Empty;
                return false;
            }

            subObjective = _steps[index].SubObjective;
            dialogue = _steps[index].Dialogue;
            return true;
        }

        /// <summary>
        /// 段階1つ分の文。
        /// </summary>
        [Serializable]
        private struct StepText
        {
            [SerializeField]
            [Tooltip("この段階の小目標。空なら小目標を消す")]
            private string _subObjective;

            [SerializeField, TextArea]
            [Tooltip("この段階に入ったときに出す会話。空なら会話を消す")]
            private string _dialogue;

            /// <summary> この段階の小目標 </summary>
            public string SubObjective => _subObjective;

            /// <summary> この段階の会話 </summary>
            public string Dialogue => _dialogue;
        }
    }
}
