using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelPiece の形状が、削る・盛る編集で変わったことを表す。
    /// </summary>
    public readonly struct VoxelShapeChange
    {
        public readonly VoxelPiece Piece;
        public readonly VoxelCsgOperation Operation;

        /// <summary> 値が書き換わりうる範囲（ピースのローカル空間） </summary>
        public readonly Bounds LocalBounds;

        public VoxelShapeChange(VoxelPiece piece, VoxelCsgOperation operation, Bounds localBounds)
        {
            Piece = piece;
            Operation = operation;
            LocalBounds = localBounds;
        }
    }
}
