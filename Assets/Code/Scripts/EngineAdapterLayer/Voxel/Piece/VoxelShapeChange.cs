using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelPiece の形状が変わった原因。
    /// </summary>
    public enum VoxelShapeChangeCause
    {
        /// <summary> ApplyEdit で削る・盛る編集をした </summary>
        Edit,

        /// <summary> ApplyHeat で加熱されて融解した </summary>
        Melt,

        /// <summary> Slice で平面で切り分けた </summary>
        Slice
    }

    /// <summary>
    /// VoxelPiece の形状が、削る・盛る編集・融解・切り分けで変わったことを表す。
    /// </summary>
    public readonly struct VoxelShapeChange
    {
        public readonly VoxelPiece Piece;
        public readonly VoxelCsgOperation Operation;
        public readonly VoxelShapeChangeCause Cause;

        /// <summary> 値が書き換わりうる範囲（ピースのローカル空間） </summary>
        public readonly Bounds LocalBounds;

        public VoxelShapeChange(VoxelPiece piece, VoxelCsgOperation operation, VoxelShapeChangeCause cause,
            Bounds localBounds)
        {
            Piece = piece;
            Operation = operation;
            Cause = cause;
            LocalBounds = localBounds;
        }
    }
}
