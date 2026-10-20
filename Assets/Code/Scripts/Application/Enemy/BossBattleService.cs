using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// ボス戦の段階と、ボスの体力の表示の状態を進めるユースケース。
    /// 直接戦闘（飛来 → 足踏み → コアを出して攻撃 → 足踏みへ戻る）で体力が尽きたらダウンし、武装を 1 つ壊したら次へ進む。
    /// 1 周目のダウンのあとは遠距離（水晶の段階、無敵）から 2 周目の直接戦闘へ、2 周目のダウンのあとは最終（体力が時間で減る）へ進み、最終で体力が尽きたら止まる。
    /// 段階の進みは、EngineAdapterLayer（BossBattleAdapter）が起きたことを報告して進める。報告が今の段階に合わなければ捨てる。
    /// </summary>
    /// <remarks>
    /// ボスの体力は TargetHealthService が持ち、段階に合わせてこの Service が回復と表示の状態を切り替える。
    /// 体力が尽きたかは Tick で毎フレーム調べる。ボスに体力の対象がなければ（最大体力が0）、ダウンせず、最終は時間だけで終える。
    /// </remarks>
    public sealed class BossBattleService
    {
        /// <summary> ダウンを 2 回したら最終へ進む </summary>
        private const int ROUND_COUNT = 2;

        private readonly BossBattleState _state = new();
        private readonly TargetHealthService _health;
        private readonly int _stompsPerRound;
        private readonly float _finalDuration;

        /// <summary> 最終に入ってからの時間（秒） </summary>
        private float _finalTime;

        /// <param name="blackBoard">BossBattleState の登録先</param>
        /// <param name="health">ボスの体力。なければ体力を見ずに進める</param>
        /// <param name="stompsPerRound">足踏みの段階で足踏みする回数</param>
        /// <param name="finalDuration">最終で、体力が満タンから尽きるまでの時間（秒）</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public BossBattleService(IBlackBoard blackBoard, TargetHealthService health, int stompsPerRound,
            float finalDuration, int sceneId)
        {
            _health = health;
            _stompsPerRound = stompsPerRound;
            _finalDuration = finalDuration;

            if (!blackBoard.TryGetBoard<EnemyBoard>(out var enemyBoard, this)) return;

            enemyBoard.RegisterSceneState<IBossBattleState>(_state, sceneId);
        }

        /// <summary>
        /// ボス戦を始める。始まる前のときだけ、1 周目の飛来から始める。
        /// </summary>
        public void Begin()
        {
            if (_state.Phase != BossBattlePhase.Idle) return;

            _state.SetRound(1);
            _state.SetStompCount(0);
            _health?.SetMode(TargetHealthMode.Normal);
            _state.SetPhase(BossBattlePhase.Arriving);
        }

        /// <summary>
        /// 毎フレーム呼ぶ。直接戦闘で体力が尽きていればダウンへ進む。最終では体力を時間で減らし、尽きたら止まる。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Tick(float deltaTime)
        {
            switch (_state.Phase)
            {
                case BossBattlePhase.Arriving:
                case BossBattlePhase.Stomping:
                case BossBattlePhase.CoreExposed:
                    if (_health != null && _health.IsDepleted) _state.SetPhase(BossBattlePhase.Down);
                    break;
                case BossBattlePhase.Final:
                    TickFinal(deltaTime);
                    break;
            }
        }

        /// <summary>
        /// 水晶をすべて壊したことを受け取り、体力を全回復して通常の表示に戻し、飛来の段階へ進む。
        /// </summary>
        public void ReportCrystalsCleared()
        {
            if (_state.Phase != BossBattlePhase.Crystal) return;

            _health?.Restore();
            _health?.SetMode(TargetHealthMode.Normal);
            _state.SetPhase(BossBattlePhase.Arriving);
        }

        /// <summary>
        /// 飛来を終えたことを受け取り、足踏みの段階へ進む。
        /// </summary>
        public void ReportArrived()
        {
            if (_state.Phase != BossBattlePhase.Arriving) return;

            StartStomping();
        }

        /// <summary>
        /// 足踏みしたことを受け取る。足踏みの段階で決めた回数に達したら、コアを出す段階へ進む。
        /// </summary>
        public void ReportStomp()
        {
            if (_state.Phase != BossBattlePhase.Stomping) return;

            _state.SetStompCount(_state.StompCount + 1);
            if (_state.StompCount >= _stompsPerRound) _state.SetPhase(BossBattlePhase.CoreExposed);
        }

        /// <summary>
        /// コアを出した攻撃を終えたことを受け取り、足踏みの段階へ戻る。
        /// </summary>
        public void ReportCoreAttackFinished()
        {
            if (_state.Phase != BossBattlePhase.CoreExposed) return;

            StartStomping();
        }

        /// <summary>
        /// 武装が 1 つ壊れたことを受け取る。ダウンのときだけ、1 周目なら遠距離へ、2 周目なら最終へ進む。どちらも体力を全回復する。
        /// </summary>
        public void ReportWeaponBroken()
        {
            if (_state.Phase != BossBattlePhase.Down) return;

            _health?.Restore();
            if (_state.Round >= ROUND_COUNT)
            {
                _finalTime = 0f;
                _health?.SetMode(TargetHealthMode.Draining);
                _state.SetPhase(BossBattlePhase.Final);
                return;
            }

            _state.SetRound(_state.Round + 1);
            _health?.SetMode(TargetHealthMode.Invincible);
            _state.SetPhase(BossBattlePhase.Crystal);
        }

        /// <summary>
        /// 最終で、満タンから _finalDuration 秒で尽きる速さで体力を減らす。尽きるか、体力の対象がなく時間が過ぎたら止まる。
        /// </summary>
        private void TickFinal(float deltaTime)
        {
            _finalTime += deltaTime;
            var hasHealth = _health != null && _health.Max > 0f;
            if (hasHealth) _health.Drain(_health.Max / _finalDuration * deltaTime);

            var isFinished = hasHealth ? _health.IsDepleted : _finalTime >= _finalDuration;
            if (isFinished) _state.SetPhase(BossBattlePhase.Stopped);
        }

        private void StartStomping()
        {
            _state.SetStompCount(0);
            _state.SetPhase(BossBattlePhase.Stomping);
        }
    }
}
