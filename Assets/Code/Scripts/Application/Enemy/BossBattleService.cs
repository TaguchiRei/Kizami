using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// ボス戦の段階と、段階の中でボスが行う行動の順番と、ボスの体力の表示の状態を進めるユースケース。
    /// 直接戦闘（飛来 → 足踏み → コアを出して攻撃 → 足踏みへ戻る）で体力が尽きたらダウンし、武装を 1 つ壊したら次へ進む。
    /// 1 周目のダウンのあとは遠距離（水晶の段階、無敵）から 2 周目の直接戦闘へ、2 周目のダウンのあとは最終（体力が時間で減る）へ進み、最終で体力が尽きたら止まる。
    /// 行動は一度に 1 つだけ決め、EngineAdapterLayer（BossBattleAdapter）が実行して終わったら報告する。報告を受けて次の行動を決める。報告が今の行動に合わなければ捨てる。
    /// </summary>
    /// <remarks>
    /// 段階ごとの行動の順番は次のとおり。
    /// 飛来：飛来 → 着いてからの待機 → 足踏みの段階へ。
    /// 足踏み：足踏みを決めた回数 → コア露出の段階へ。
    /// コア露出：下部レーザー → 降下フィニッシャーを決めた数だけ間隔を空けて出す → ビームが終わるのを待つ → 塔投げ → 待機 → 足踏みの段階へ。
    /// 水晶：退く → 水晶を投げる → 水晶がすべて壊れるまで遠距離の攻撃 → 飛来の段階へ。
    /// 最終：足踏みを決めた回数 → 一斉攻撃 → 足踏みへ戻る、を止まるまで繰り返す。
    /// ボスの体力は TargetHealthService が持ち、段階に合わせてこの Service が回復と表示の状態を切り替える。
    /// 体力が尽きたかは Tick で毎フレーム調べる。ボスに体力の対象がなければ（最大体力が0）、ダウンせず、最終は時間だけで終える。
    /// </remarks>
    public sealed class BossBattleService
    {
        /// <summary> ダウンを 2 回したら最終へ進む </summary>
        private const int ROUND_COUNT = 2;

        private readonly BossBattleState _state = new();
        private readonly ITargetHealthController _health;
        private readonly BossBattleSettings _settings;
        private readonly int _stompsPerRound;
        private readonly float _finalDuration;

        /// <summary> 最終に入ってからの時間（秒） </summary>
        private float _finalTime;

        /// <summary> 待機の残り時間（秒） </summary>
        private float _waitRemaining;

        /// <summary> コア露出で、降下フィニッシャーをあと何回出すか </summary>
        private int _strikesRemaining;

        /// <summary> 最終の段階で、今の周に足踏みした回数 </summary>
        private int _finalStompCount;

        /// <summary> 足踏みとコア露出の段階が続いている時間（秒）。それ以外の段階では 0 </summary>
        private float _fightTime;

        private EnemyBoard _enemyBoard;
        private StageBoard _stageBoard;

        /// <summary> ボスの体力。StageInitializer がこの Service より後に登録するので、登録されるまでは null で、体力が尽きたとみなさない </summary>
        private ITargetHealthState _healthState;

        /// <summary> ボスの武装の数。BossBattleAdapter が登録するので、登録されるまでは null で、行動を飛ばさない </summary>
        private IBossWeaponState _weaponState;

        /// <summary> ボスの最大体力。体力の対象がなければ 0 </summary>
        private float HealthMax => _health != null && _healthState != null ? _healthState.Max : 0f;

        /// <summary> 体力の対象があり、体力が尽きているか </summary>
        private bool IsHealthDepleted => HealthMax > 0f && _healthState.Current <= 0f;

        /// <param name="blackBoard">BossBattleState の登録先と、BossWeaponState・TargetHealthState の取得元</param>
        /// <param name="health">ボスの体力。なければ体力を見ずに進める</param>
        /// <param name="settings">行動の回数・間隔・待機の値</param>
        /// <param name="stompsPerRound">足踏みの段階で足踏みする回数</param>
        /// <param name="finalDuration">最終で、体力が満タンから尽きるまでの時間（秒）</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public BossBattleService(IBlackBoard blackBoard, ITargetHealthController health, BossBattleSettings settings,
            int stompsPerRound, float finalDuration, int sceneId)
        {
            _health = health;
            _settings = settings;
            _stompsPerRound = stompsPerRound;
            _finalDuration = finalDuration;

            blackBoard.TryGetBoard(out _stageBoard, this);
            if (!blackBoard.TryGetBoard(out _enemyBoard, this)) return;

            _enemyBoard.RegisterSceneState<IBossBattleState>(_state, sceneId);
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
            StartArriving();
        }

        /// <summary>
        /// 毎フレーム呼ぶ。無敵と落ちる塊を数えるかを決め、行えない行動を飛ばし、待機の時間を数える。直接戦闘で体力が尽きていればダウンへ進む。最終では体力を時間で減らし、尽きたら止まる。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Tick(float deltaTime)
        {
            if (_healthState == null && _stageBoard != null) _stageBoard.TryGetSceneState(out _healthState, out _);
            UpdateDamageRules(deltaTime);
            SkipUnavailableAction();
            switch (_state.Phase)
            {
                case BossBattlePhase.Arriving:
                case BossBattlePhase.Stomping:
                case BossBattlePhase.CoreExposed:
                    if (IsHealthDepleted)
                    {
                        _state.SetPhase(BossBattlePhase.Down);
                        _state.SetAction(BossAction.AwaitHit);
                        return;
                    }

                    break;
                case BossBattlePhase.Final:
                    TickFinal(deltaTime);
                    if (_state.IsStopped) return;

                    break;
            }

            TickWait(deltaTime);
        }

        /// <summary>
        /// 今の行動を終えたことを受け取り、次の行動を決める。
        /// </summary>
        /// <param name="isPerformed">行動を実際に行えたか。使う武装がなく飛ばしたとき（降下フィニッシャー・ダウンで壊す武装）と、塔投げや武装の破壊を試みて行えなかったときは false</param>
        public void ReportActionFinished(bool isPerformed)
        {
            switch (_state.Action)
            {
                case BossAction.Retreat:
                    _state.SetAction(BossAction.ThrowCrystals);
                    break;
                case BossAction.ThrowCrystals:
                    _state.SetAction(BossAction.Barrage);
                    break;
                case BossAction.Arrive:
                    StartWait(_settings.LandingPause);
                    break;
                case BossAction.Stomp:
                    OnStomped();
                    break;
                case BossAction.Laser:
                    _strikesRemaining = _settings.MeleeStrikeCount;
                    _state.SetAction(BossAction.Strike);
                    break;
                case BossAction.Strike:
                    OnStruck(isPerformed);
                    break;
                case BossAction.AwaitStrikes:
                    _state.SetAction(BossAction.TowerThrow);
                    break;
                case BossAction.TowerThrow:
                    StartWait(isPerformed ? _settings.TowerRest : 0f);
                    break;
                case BossAction.FinalVolley:
                    _finalStompCount = 0;
                    _state.SetAction(BossAction.Stomp);
                    break;
                case BossAction.AwaitHit:
                    // 壊せる武装が残っていないので、壊したものとして進める
                    if (!isPerformed) LeaveDown();
                    break;
                case BossAction.BreakWeapon:
                    if (isPerformed) LeaveDown();
                    else _state.SetAction(BossAction.AwaitHit);
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
            StartArriving();
        }

        /// <summary>
        /// 体力が尽きたあとに、ボスへ攻撃が当たったことを受け取る。ダウンで攻撃を待っているときだけ、武装を 1 つ壊させる。ダウンの間に壊すのは 1 つだけ。
        /// </summary>
        public void ReportHitWhileDown()
        {
            if (_state.Phase != BossBattlePhase.Down || _state.Action != BossAction.AwaitHit) return;

            _state.SetAction(BossAction.BreakWeapon);
        }

        /// <summary>
        /// 足踏みとコア露出が続いている時間を数え、ボスの体力の無敵と落ちる塊を数えるかを TargetHealthService に指示する。
        /// 飛来の間と、足踏みを始めてから猶予の時間が過ぎるまでは無敵にし、飛来の崩落の落下を待つ。落ちる塊は、足踏みとコア露出の間の猶予のあとだけ数え、戦っていないときの崩落や外れた武装の落下で体力が減らないようにする。
        /// </summary>
        private void UpdateDamageRules(float deltaTime)
        {
            if (_health == null) return;

            var phase = _state.Phase;
            if (phase != BossBattlePhase.Stomping && phase != BossBattlePhase.CoreExposed)
            {
                _fightTime = 0f;
                _health.SetCollapseCounting(false);
                if (phase == BossBattlePhase.Arriving) _health.SetMode(TargetHealthMode.Invincible);
                return;
            }

            _fightTime += deltaTime;
            var isGraceOver = _fightTime >= _settings.CollapseGrace;
            _health.SetCollapseCounting(isGraceOver);
            _health.SetMode(isGraceOver ? TargetHealthMode.Normal : TargetHealthMode.Invincible);
        }

        /// <summary>
        /// 今の行動に使う武装がなければ、行動を終えたものとして次へ進める。1 フレームに飛ばすのは 1 つだけにし、続けて行えない行動が並んでも 1 フレームで回りきらないようにする。
        /// アウトリガーと下部レーザーがなければ行ったものとし（足踏みの回数も数える）、降下フィニッシャーがなければ出せなかったものとして残りを飛ばす。壊せる武装が残っていなければ、ダウンを終える。
        /// </summary>
        private void SkipUnavailableAction()
        {
            if (_weaponState == null && _enemyBoard != null) _enemyBoard.TryGetSceneState(out _weaponState, out _);
            if (_weaponState == null) return;

            switch (_state.Action)
            {
                case BossAction.Stomp when _weaponState.OutriggerCount == 0:
                case BossAction.Laser when _weaponState.LaserCount == 0:
                case BossAction.FinalVolley when _weaponState.LaserCount == 0:
                    ReportActionFinished(true);
                    break;
                case BossAction.Strike when _weaponState.MissileCount == 0:
                case BossAction.AwaitHit when !_weaponState.HasBreakable:
                    ReportActionFinished(false);
                    break;
            }
        }

        /// <summary>
        /// ダウンを終える。1 周目なら遠距離へ、2 周目なら最終へ進む。どちらも体力を全回復する。
        /// </summary>
        private void LeaveDown()
        {
            _health?.Restore();
            if (_state.Round >= ROUND_COUNT)
            {
                _finalTime = 0f;
                _finalStompCount = 0;
                _health?.SetMode(TargetHealthMode.Draining);
                _state.SetPhase(BossBattlePhase.Final);
                _state.SetAction(BossAction.Stomp);
                return;
            }

            _state.SetRound(_state.Round + 1);
            _health?.SetMode(TargetHealthMode.Invincible);
            _state.SetPhase(BossBattlePhase.Crystal);
            _state.SetAction(BossAction.Retreat);
        }

        /// <summary>
        /// 最終で、満タンから _finalDuration 秒で尽きる速さで体力を減らす。尽きるか、体力の対象がなく時間が過ぎたら止まる。
        /// </summary>
        private void TickFinal(float deltaTime)
        {
            _finalTime += deltaTime;
            var hasHealth = HealthMax > 0f;
            if (hasHealth) _health.Drain(HealthMax / _finalDuration * deltaTime);

            var isFinished = hasHealth ? IsHealthDepleted : _finalTime >= _finalDuration;
            if (!isFinished) return;

            _health?.SetMode(TargetHealthMode.Broken);
            _state.SetPhase(BossBattlePhase.Stopped);
            _state.SetAction(BossAction.None);
        }

        /// <summary>
        /// 待機の間だけ時間を数え、終わったら次の行動を決める。飛来のあとの待機なら足踏みの段階へ、コア露出の待機なら次の降下フィニッシャーか足踏みの段階へ進む。
        /// </summary>
        private void TickWait(float deltaTime)
        {
            if (_state.Action != BossAction.Wait) return;

            _waitRemaining -= deltaTime;
            if (_waitRemaining > 0f) return;

            if (_state.Phase == BossBattlePhase.CoreExposed && _strikesRemaining > 0)
            {
                _state.SetAction(BossAction.Strike);
                return;
            }

            StartStomping();
        }

        /// <summary>
        /// 足踏みを数える。足踏みの段階で決めた回数に達したらコア露出の段階へ、最終で決めた回数に達したら一斉攻撃へ進む。
        /// </summary>
        private void OnStomped()
        {
            if (_state.Phase == BossBattlePhase.Final)
            {
                _finalStompCount++;
                _state.SetAction(_finalStompCount >= _settings.FinalStomps ? BossAction.FinalVolley : BossAction.Stomp);
                return;
            }

            _state.SetStompCount(_state.StompCount + 1);
            if (_state.StompCount < _stompsPerRound)
            {
                _state.SetAction(BossAction.Stomp);
                return;
            }

            _state.SetPhase(BossBattlePhase.CoreExposed);
            _state.SetAction(BossAction.Laser);
        }

        /// <summary>
        /// 降下フィニッシャーを 1 つ出したことを数え、残りがあれば間隔を空けて次を出す。出し終えたか、出せなかったら、ビームが終わるのを待つ。
        /// </summary>
        private void OnStruck(bool isPerformed)
        {
            _strikesRemaining = isPerformed ? _strikesRemaining - 1 : 0;
            if (_strikesRemaining > 0)
            {
                StartWait(_settings.MeleeStrikeInterval);
                return;
            }

            _state.SetAction(BossAction.AwaitStrikes);
        }

        private void StartArriving()
        {
            _state.SetPhase(BossBattlePhase.Arriving);
            _state.SetAction(BossAction.Arrive);
        }

        private void StartStomping()
        {
            _state.SetStompCount(0);
            _state.SetPhase(BossBattlePhase.Stomping);
            _state.SetAction(BossAction.Stomp);
        }

        private void StartWait(float duration)
        {
            _waitRemaining = duration;
            _state.SetAction(BossAction.Wait);
        }
    }
}
