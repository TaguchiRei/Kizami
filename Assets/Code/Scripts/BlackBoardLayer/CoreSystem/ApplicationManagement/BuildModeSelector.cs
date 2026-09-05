using System;
using UnityEngine;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ビルドモードごとの実体を並べて持ち、実行時のビルドモードで 1 つ引く入れ物。
    ///
    /// ビルドモードによる分岐はこの型の中の 1 箇所だけに置き、利用側は
    /// 「何を並べたか」を Inspector で示すだけにする。呼ぶ側に switch を書かせない為の型。
    /// </summary>
    /// <typeparam name="T">ビルドモードごとに差し替える実体の型</typeparam>
    [Serializable]
    public sealed class BuildModeSelector<T>
    {
        [SerializeField] private T _pc;
        [SerializeField] private T _mobile;
        [SerializeField] private T _vr;

        /// <summary>
        /// ビルドモードに対応する実体を取り出す。
        /// </summary>
        /// <param name="buildMode">引くビルドモード</param>
        public T Select(BuildMode buildMode)
        {
            return buildMode switch
            {
                BuildMode.PC => _pc,
                BuildMode.Mobile => _mobile,
                _ => _vr
            };
        }

        /// <summary>
        /// 全ての実体を BuildMode の並び順で取り出す。
        /// 並びは <see cref="BuildModeSelector.IndexOf"/> と対応する。
        /// </summary>
        public T[] ToArray()
        {
            return new[] { _pc, _mobile, _vr };
        }
    }

    /// <summary>
    /// <see cref="BuildModeSelector{T}"/> の並びに関する規約。
    /// 複数の Selector を 1 本の配列へ連結する側が使う。
    /// </summary>
    public static class BuildModeSelector
    {
        /// <summary> 1 つの Selector が持つ実体の数 </summary>
        public const int Count = 3;

        /// <summary>
        /// Selector の並びの中での位置。
        /// BuildMode の宣言順がそのまま並び順である前提に依存している。
        /// </summary>
        /// <param name="buildMode">位置を求めるビルドモード</param>
        public static int IndexOf(BuildMode buildMode)
        {
            return (int)buildMode;
        }
    }
}
