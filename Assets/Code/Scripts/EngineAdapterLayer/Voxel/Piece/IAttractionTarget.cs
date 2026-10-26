using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 切り離されたボクセルの物を、優先して自分の側へ倒させる引き寄せ対象。VoxelAttraction に登録する。
    /// </summary>
    public interface IAttractionTarget
    {
        /// <summary>
        /// 今の引き寄せる位置を返す。引き寄せないときは false。
        /// </summary>
        /// <param name="point">引き寄せる位置（ワールド空間）</param>
        bool TryGetAttractionPoint(out Vector3 point);
    }
}
