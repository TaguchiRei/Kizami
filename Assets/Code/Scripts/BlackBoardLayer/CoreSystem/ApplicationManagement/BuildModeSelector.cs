using System;
using UnityEngine;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ビルドモードごとの実体を並べて持ち、実行時のビルドモードで 1 つ引く入れ物。
    /// ビルドモードによる分岐はこの型に集め、利用側は Inspector で実体を並べるだけにする。
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
    }
}
