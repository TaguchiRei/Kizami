using UnityEngine;

namespace UsefulTools.Editor
{
    /// <summary>
    /// UsefulToolsSettingの各ページのベースクラス
    /// </summary>
    public abstract class SettingPageBase
    {
        /// <summary>
        /// タブに表示される名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// GUIの描画処理
        /// </summary>
        public abstract void OnGUI();

        /// <summary>
        /// 初期化処理（必要に応じてオーバーライド）
        /// </summary>
        public virtual void Initialize() { }
    }

    /// <summary>
    /// 自動生成のタイミング
    /// </summary>
    public enum GenerateTiming
    {
        None = 0,
        OnAssetChanged = 1,     // アセット変更時（SceneList変更やInputActions更新時）
        OnToolUpdate = 2,       // 関連ツール（SceneLoaderなど）の更新時にも連動
    }
}
