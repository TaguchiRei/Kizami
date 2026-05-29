using UnityEngine;

namespace Kizami.Presentation.Runtime.Enemy
{
    public interface IEnemyView
    {
        /// <summary> 目標地点とどの程度ずらした位置を目標にするか </summary>
        Vector3 Offset { get; set; }

        /// <summary> 移動先の目標座標 </summary>
        Vector3 TargetTransform { get; set; }

        /// <summary>
        /// 体力を0にして爆散させる
        /// </summary>
        void Kill();
    }
}