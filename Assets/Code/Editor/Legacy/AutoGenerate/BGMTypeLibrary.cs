// [Legacy] 作り直しに伴い全体を無効化
#if false
// 自動生成ファイルの為、手動での編集は上書きされます。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsefulTOols.AutoGenerate
{
    [CreateAssetMenu(fileName = "BGMTypeLibrary", menuName = "UsefulTools/Audio/BGMTypeLibrary")]
    public class BGMTypeLibrary : ScriptableObject
    {
        [Serializable]
        public struct AudioPair
        {
            public BGMType Type;
            public AudioClip Clip;
        }

        public List<AudioPair> Clips = new List<AudioPair>();

        public AudioClip GetClip(BGMType type)
        {
            var pair = Clips.Find(p => p.Type == type);
            return pair.Clip;
        }
    }
}
#endif
