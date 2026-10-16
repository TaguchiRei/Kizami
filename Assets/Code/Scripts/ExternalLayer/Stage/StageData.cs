using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.External.Scene;

namespace Kizami.External
{
    /// <summary>
    /// ステージ1つの定義。アウトゲームの選択肢に出す名前、インゲームで読み込むシーングループ、クリアの基礎スコア、使うスキルの固定、始めるときのチャージ量、失敗条件を持つ。
    /// 敵の出し方と破壊対象は、ステージシーンに置いた物が持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "StageData", menuName = "Kizami/Stage/StageData")]
    public sealed class StageData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("アウトゲームの選択肢に出す名前")]
        private string _displayName = "Stage";

        [SerializeField]
        [Tooltip("インゲームで読み込むシーングループ。ステージシーンをアクティブにし、InGame を加える")]
        private SceneGroupDataBase _sceneGroup;

        [SerializeField, Min(0)]
        [Tooltip("クリアしたときにスコアへ足す点数")]
        private int _clearScore = 1000;

        [SerializeField, Min(0)]
        [Tooltip("ステージを始めるときのチャージ量。上限を超えた分は切り捨てる")]
        private int _initialCharge;

        [SerializeField]
        [Tooltip("このステージで使うスキルを固定する。空でなければ、アウトゲームでの装備を無視して、先頭から順に装備枠へ入れる。空なら装備したスキルを使う")]
        private SkillData[] _fixedSkills = System.Array.Empty<SkillData>();

        [SerializeReference, SubclassSelector]
        [Tooltip("失敗条件。どれか 1 つを満たしたら失敗にする。空ならこのステージでは失敗しない")]
        private StageFailCondition[] _failConditions = { new PlayerDeathFailCondition() };

        /// <summary> アウトゲームの選択肢に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> インゲームで読み込むシーングループ </summary>
        public SceneGroupDataBase SceneGroup => _sceneGroup;

        /// <summary> クリアしたときにスコアへ足す点数 </summary>
        public int ClearScore => _clearScore;

        /// <summary> ステージを始めるときのチャージ量 </summary>
        public int InitialCharge => _initialCharge;

        /// <summary> このステージで使うスキルの固定。空なら固定しない </summary>
        public IReadOnlyList<SkillData> FixedSkills => _fixedSkills;

        /// <summary> 失敗条件。どれか1つを満たしたら失敗にする </summary>
        public IReadOnlyList<StageFailCondition> FailConditions => _failConditions;
    }
}
