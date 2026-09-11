// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Text;
using UnityEngine.SceneManagement;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetHierarchyCommand : IAiCommand
    {
        public string Name => "GetHierarchy";
        public string Description => "ヒエラルキーを取得します。引数: [Optional: Path or #ID] 指定した場合はそのオブジェクト配下のみを表示します。";

        public string Execute(List<string> arguments)
        {
            var builder = new StringBuilder();
            
            if (arguments.Count > 0 && !string.IsNullOrWhiteSpace(arguments[0]))
            {
                var rootGo = GameObjectResolver.Resolve(arguments[0]);
                if (rootGo != null)
                {
                    BuildHierarchy(rootGo.transform, builder, 0);
                    return builder.ToString();
                }
                return $"Error: Root GameObject not found: {arguments[0]}";
            }

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) 
                BuildHierarchy(root.transform, builder, 0);
            return builder.ToString();
        }

        private void BuildHierarchy(UnityEngine.Transform current, StringBuilder builder, int depth)
        {
            builder.Append(new string(' ', depth * 2))
                   .Append(current.name)
                   .Append(" (#")
                   .Append(current.gameObject.GetInstanceID())
                   .AppendLine(")");
            
            foreach (UnityEngine.Transform child in current) BuildHierarchy(child, builder, depth + 1);
        }
    }
}
#endif
