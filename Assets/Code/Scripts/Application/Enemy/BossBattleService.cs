using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// ボス戦の段階を進めるユースケース。
    /// 水晶の段階（水晶をすべて壊すまで）→ 飛来 → 足踏み 3 回 → コアを出して攻撃 → 隙（武装を 1 つ壊すまで）を 1 周とし、決めた周数を終えたら総攻撃、総攻撃を終えたら止まる。
    /// 1 周目は水晶の段階を飛ばし、プレイヤーの正面への飛来から始める。
    /// 段階の進みは、EngineAdapterLayer（BossBattleAdapter）が起きたことを報告して進める。報告が今の段階に合わなければ捨てる。
    /// </summary>
    public sealed class BossBattleService
    {
        private readonly BossBattleState _state = new();
        private readonly int _stompsPerRound;
        private readonly int _roundCount;

        /// <param name="blackBoard">BossBattleState の登録先</param>
        /// <param name="stompsPerRound">1 周で足踏みする回数</param>
        /// <param name="roundCount">総攻撃の前に行う周の数</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public BossBattleService(IBlackBoard blackBoard, int stompsPerRound, int roundCount, int sceneId)
        {
            _stompsPerRound = stompsPerRound;
            _roundCount = roundCount;

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
            _state.SetPhase(BossBattlePhase.Arriving);
        }

        /// <summary>
        /// 水晶をすべて壊したことを受け取り、飛来の段階へ進む。
        /// </summary>
        public void ReportCrystalsCleared()
        {
            if (_state.Phase != BossBattlePhase.Crystal) return;

            _state.SetPhase(BossBattlePhase.Arriving);
        }

        /// <summary>
        /// 飛来を終えたことを受け取り、足踏みの段階へ進む。
        /// </summary>
        public void ReportArrived()
        {
            if (_state.Phase != BossBattlePhase.Arriving) return;

            _state.SetPhase(BossBattlePhase.Stomping);
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
        /// コアを出した攻撃を終えたことを受け取り、隙の段階へ進む。
        /// </summary>
        public void ReportCoreAttackFinished()
        {
            if (_state.Phase != BossBattlePhase.CoreExposed) return;

            _state.SetPhase(BossBattlePhase.Opening);
        }

        /// <summary>
        /// 武装が 1 つ壊れたことを受け取る。隙の段階のときだけ、次の周か総攻撃へ進む。
        /// </summary>
        public void ReportWeaponBroken()
        {
            if (_state.Phase != BossBattlePhase.Opening) return;

            if (_state.Round >= _roundCount)
            {
                _state.SetPhase(BossBattlePhase.AllOutAttack);
                return;
            }

            StartRound(_state.Round + 1);
        }

        /// <summary>
        /// 総攻撃を終えたことを受け取り、止まる。
        /// </summary>
        public void ReportAllOutFinished()
        {
            if (_state.Phase != BossBattlePhase.AllOutAttack) return;

            _state.SetPhase(BossBattlePhase.Stopped);
        }

        private void StartRound(int round)
        {
            _state.SetRound(round);
            _state.SetStompCount(0);
            _state.SetPhase(BossBattlePhase.Crystal);
        }
    }
}
