using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージのギミック（破壊対象・スポーン位置）の ID の目印。ギミックと同じ GameObject に付ける。
    /// シーンをまたぐ参照は Unity にない為、街にあるギミックを、ステージの定義や台本が ID で探せるようにする。
    /// </summary>
    public sealed class StageGimmick : MonoBehaviour
    {
        private static readonly List<StageGimmick> _active = new();

        [SerializeField]
        [Tooltip("ギミックの ID。街の中で重複させない")]
        private string _id = string.Empty;

        /// <summary> ギミックの ID </summary>
        public string Id => _id;

        /// <summary>
        /// 有効なギミックから、ID が一致するものを探す。
        /// </summary>
        public static bool TryFind(string id, out StageGimmick gimmick)
        {
            foreach (var candidate in _active)
            {
                if (candidate._id != id) continue;

                gimmick = candidate;
                return true;
            }

            gimmick = null;
            return false;
        }

        /// <summary>
        /// ID を設定する。シーンを組み立てるエディタ拡張が使う。
        /// </summary>
        public void SetId(string id)
        {
            _id = id;
        }

        private void OnEnable()
        {
            _active.Add(this);
        }

        private void OnDisable()
        {
            _active.Remove(this);
        }
    }
}
