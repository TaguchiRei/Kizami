using System.Text;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 敵の生成と体のプール（EnemySpawnAdapter）を初期化し、出ている敵の数と生成位置の有効・無効を画面に出す配線役。インゲームのシーンへ置く。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。表示はエディタと Development Build でのみ行う。
    /// </summary>
    public sealed class EnemyInitializer : InitializerBase
    {
        private readonly StringBuilder _spawnPointText = new();

        [SerializeField] private EnemySpawnAdapter _spawnAdapter;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (_spawnAdapter != null)
            {
                _spawnAdapter.Initialize();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DebugGUI.ObserveVariable("Enemies", () => $"{_spawnAdapter.SpawnedCount} / {_spawnAdapter.Capacity}");
                DebugGUI.ObserveVariable("Spawn Points", GetSpawnPointText);
#endif
            }
            else
            {
                UsefulLogger.LogError("EnemySpawnAdapter が設定されていません。", this);
            }

            base.Initialize(blackBoard);
        }

        /// <summary>
        /// 生成位置ごとの有効・無効を「名前:on」の形で並べる。
        /// </summary>
        private string GetSpawnPointText()
        {
            _spawnPointText.Clear();

            foreach (var point in _spawnAdapter.SpawnPoints)
            {
                if (point == null) continue;

                if (_spawnPointText.Length > 0) _spawnPointText.Append(' ');
                _spawnPointText.Append(point.name).Append(point.IsEnabled ? ":on" : ":off");
            }

            return _spawnPointText.ToString();
        }
    }
}
