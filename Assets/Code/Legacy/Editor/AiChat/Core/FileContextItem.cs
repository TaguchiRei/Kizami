// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.IO;

namespace UsefulTools.Editor.Ai
{
    [Serializable]
    public class FileContextItem
    {
        public string FilePath;
        public string FileName => Path.GetFileName(FilePath);
        public string Content;
        public bool IsEnabled = true;

        public FileContextItem() { }

        public FileContextItem(string path)
        {
            FilePath = path;
            if (File.Exists(path))
            {
                Content = File.ReadAllText(path);
            }
        }
    }
}
#endif
