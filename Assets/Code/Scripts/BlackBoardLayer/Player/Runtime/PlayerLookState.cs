using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 視点操作の入力値を保持するステート。
    ///
    /// 持つのは「どちらへどれだけ視点を動かす指示が出ているか」であって、視点の向きそのものではない。
    /// 感度倍率と回転方向の解釈は Application 側で済ませてある為、この値は
    /// 基準感度 1.0 のときの「x = 右向きが正、y = 上向きが正」の量になる。
    ///
    /// 値の単位は入力経路によって「そのフレームの移動量」だったり「倒し量」だったりする。
    /// どちらとして扱い、実際に何度回すかは EngineAdapterLayer 側が決める。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerLookState : SceneStateBase, IPlayerLookState
    {
        public Vector2 LookInput => _lookInput;

        private Vector2 _lookInput;

        private Action<Vector2> _lookInputChangedCallback;

        /// <summary>
        /// 視点操作の入力値を設定する。
        /// </summary>
        /// <param name="lookInput">x が右向き、y が上向きを正とする回転量</param>
        public void ChangeLookInput(Vector2 lookInput)
        {
            _lookInput = lookInput;
            _lookInputChangedCallback?.Invoke(lookInput);
        }

        public IDisposable RegisterOnLookInputChanged(Action<Vector2> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _lookInputChangedCallback += callback;
            return new BoardDispose(() => _lookInputChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"LookInput: {LookInput}";
        }
    }

    /// <summary>
    /// 視点操作の入力値の読み取り面。
    /// </summary>
    public interface IPlayerLookState : IStateGetter
    {
        /// <summary> 視点操作の入力値。x が右向き、y が上向きを正とする </summary>
        Vector2 LookInput { get; }

        /// <summary>
        /// 視点操作の入力値が変化した際に発火するイベントを登録する。
        /// 入力が打ち切られた際は Vector2.zero が流れる。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に変化後の入力値が入る</param>
        IDisposable RegisterOnLookInputChanged(Action<Vector2> callback);
    }
}
