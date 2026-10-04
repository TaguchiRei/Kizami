using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// プレイヤーの移動と HP に関わる、遊びのルールのパラメータ。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerParameterData", menuName = "Kizami/Player/PlayerParameterData")]
    public sealed class PlayerParameterData : ScriptableObject
    {
        [Header("歩行・ダッシュ")]
        [SerializeField, Min(0f)]
        [Tooltip("歩行の速度（m/s）")]
        private float _walkSpeed = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("ダッシュの速度（m/s）")]
        private float _sprintSpeed = 9f;

        /// <summary> 歩行の速度（m/s） </summary>
        public float WalkSpeed => _walkSpeed;

        /// <summary> ダッシュの速度（m/s） </summary>
        public float SprintSpeed => _sprintSpeed;
    }
}
