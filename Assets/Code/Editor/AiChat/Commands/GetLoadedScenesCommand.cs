using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetLoadedScenesCommand : IAiCommand
    {
        public string Name => "GetLoadedScenes";
        public string Description => "GetLoadedScenes";

        public string Execute(List<string> arguments)
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).path);
            return scenes.Count > 0 ? string.Join("\n", scenes) : "No scenes loaded.";
        }
    }
}