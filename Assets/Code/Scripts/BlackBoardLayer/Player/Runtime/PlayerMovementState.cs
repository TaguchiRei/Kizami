using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    public class PlayerMovementState : SceneStateBase
    {
        public Vector3 MovementDirection { get; set; }
        public float MovementSpeed { get; set; }

        public override string GetLog()
        {
            return $"MovementDirection: {MovementDirection}  \nMovementSpeed: {MovementSpeed}";
        }
    }
}