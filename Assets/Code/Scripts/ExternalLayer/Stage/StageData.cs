using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.External.Scene;

namespace Kizami.External
{
    /// <summary>
    /// ステージ1つの定義。アウトゲームの選択肢に出す名前、保存に使う ID、初回クリアで解放するスキル、インゲームで読み込むシーングループ、クリアの基礎スコア、使うスキルの固定、始めるときのチャージ量、失敗条件を持つ。
    /// 敵の出し方と破壊対象は、ステージシーンに置いた物が持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "StageData", menuName = "Kizami/Stage/StageData")]
    public sealed class StageData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("アウトゲームの選択肢に出す名前")]
        private string _displayName = "Stage";

        [SerializeField]
        [Tooltip("保存データでステージを見分ける ID。並びを変えても進行が残るよう、番号ではなく ID で保存する。空ならアセットの名前を使う")]
        private string _id;

        [SerializeField]
        [Tooltip("初めてクリアしたときに解放するスキル。空なら解放しない")]
        private SkillData _unlockSkill;

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
        [Tooltip("クリアに必要な、倒した敵の数。0 なら敵の数を条件にしない")]
        private int _requiredDefeatCount;

        [SerializeField, Min(-1)]
        [Tooltip("同時に弾を撃つアタッカーの数の上限。-1 なら敵の設定の既定の値、0 なら撃たない")]
        private int _maxAttackingAttackers = -1;

        [SerializeField, Min(-1)]
        [Tooltip("同時に攻撃するフィニッシャーの数の上限。-1 なら敵の設定の既定の値、0 なら攻撃しない")]
        private int _maxAttackingFinishers = -1;

        [SerializeField]
        [Tooltip("破壊対象の破壊でクリアにするか。オフなら、破壊済みの数は表示だけに使い、クリアは台本や到達の判定など別の仕組みが決める")]
        private bool _clearsOnTargets = true;

        [SerializeField]
        [Tooltip("すべてのスキルの破壊力を最大にする。どのスキルでも、強度が硬い素材を普通の素材と同じ範囲まで削れる")]
        private bool _isMaxDestructionPower;

        [SerializeReference, SubclassSelector]
        [Tooltip("失敗条件。どれか 1 つを満たしたら失敗にする。空ならこのステージでは失敗しない")]
        private StageFailCondition[] _failConditions = { new PlayerDeathFailCondition() };

        /// <summary> アウトゲームの選択肢に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> 保存データでステージを見分ける ID </summary>
        public string Id => string.IsNullOrEmpty(_id) ? name : _id;

        /// <summary> 初めてクリアしたときに解放するスキル。解放しなければ null </summary>
        public SkillData UnlockSkill => _unlockSkill;

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

        /// <summary> 破壊対象の破壊でクリアにするか </summary>
        public bool ClearsOnTargets => _clearsOnTargets;

        /// <summary> クリアに必要な破壊済みの対象の数。0 なら対象すべて </summary>
        public int RequiredTargetCount => _requiredTargetCount;

        /// <summary> クリアに必要な倒した敵の数。0 なら敵の数を条件にしない </summary>
        public int RequiredDefeatCount => _requiredDefeatCount;

        /// <summary> 同時に弾を撃つアタッカーの数の上限。負の値なら既定の値、0 なら撃たない </summary>
        public int MaxAttackingAttackers => _maxAttackingAttackers;

        /// <summary> 同時に攻撃するフィニッシャーの数の上限。負の値なら既定の値、0 なら攻撃しない </summary>
        public int MaxAttackingFinishers => _maxAttackingFinishers;

        /// <summary> すべてのスキルの破壊力を最大にするか </summary>
        public bool IsMaxDestructionPower => _isMaxDestructionPower;

        /// <summary> 失敗条件。どれか1つを満たしたら失敗にする </summary>
        public IReadOnlyList<StageFailCondition> FailConditions => _failConditions;
    }
}
