using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel.DebugTools
{
    /// <summary>
    /// VoxelModelLoader（未設定なら VoxelPiece）のコールバックを登録し、
    /// 呼ばれた回数と直近の内容を画面に、分離した各ピースの情報をログに出す検証用コンポーネント。
    /// </summary>
    public sealed class VoxelDebugEventMonitor : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("モデル全体のコールバックを見る対象")]
        private VoxelModelLoader _loader;

        [SerializeField]
        [Tooltip("_loader が未設定のとき、コールバックを見る対象のピース")]
        private VoxelPiece _piece;

        private readonly List<IDisposable> _registrations = new();
        private int _shapeChangedCount;
        private int _meltChangedCount;
        private int _splitCount;
        private int _destroyedCount;
        private string _lastSplit = "-";
        private string _lastDestroyed = "-";

        private void Start()
        {
            if (_loader != null)
            {
                _registrations.Add(_loader.RegisterOnPieceShapeChanged(OnShapeChanged));
                _registrations.Add(_loader.RegisterOnPieceSplit(OnSplit));
                _registrations.Add(_loader.RegisterOnPieceDestroyed(OnDestroyed));
                return;
            }

            if (_piece == null) return;

            _registrations.Add(_piece.RegisterOnShapeChanged(OnShapeChanged));
            _registrations.Add(_piece.RegisterOnSplit(OnSplit));
            _registrations.Add(_piece.RegisterOnDestroyed(OnDestroyed));
        }

        private void OnDestroy()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }

            _registrations.Clear();
        }

        private void OnShapeChanged(VoxelShapeChange change)
        {
            _shapeChangedCount++;
            if (change.Cause == VoxelShapeChangeCause.Melt) _meltChangedCount++;
        }

        private void OnSplit(VoxelPiece[] pieces)
        {
            _splitCount++;

            var source = pieces[0];
            _lastSplit = $"{source.name}（世代 {source.Generation}）から {pieces.Length - 1} 個";

            for (var i = 1; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                Debug.Log($"[VoxelDebug] 分離: {piece.name}  分離元: {source.name}  パーツ: {piece.PartPath}  " +
                          $"世代: {piece.Generation}  体積: {piece.Volume:0.0000} m³", piece);
            }
        }

        private void OnDestroyed(VoxelPiece piece)
        {
            _destroyedCount++;
            _lastDestroyed = $"{piece.name}（世代 {piece.Generation}）";
        }

        private void OnGUI()
        {
            if (!VoxelDebugHud.IsVisible) return;

            var text = $"形状変化: {_shapeChangedCount} 回（うち融解 {_meltChangedCount} 回）  " +
                       $"分離: {_splitCount} 回（直近: {_lastSplit}）\n" +
                       $"破棄: {_destroyedCount} 個（直近: {_lastDestroyed}）";

            if (_loader != null)
            {
                text += $"  ピース数: {_loader.Pieces.Count}  " +
                        $"モデルの体積: {_loader.Volume:0.000} m³（初期比 {_loader.RelativeVolume:P0}）";
            }

            GUI.Box(new Rect(10f, 236f, 560f, 44f), text);
        }
    }
}
