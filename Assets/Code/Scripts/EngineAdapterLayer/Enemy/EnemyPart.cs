using System;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の体を作る部位1つの設定。
    /// </summary>
    [Serializable]
    public sealed class EnemyPart
    {
        [SerializeField]
        [Tooltip("部位の切断対象。基準点（Transform の位置）を、親の部位や胴体との接続部に置く")]
        private CuttableObject _cuttable;

        [SerializeField, Min(0f)]
        [Tooltip("切断面と基準点の距離がこの値（m）以下なら、接続部の近くを切ったとみなし、部位全体を落とす")]
        private float _jointDistance = 0.3f;

        [SerializeReference, SubclassSelector]
        [Tooltip("部位が壊れたときの処理。空なら、壊れても体に何も起きない")]
        private EnemyPartRole _role;

        [SerializeField]
        [Tooltip("遠くの敵（体を貸していない敵）を描くときの待機姿勢を持っているか。持っていなければ、体のプレハブの姿勢で描く")]
        private bool _hasIdlePose;

        [SerializeField]
        [Tooltip("待機姿勢の、体の根から見た部位の位置。エディタの焼き込みで書く")]
        private Vector3 _idlePosition;

        [SerializeField]
        [Tooltip("待機姿勢の、体の根から見た部位の回転。エディタの焼き込みで書く")]
        private Quaternion _idleRotation = Quaternion.identity;

        /// <summary> 部位の切断対象 </summary>
        public CuttableObject Cuttable => _cuttable;

        /// <summary> 接続部の近くを切ったとみなす、切断面と基準点の距離（m） </summary>
        public float JointDistance => _jointDistance;

        /// <summary> 部位の役割。Inspector で空にした部位では null </summary>
        public EnemyPartRole Role => _role;

        /// <summary> 遠くの敵を描くときの待機姿勢を持っているか </summary>
        public bool HasIdlePose => _hasIdlePose;

        /// <summary> 待機姿勢の、体の根から見た部位の位置 </summary>
        public Vector3 IdlePosition => _idlePosition;

        /// <summary> 待機姿勢の、体の根から見た部位の回転 </summary>
        public Quaternion IdleRotation => _idleRotation;
    }
}
