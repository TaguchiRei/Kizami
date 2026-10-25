using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 体力制の破壊対象の体力（TargetHealthState）の唯一の書き手。当たりの種類と大きさを、対象ごとの量に直して体力から引く。無敵の間は引かない。
    /// </summary>
    public sealed class TargetHealthService : ITargetHealthController
    {
        private readonly TargetHealthState _state = new();

        private TargetDamageSettings _damage;

        /// <summary> 落ちてくる塊の当たりで体力を引くか </summary>
        private bool _countsCollapse = true;

        /// <summary> 最大体力。対象がないステージでは0 </summary>
        public float Max => _state.Max;

        /// <summary> 対象があり、体力が尽きているか </summary>
        public bool IsDepleted => _state.Max > 0f && _state.Current <= 0f;

        /// <summary>
        /// TargetHealthState を StageBoard へ登録する。ほかの Initializer へ渡せるよう、Service は登録より前に作る。
        /// </summary>
        /// <param name="blackBoard">TargetHealthState の登録先</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public void Register(IBlackBoard blackBoard, int sceneId)
        {
            if (!blackBoard.TryGetBoard<StageBoard>(out var stageBoard, this)) return;

            stageBoard.RegisterSceneState<ITargetHealthState>(_state, sceneId);
        }

        /// <summary>
        /// 対象の名前と最大体力と、当たりの種類ごとの量を受け取り、体力を満タンにする。
        /// </summary>
        /// <param name="name">ゲージに出す名前</param>
        /// <param name="max">最大体力</param>
        /// <param name="damage">当たりの種類ごとに減る体力の量</param>
        public void Setup(string name, float max, TargetDamageSettings damage)
        {
            _damage = damage;
            _state.Setup(name, max);
        }

        /// <summary>
        /// 当たりを受け取り、種類ごとの量に直して体力を減らす。通常の表示のときだけ減らし、無敵と時間で減っていく間は減らさない。落ちてくる塊は、数えない間は減らさない。
        /// </summary>
        /// <param name="kind">当たりの種類</param>
        /// <param name="size">当たりの大きさ。落ちてくる塊は体積（m³）、量を決めたダメージは減らす量。ほかの種類では使わない</param>
        /// <returns>この呼び出しで体力が0になったら true</returns>
        public bool ReportHit(TargetHitKind kind, float size)
        {
            if (_state.Mode != TargetHealthMode.Normal || _damage == null) return false;

            var amount = kind switch
            {
                TargetHitKind.Melee => _damage.MeleeDamage,
                TargetHitKind.Skill => _damage.SkillDamage,
                TargetHitKind.Collapse => _countsCollapse ? size * _damage.CollapseDamagePerCubicMeter : 0f,
                _ => size
            };
            return Reduce(amount);
        }

        /// <summary>
        /// 時間で減っていく間に、体力を減らす。表示の状態が Draining のときだけ減らす。
        /// </summary>
        /// <param name="amount">減らす量</param>
        /// <returns>この呼び出しで体力が0になったら true</returns>
        public bool Drain(float amount)
        {
            if (_state.Mode != TargetHealthMode.Draining) return false;

            return Reduce(amount);
        }

        /// <summary>
        /// 体力を最大まで戻す。
        /// </summary>
        public void Restore()
        {
            _state.SetCurrent(_state.Max);
        }

        /// <summary>
        /// 表示の状態を設定する。
        /// </summary>
        /// <param name="mode">表示の状態</param>
        public void SetMode(TargetHealthMode mode)
        {
            _state.SetMode(mode);
        }

        /// <summary>
        /// 落ちてくる塊の当たりで体力を引くかを切り替える。ボスの飛来のように、体力に関わらない崩落が起きる間に切る。
        /// </summary>
        /// <param name="countsCollapse">落ちてくる塊の当たりで体力を引くか</param>
        public void SetCollapseCounting(bool countsCollapse)
        {
            _countsCollapse = countsCollapse;
        }

        private bool Reduce(float amount)
        {
            if (amount <= 0f || _state.Current <= 0f) return false;

            _state.SetCurrent(_state.Current - amount);
            return _state.Current <= 0f;
        }
    }

    /// <summary>
    /// 体力制の破壊対象の体力を、段階に合わせて回復・時間で減らし、表示の状態と落ちる塊を数えるかを切り替える操作面。ボス戦が使う。体力の値を読むだけなら ITargetHealthState を使う。
    /// </summary>
    public interface ITargetHealthController
    {
        /// <summary>
        /// 時間で減っていく間に、体力を減らす。表示の状態が Draining のときだけ減らす。
        /// </summary>
        /// <param name="amount">減らす量</param>
        /// <returns>この呼び出しで体力が0になったら true</returns>
        bool Drain(float amount);

        /// <summary>
        /// 体力を最大まで戻す。
        /// </summary>
        void Restore();

        /// <summary>
        /// 表示の状態を設定する。
        /// </summary>
        /// <param name="mode">表示の状態</param>
        void SetMode(TargetHealthMode mode);

        /// <summary>
        /// 落ちてくる塊の当たりで体力を引くかを切り替える。
        /// </summary>
        /// <param name="countsCollapse">落ちてくる塊の当たりで体力を引くか</param>
        void SetCollapseCounting(bool countsCollapse);
    }
}
