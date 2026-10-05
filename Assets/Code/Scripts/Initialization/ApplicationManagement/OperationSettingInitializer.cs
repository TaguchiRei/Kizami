using Kizami.Application;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// 操作に関わる設定（OperationSettingState）を生成する Initializer。
    /// 設定はシーンを跨いで保たれるので、常駐シーンへ置いて一度だけ生成する。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.InitializerEarly - 10)]
    public sealed class OperationSettingInitializer : InitializerBase
    {
        [SerializeField]
        [Tooltip("ダッシュ入力の受け付け方の初期値")]
        private SprintInputMode _sprintInputMode = SprintInputMode.Hold;

        [SerializeField, Range(1f, 90f)]
        [Tooltip("切断面の回転入力（ホイール 1 段）1 回あたりの回転角度（度）の初期値")]
        private float _cutRotateStepAngle = 15f;

        private OperationSettingService _settingService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetStateBoard<AppBoard>(out var appBoard))
            {
                UsefulLogger.LogError("AppBoard が未登録の為、操作設定を登録できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            _settingService = new OperationSettingService(appBoard, _sprintInputMode, _cutRotateStepAngle);

            base.Initialize(blackBoard);
        }
    }
}
