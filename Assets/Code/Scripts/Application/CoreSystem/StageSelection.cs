using Kizami.External;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// インゲームで使うステージと装備を、アウトゲームで選んだ進行から決める。
    /// アウトゲームを通さずにインゲームのシーンから直接再生したときは、ステージを選んでいない為、既定の値に替える。
    /// </summary>
    public static class StageSelection
    {
        /// <summary>
        /// アウトゲームで選んだステージの定義を取得する。
        /// </summary>
        /// <param name="progress">進行の Service。受け取れなかったときは null</param>
        /// <param name="stage">選んだステージの定義。選んでいなければ null</param>
        /// <returns>ステージを選んでいるか</returns>
        public static bool TryGetSelectedStage(IGameProgressController progress, out StageData stage)
        {
            stage = null;
            return progress != null && progress.TryGetSelectedStage(out stage);
        }

        /// <summary>
        /// アウトゲームで選んだステージの定義を返す。選んでいなければ、既定の値のステージを作って返す。
        /// </summary>
        /// <param name="progress">進行の Service。受け取れなかったときは null</param>
        /// <param name="context">警告のログに添えるオブジェクト</param>
        public static StageData GetStageOrDefault(IGameProgressController progress, object context)
        {
            if (TryGetSelectedStage(progress, out var stage)) return stage;

            UsefulLogger.LogWarning("ステージが選ばれていない為、既定の値のステージで始めます。", context);
            return ScriptableObject.CreateInstance<StageData>();
        }

        /// <summary>
        /// アウトゲームで装備したスキルを返す。ステージを選んでいなければ、fallback を返す。
        /// </summary>
        /// <param name="progress">進行の Service。受け取れなかったときは null</param>
        /// <param name="fallback">アウトゲームを通さずに始めたときの装備</param>
        public static SkillData[] GetEquippedSkillsOrDefault(IGameProgressController progress, SkillData[] fallback)
        {
            return TryGetSelectedStage(progress, out _) ? progress.GetEquippedSkills() : fallback;
        }
    }
}
