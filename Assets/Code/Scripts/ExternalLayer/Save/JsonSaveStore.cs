using System;
using System.IO;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// 保存データ1種類を、JsonUtility で SaveDirectory の下のファイルへ書き出し・読み込む窓口。
    /// PC・スマホ・VR で同じ扱いにできるよう、PlayerPrefs ではなくファイルにする。
    /// </summary>
    /// <typeparam name="T">保存するデータ。JsonUtility で書き出せる型</typeparam>
    public sealed class JsonSaveStore<T> where T : class, new()
    {
        private readonly string _path;

        /// <param name="fileName">保存先のファイル名（拡張子を含む）</param>
        public JsonSaveStore(string fileName)
        {
            _path = Path.Combine(SaveDirectory.Path, fileName);
        }

        /// <summary>
        /// 保存データを読み込む。
        /// </summary>
        /// <param name="data">読み込んだデータ。読み込めなければ null</param>
        /// <returns>読み込めたか。ファイルがない、または壊れているときは false</returns>
        public bool TryLoad(out T data)
        {
            data = null;
            if (!File.Exists(_path)) return false;

            try
            {
                data = JsonUtility.FromJson<T>(File.ReadAllText(_path));
            }
            catch (Exception e)
            {
                Debug.LogError($"保存データ {_path} を読み込めません。{e.Message}");
            }

            return data != null;
        }

        /// <summary>
        /// 保存データを書き出す。
        /// </summary>
        /// <param name="data">書き出すデータ</param>
        public void Save(T data)
        {
            try
            {
                Directory.CreateDirectory(SaveDirectory.Path);
                File.WriteAllText(_path, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"保存データ {_path} を書き出せません。{e.Message}");
            }
        }
    }
}
