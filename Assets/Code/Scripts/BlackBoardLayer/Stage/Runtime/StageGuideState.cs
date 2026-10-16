using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ステージの案内（大目標、小目標、会話、プレイヤーのやったことのログ）を保持するステート。
    /// ログは上限の件数まで持ち、超えたら古いものから消す。
    /// </summary>
    [RegisterBoard(typeof(StageBoard))]
    public sealed class StageGuideState : SceneStateBase, IStageGuideState
    {
        private readonly List<GuideLogEntry> _logs = new();

        /// <summary> 次に付けるログの番号 </summary>
        private int _nextLogId;

        public string Objective { get; private set; } = string.Empty;
        public string SubObjective { get; private set; } = string.Empty;
        public string Dialogue { get; private set; } = string.Empty;
        public int LogMaxCount { get; }
        public float LogLifetime { get; }
        public IReadOnlyList<GuideLogEntry> Logs => _logs;

        /// <param name="logMaxCount">ログを持つ件数の上限</param>
        /// <param name="logLifetime">ログを出しておく時間（秒）</param>
        public StageGuideState(int logMaxCount, float logLifetime)
        {
            LogMaxCount = logMaxCount;
            LogLifetime = logLifetime;
        }

        public override string GetLog()
        {
            return $"Objective: {Objective}  \nSubObjective: {SubObjective}  \nDialogue: {Dialogue}  \nLogs: {_logs.Count} / {LogMaxCount}";
        }

        /// <summary>
        /// 大目標を設定する。
        /// </summary>
        /// <param name="objective">大目標の文。空なら出さない</param>
        public void SetObjective(string objective)
        {
            Objective = objective ?? string.Empty;
        }

        /// <summary>
        /// 小目標を設定する。
        /// </summary>
        /// <param name="subObjective">小目標の文。空なら出さない</param>
        public void SetSubObjective(string subObjective)
        {
            SubObjective = subObjective ?? string.Empty;
        }

        /// <summary>
        /// 会話を設定する。
        /// </summary>
        /// <param name="dialogue">会話の文。空なら出さない</param>
        public void SetDialogue(string dialogue)
        {
            Dialogue = dialogue ?? string.Empty;
        }

        /// <summary>
        /// ログを末尾に足す。上限を超えたら、古いものから消す。
        /// </summary>
        /// <param name="text">ログの文</param>
        /// <param name="count">まとめた回数</param>
        /// <param name="time">足した時刻（実時間の秒）</param>
        public void AppendLog(string text, int count, float time)
        {
            _logs.Add(new GuideLogEntry(_nextLogId++, text, count, time));
            while (_logs.Count > LogMaxCount)
            {
                _logs.RemoveAt(0);
            }
        }

        /// <summary>
        /// 末尾のログの回数と時刻を書き換える。ログがなければ何もしない。
        /// </summary>
        /// <param name="count">まとめた回数</param>
        /// <param name="time">書き換えた時刻（実時間の秒）</param>
        public void UpdateLastLog(int count, float time)
        {
            if (_logs.Count == 0) return;

            var last = _logs[^1];
            _logs[^1] = new GuideLogEntry(last.Id, last.Text, count, time);
        }
    }

    /// <summary>
    /// ステージの案内の読み取り面。
    /// </summary>
    public interface IStageGuideState : IStateGetter
    {
        /// <summary> 大目標の文。空なら出さない </summary>
        string Objective { get; }

        /// <summary> 小目標の文。空なら出さない </summary>
        string SubObjective { get; }

        /// <summary> 会話の文。空なら出さない </summary>
        string Dialogue { get; }

        /// <summary> ログを持つ件数の上限 </summary>
        int LogMaxCount { get; }

        /// <summary> ログを出しておく時間（秒）。最後に書き換えた時刻から数える </summary>
        float LogLifetime { get; }

        /// <summary> ログ。古い順に並ぶ </summary>
        IReadOnlyList<GuideLogEntry> Logs { get; }
    }

    /// <summary>
    /// ログ1行。連続した同じ出来事は、1行にまとめて回数を増やす。
    /// </summary>
    public readonly struct GuideLogEntry
    {
        /// <summary> 行の番号。足した順に増え、まとめても変わらない </summary>
        public int Id { get; }

        /// <summary> 回数を付けない文 </summary>
        public string Text { get; }

        /// <summary> まとめた回数 </summary>
        public int Count { get; }

        /// <summary> 最後に書き換えた時刻（実時間の秒） </summary>
        public float Time { get; }

        public GuideLogEntry(int id, string text, int count, float time)
        {
            Id = id;
            Text = text;
            Count = count;
            Time = time;
        }
    }
}
