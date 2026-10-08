using System.Text;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 敵の生成と体のプール（EnemySpawnAdapter）を初期化し、出ている敵とグループの数、崩落で倒した敵の数、生成位置の有効・無効、敵の処理にかかった時間を画面に出す配線役。インゲームのシーンへ置く。
    /// 倒れた体に残っていた切断済みの部位は、FragmentOrbAdapter でオーブにする。崩落で倒した敵のエネルギーは、EnemyEnergyAdapter で出す。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。表示はエディタと Development Build でのみ行う。
    /// </summary>
    public sealed class EnemyInitializer : InitializerBase
    {
        private readonly StringBuilder _spawnPointText = new();

        [SerializeField] private EnemySpawnAdapter _spawnAdapter;
        [SerializeField] private FragmentOrbAdapter _fragmentOrbAdapter;
        [SerializeField] private EnemyEnergyAdapter _energyAdapter;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!HasRequiredReferences())
            {
                base.Initialize(blackBoard);
                return;
            }

            _spawnAdapter.Initialize(_fragmentOrbAdapter.SpawnOrb, _energyAdapter.Emit);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugGUI.ObserveVariable("Enemies",
                () => $"{_spawnAdapter.SpawnedCount} / {_spawnAdapter.Capacity} (groups {_spawnAdapter.GroupCount})");
            DebugGUI.ObserveVariable("Collapse Defeats",
                () => $"{_spawnAdapter.CollapseDefeatCount} (crush {_spawnAdapter.CrushDefeatCount})");
            DebugGUI.ObserveVariable("Spawn Points", GetSpawnPointText);
            DebugGUI.ObserveVariable("Enemy ms",
                () => $"update {_spawnAdapter.UpdateMilliseconds:F2} / move {_spawnAdapter.MoveMilliseconds:F2} / body {_spawnAdapter.BodyMilliseconds:F2} / render {_spawnAdapter.RenderMilliseconds:F2}");
            DebugGUI.ObserveVariable("Bodies", () => $"{_spawnAdapter.LentBodyCount} / {_spawnAdapter.BodyCount} (kept shapes {_spawnAdapter.KeptShapeCount})");
            DebugGUI.ObserveVariable("Distance Field", GetDistanceFieldText);
#endif

            base.Initialize(blackBoard);
        }

        /// <summary>
        /// 必須の参照がすべて設定されているかを返す。足りないものは、すべてエラーログに出す。
        /// </summary>
        private bool HasRequiredReferences()
        {
            var hasAll = this.IsAssigned(_spawnAdapter, nameof(EnemySpawnAdapter));
            hasAll &= this.IsAssigned(_fragmentOrbAdapter, nameof(FragmentOrbAdapter));
            hasAll &= this.IsAssigned(_energyAdapter, nameof(EnemyEnergyAdapter));
            return hasAll;
        }

        /// <summary>
        /// 距離マップの立てる層の数と、格子を作る時間・距離の計算 1 回の時間・形が変わった範囲を調べ直す時間（ms）を並べる。
        /// </summary>
        private string GetDistanceFieldText()
        {
            var field = _spawnAdapter.DistanceField;
            if (field == null) return "-";

            return $"nodes {field.NodeCount} (overflow {field.OverflowColumnCount}) / bake {field.BakeMilliseconds:F1} / compute {field.ComputeMilliseconds:F2} / rebake {field.RebakeMilliseconds:F2}";
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
