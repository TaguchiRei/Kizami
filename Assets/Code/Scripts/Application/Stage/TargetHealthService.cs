using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 体力制の破壊対象の体力（TargetHealthState）の唯一の書き手。受け取ったダメージを体力から引く。無敵の間は引かない。
    /// </summary>
    public sealed class TargetHealthService
    {
        private readonly TargetHealthState _state = new();

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
        /// 対象の名前と最大体力を受け取り、体力を満タンにする。
        /// </summary>
        /// <param name="name">ゲージに出す名前</param>
        /// <param name="max">最大体力</param>
        public void Setup(string name, float max)
        {
            _state.Setup(name, max);
        }

        /// <summary>
        /// 体力を減らす。無敵のとき、ダメージが0以下のときは減らさない。
        /// </summary>
        /// <param name="amount">減らす量</param>
        /// <returns>この呼び出しで体力が0になったら true</returns>
        public bool Damage(float amount)
        {
            if (_state.Mode == TargetHealthMode.Invincible || amount <= 0f || _state.Current <= 0f) return false;

            _state.SetCurrent(_state.Current - amount);
            return _state.Current <= 0f;
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
    }
}
