using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーが触れている物のうち、プレイヤーが取れる行動に関わるものを保持するステート。
    /// 物理の判定から EngineAdapterLayer（PlayerMovementAdapterBase）が書き込む。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerContactState : SceneStateBase, IPlayerContactState
    {
        public PlayerContact Contacts { get; private set; }

        private Action<PlayerContact, PlayerContact> _contactsChangedCallback;

        /// <summary>
        /// 触れている物を設定する。値が変わったときだけ変化の通知を流す。
        /// </summary>
        /// <param name="contacts">触れている物</param>
        public void SetContacts(PlayerContact contacts)
        {
            if (Contacts == contacts) return;

            var previous = Contacts;
            Contacts = contacts;
            _contactsChangedCallback?.Invoke(previous, contacts);
        }

        public IDisposable RegisterOnContactsChanged(Action<PlayerContact, PlayerContact> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _contactsChangedCallback += callback;
            return new BoardDispose(() => _contactsChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"Contacts: {Contacts}";
        }
    }

    /// <summary>
    /// プレイヤーが触れている物の読み取り面。
    /// </summary>
    public interface IPlayerContactState : IStateGetter
    {
        /// <summary> 触れている物。何にも触れていない（空中にいる）ときは None </summary>
        PlayerContact Contacts { get; }

        /// <summary>
        /// 触れている物が変化した際に発火するイベントを登録する。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に変化前と変化後の値が入る</param>
        IDisposable RegisterOnContactsChanged(Action<PlayerContact, PlayerContact> callback);
    }

    /// <summary>
    /// プレイヤーが触れている物の種類。複数に同時に触れることがある。
    /// </summary>
    [Flags]
    public enum PlayerContact
    {
        /// <summary> 何にも触れていない </summary>
        None = 0,

        /// <summary> 地面 </summary>
        Ground = 1 << 0
    }
}
