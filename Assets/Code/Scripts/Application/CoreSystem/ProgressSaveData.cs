using System;

namespace Kizami.Application
{
    /// <summary>
    /// 進行の保存データ。ステージとスキルは、並びを変えても壊れないよう定義の ID で持つ。
    /// 解放済みのスキルは、初期の解放とクリア済みのステージから決まるので持たない。
    /// </summary>
    [Serializable]
    public sealed class ProgressSaveData
    {
        /// <summary> クリア済みのステージの ID </summary>
        public string[] ClearedStageIds = Array.Empty<string>();

        /// <summary> 装備枠ごとのスキルの ID。空きの枠は空文字 </summary>
        public string[] EquippedSkillIds = Array.Empty<string>();
    }
}
