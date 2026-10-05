using System;
using UnityEngine;
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
        public Vector3 WallNormal { get; private set; }

        public override string GetLog()
        {
            return $"Contacts: {Contacts}  \nWallNormal: {WallNormal}";
        }

        /// <summary>
        /// 触れている物と壁の法線を設定する。
        /// </summary>
        /// <param name="contacts">触れている物</param>
        /// <param name="wallNormal">触れている壁の、壁から離れる向きの水平な単位ベクトル。壁に触れていないときは Vector3.zero</param>
        public void SetContacts(PlayerContact contacts, Vector3 wallNormal)
        {
            Contacts = contacts;
            WallNormal = wallNormal;
        }
    }

    /// <summary>
    /// プレイヤーが触れている物の読み取り面。
    /// </summary>
    public interface IPlayerContactState : IStateGetter
    {
        /// <summary> 触れている物。空中にいるときは None </summary>
        PlayerContact Contacts { get; }

        /// <summary> 触れている壁の、壁から離れる向きの水平な単位ベクトル。壁に触れていないときは Vector3.zero </summary>
        Vector3 WallNormal { get; }
    }

    /// <summary>
    /// プレイヤーが触れている物の種類。複数に同時に触れることがある。
    /// </summary>
    [Flags]
    public enum PlayerContact
    {
        /// <summary> 接触なし </summary>
        None = 0,

        /// <summary> 地面 </summary>
        Ground = 1 << 0,

        /// <summary> 壁走りのできる壁 </summary>
        Wall = 1 << 1
    }
}
