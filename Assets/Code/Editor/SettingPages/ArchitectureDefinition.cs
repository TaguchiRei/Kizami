using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UsefulAttribute;

namespace UsefulTools.Editor
{
    [Serializable]
    public class LayerDefinition
    {
        public string layerName;
        public string relativePath;
        public bool createAsmdef = true;
        public List<string> subFolders = new List<string>();
        public List<string> references = new List<string>(); // GUID or Name for asmdef references
    }

    [CreateAssetMenu(fileName = "ArchitectureDefinition", menuName = "UsefulTools/Architecture Definition")]
    public class ArchitectureDefinition : MethodExecutorScriptableObject
    {
        public string rootPath = "Assets/Code/Scripts";
        public List<LayerDefinition> layers = new();

        [MethodExecutor("SetDefault", true)]
        public void SetDefault()
        {
            layers.Clear();

            // 3. Domain (完全独立)
            layers.Add(new LayerDefinition
            {
                layerName = "Domain",
                relativePath = "Domain",
                subFolders = new List<string> { "Entities", "ValueObjects", "Services" },
                references = new List<string>()
            });

            // 2. Application (Domainに依存)
            layers.Add(new LayerDefinition
            {
                layerName = "Application",
                relativePath = "Application",
                subFolders = new List<string>
                {
                    "UseCases", "Services", "Interfaces/Infrastructure", "Interfaces/Presentation",
                    "Interfaces/Repositories", "Interfaces/Gateways", "DTOs"
                },
                references = new List<string> { "Domain" }
            });

            // 1. Infrastructure (Applicationに依存)
            layers.Add(new LayerDefinition
            {
                layerName = "Infrastructure",
                relativePath = "Infrastructure",
                subFolders = new List<string> { "Adapters", "Gateways" },
                references = new List<string> { "Application" }
            });

            // 4. DataAccess (Application, Domainに依存)
            layers.Add(new LayerDefinition
            {
                layerName = "DataAccess",
                relativePath = "DataAccess",
                subFolders = new List<string> { "Repositories" },
                references = new List<string> { "Application", "Domain" }
            });

            // 5. Presentation (外部依存なし - Presenter)
            layers.Add(new LayerDefinition
            {
                layerName = "Presentation",
                relativePath = "Presentation",
                subFolders = new List<string> { "Presenters", "Interfaces" },
                references = new List<string>()
            });

            // 6. View (Presentationに依存)
            layers.Add(new LayerDefinition
            {
                layerName = "View",
                relativePath = "View",
                subFolders = new List<string>(),
                references = new List<string> { "Presentation" }
            });

            // 7. Composition (全レイヤーに依存)
            layers.Add(new LayerDefinition
            {
                layerName = "Composition",
                relativePath = "Composition",
                subFolders = new List<string> { "Initializers" },
                references = new List<string>
                    { "Domain", "Application", "Infrastructure", "DataAccess", "Presentation", "View" }
            });
        }

        [MethodExecutor("GenerateFromExisting", true)]
        public void GenerateFromExisting()
        {
            layers.Clear();

            string fullRootPath = rootPath;
            if (!fullRootPath.StartsWith("Assets"))
            {
                fullRootPath = "Assets/" + fullRootPath;
            }

            // Convert to system path
            string systemPath = Application.dataPath.Replace("Assets", "") + fullRootPath;

            if (!Directory.Exists(systemPath))
            {
                Debug.LogError($"[UsefulTools] Root path does not exist: {systemPath}");
                return;
            }

            string[] layerDirs = Directory.GetDirectories(systemPath);
            foreach (var layerDir in layerDirs)
            {
                string layerFolderName = Path.GetFileName(layerDir);
                var layerDef = new LayerDefinition
                {
                    layerName = layerFolderName,
                    relativePath = layerFolderName,
                    createAsmdef = false,
                    subFolders = new List<string>(),
                    references = new List<string>()
                };

                // Check for asmdef
                string[] asmdefs = Directory.GetFiles(layerDir, "*.asmdef");
                if (asmdefs.Length > 0)
                {
                    layerDef.createAsmdef = true;
                    string asmdefContent = File.ReadAllText(asmdefs[0]);
                    try
                    {
                        var asmData = JsonUtility.FromJson<AsmdefData>(asmdefContent);
                        if (!string.IsNullOrEmpty(asmData.name))
                        {
                            layerDef.layerName = asmData.name;
                        }

                        if (asmData.references != null)
                        {
                            layerDef.references = new List<string>(asmData.references);
                        }
                    }
                    catch
                    {
                        layerDef.layerName = Path.GetFileNameWithoutExtension(asmdefs[0]);
                    }
                }

                // Scan subfolders
                string[] subDirs = Directory.GetDirectories(layerDir);
                foreach (var subDir in subDirs)
                {
                    layerDef.subFolders.Add(Path.GetFileName(subDir));
                }

                layers.Add(layerDef);
            }
            
            Debug.Log($"[UsefulTools] Architecture Definition generated from existing structure at {rootPath}");
        }

        [Serializable]
        private class AsmdefData
        {
            public string name;
            public string[] references;
        }
    }
}