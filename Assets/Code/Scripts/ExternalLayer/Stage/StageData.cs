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

        [SerializeField]
        [Tooltip("クリアの対象にするギミックの ID。末尾を * にすると、その前までで始まる ID をすべて対象にする（Factory_* など）。空なら、読み込んだシーンの破壊対象をすべて対象にする")]
        private string[] _targetGimmickIds = System.Array.Empty<string>();

        [SerializeField, Min(0)]
        [Tooltip("クリアに必要な、破壊済みの対象の数。どの対象を壊してもよい。0 なら対象すべて")]
        private int _requiredTargetCount;

        [SerializeField, Min(0)]
        [Tooltip("同時に弾を撃つアタッカーの数の上限。0 なら敵の設定の既定の値")]
        private int _maxAttackingAttackers;

        [SerializeField, Min(0)]
        [Tooltip("同時に攻撃するフィニッシャーの数の上限。0 なら敵の設定の既定の値")]
        private int _maxAttackingFinishers;

        [SerializeField]
        [Tooltip("すべてのスキルの破壊力を最大にする。どのスキルでも、強度が硬い素材を普通の素材と同じ範囲まで削れる")]
        private bool _isMaxDestructionPower;

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

        /// <summary> クリアの対象にするギミックの ID。末尾の * は前方一致。空なら破壊対象をすべて対象にする </summary>
        public IReadOnlyList<string> TargetGimmickIds => _targetGimmickIds;

        /// <summary> クリアに必要な破壊済みの対象の数。0 なら対象すべて </summary>
        public int RequiredTargetCount => _requiredTargetCount;

        /// <summary> 同時に弾を撃つアタッカーの数の上限。0 なら既定の値 </summary>
        public int MaxAttackingAttackers => _maxAttackingAttackers;

        /// <summary> 同時に攻撃するフィニッシャーの数の上限。0 なら既定の値 </summary>
        public int MaxAttackingFinishers => _maxAttackingFinishers;

        /// <summary> すべてのスキルの破壊力を最大にするか </summary>
        public bool IsMaxDestructionPower => _isMaxDestructionPower;

        /// <summary> 失敗条件。どれか1つを満たしたら失敗にする </summary>
        public IReadOnlyList<StageFailCondition> FailConditions => _failConditions;
    }
}
