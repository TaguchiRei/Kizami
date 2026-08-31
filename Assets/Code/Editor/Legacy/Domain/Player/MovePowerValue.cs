// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace Code.Scripts.Domain.Player
{
    /// <summary>
    /// 移動入力を保持する値オブジェクト
    /// </summary>
    public readonly struct MovePowerValue
    {
        public Vector3 Value { get; }

        public MovePowerValue(Vector3 value)
        {
            Value = value;
        }

        public static MovePowerValue Zero => new MovePowerValue(Vector3.zero);
    }
}
#endif
