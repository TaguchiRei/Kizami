using System.Text;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージのクリアに数える破壊対象。ボクセルの建物（DestructionTarget）と、普通のオブジェクトのコア（CoreSystem）が実装する。
    /// </summary>
    public interface IDestructionTarget
    {
        /// <summary> 破壊済みか。状態を測れるようになる前は false </summary>
        bool IsDestroyed { get; }

        /// <summary>
        /// 名前と壊れ具合を、デバッグ表示の文字列へ書き足す。
        /// </summary>
        /// <param name="builder">書き足す先</param>
        void AppendStatus(StringBuilder builder);
    }
}
