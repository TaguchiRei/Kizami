using System.Collections.Generic;
using System.Text;
using Kizami.EngineAdapter;
using UnityEditor;
using UnityEngine;

namespace Kizami.EditorTools
{
    /// <summary>
    /// 遠くの敵（体を貸していない敵）を描く待機姿勢を、旧作の待機姿勢に合わせて体のプレハブの各部位（EnemyPart）に焼き込む。
    /// </summary>
    /// <remarks>
    /// フィニッシャーは、変換した FinisherStay の姿勢をそのまま写す。
    /// アタッカーとシールドを持つ敵は、旧作のモデルと別物なので、旧作の待機姿勢で足先が地面に付く位置（脛のメッシュの最も低い点）へ、
    /// kizami の脚を IK（EnemyLegs.PoseFeet）で向けた姿勢を写す。脚は、腰の水平の位置が最も近い旧作の脚と対応させる。
    /// </remarks>
    public static class EnemyIdlePoseBuilder
    {
        private const string PREFAB_DIRECTORY = "Assets/Level/Prefabs/Enemy/";
        private const string SAMPLE_DIRECTORY = "Assets/Art/sample/";
        private const string FINISHER_STAY_PATH = "Assets/Art/Animation/Finisher/FinisherStay.anim";

        [MenuItem("Kizami/Enemy/Bake Idle Poses")]
        public static void Bake()
        {
            BakeFinisher();
            BakeWalker("AttackerEnemy", "AttakkerEnemy.fbx", "EnemyAnimation/AttackerIdle.anim");
            BakeWalker("DefenderEnemy", "DefenderEnemy.fbx", "EnemyAnimation/DefenderIdle.anim");
            AssetDatabase.SaveAssets();
        }

        private static void BakeFinisher()
        {
            Bake("FinisherEnemy", root =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FINISHER_STAY_PATH);
                clip.SampleAnimation(root, 0f);
            });
        }

        private static void BakeWalker(string prefabName, string modelFile, string clipFile)
        {
            var oldFeet = GetOldFootPositions(SAMPLE_DIRECTORY + modelFile, SAMPLE_DIRECTORY + clipFile);

            Bake(prefabName, root =>
            {
                var legs = root.GetComponent<EnemyLegs>();
                var serialized = new SerializedObject(legs);
                var legArray = serialized.FindProperty("_legs");
                var targets = new List<Vector3>();

                for (var i = 0; i < legArray.arraySize; i++)
                {
                    var upper = (Transform)legArray.GetArrayElementAtIndex(i).FindPropertyRelative("_upper").objectReferenceValue;
                    targets.Add(FindNearestFoot(oldFeet, upper.localPosition));
                }

                legs.PoseFeet(targets);
            });
        }

        /// <summary>
        /// プレハブを開き、pose で姿勢を作って、各部位の体の根から見た位置と回転を読む。
        /// 姿勢を作ったプレハブは保存せず、開き直したプレハブに、読んだ値を各部位の待機姿勢として書き込んで保存する。
        /// </summary>
        private static void Bake(string prefabName, System.Action<GameObject> pose)
        {
            var path = PREFAB_DIRECTORY + prefabName + ".prefab";
            var poses = new List<(Vector3 Position, Quaternion Rotation)>();

            var posed = PrefabUtility.LoadPrefabContents(path);
            try
            {
                pose(posed);
                var rootWorldToLocal = posed.transform.worldToLocalMatrix;
                foreach (var part in posed.GetComponent<EnemyBody>().Parts)
                {
                    var matrix = rootWorldToLocal * part.Cuttable.transform.localToWorldMatrix;
                    poses.Add((matrix.GetPosition(), matrix.rotation));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(posed);
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var serialized = new SerializedObject(root.GetComponent<EnemyBody>());
                var parts = serialized.FindProperty("_parts");
                for (var i = 0; i < parts.arraySize; i++)
                {
                    var part = parts.GetArrayElementAtIndex(i);
                    part.FindPropertyRelative("_hasIdlePose").boolValue = true;
                    part.FindPropertyRelative("_idlePosition").vector3Value = poses[i].Position;
                    part.FindPropertyRelative("_idleRotation").quaternionValue = poses[i].Rotation;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 旧作のモデルに待機のクリップをかけた姿勢で、各脛のメッシュの最も低い点を、体の根の空間で返す。
        /// </summary>
        private static List<(Vector3 Hip, Vector3 Foot)> GetOldFootPositions(string modelPath, string clipPath)
        {
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            var feet = new List<(Vector3, Vector3)>();
            try
            {
                AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath).SampleAnimation(model, 0f);

                foreach (var transform in model.GetComponentsInChildren<Transform>())
                {
                    if (!transform.name.EndsWith("UnderLeg")) continue;

                    var meshFilter = transform.GetComponent<MeshFilter>();
                    var lowest = Vector3.zero;
                    var lowestHeight = float.MaxValue;
                    foreach (var vertex in meshFilter.sharedMesh.vertices)
                    {
                        var world = transform.TransformPoint(vertex);
                        if (world.y >= lowestHeight) continue;

                        lowestHeight = world.y;
                        lowest = world;
                    }

                    var hip = transform.parent.position;
                    feet.Add((model.transform.InverseTransformPoint(hip), model.transform.InverseTransformPoint(lowest)));
                }
            }
            finally
            {
                Object.DestroyImmediate(model);
            }

            return feet;
        }

        /// <summary>
        /// 腰の水平の位置が hip に最も近い旧作の脚の、足先の位置を返す。
        /// </summary>
        private static Vector3 FindNearestFoot(List<(Vector3 Hip, Vector3 Foot)> oldFeet, Vector3 hip)
        {
            var best = oldFeet[0];
            var bestDistance = float.MaxValue;
            foreach (var candidate in oldFeet)
            {
                var distance = Vector2.Distance(new Vector2(candidate.Hip.x, candidate.Hip.z), new Vector2(hip.x, hip.z));
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best.Foot;
        }

        /// <summary>
        /// 焼き込んだ待機姿勢を、プレハブごとに文字で返す。足先の高さの確認に使う。
        /// </summary>
        public static string Describe()
        {
            var builder = new StringBuilder();
            foreach (var name in new[] { "FinisherEnemy", "AttackerEnemy", "DefenderEnemy" })
            {
                var root = PrefabUtility.LoadPrefabContents(PREFAB_DIRECTORY + name + ".prefab");
                try
                {
                    builder.AppendLine("== " + name);
                    var worldToLocal = root.transform.worldToLocalMatrix;
                    foreach (var part in root.GetComponent<EnemyBody>().Parts)
                    {
                        if (part.Cuttable == null) continue;

                        var meshFilter = part.Cuttable.GetComponent<MeshFilter>();
                        var matrix = Matrix4x4.TRS(part.IdlePosition, part.IdleRotation, (worldToLocal * part.Cuttable.transform.localToWorldMatrix).lossyScale);
                        var lowest = float.MaxValue;
                        foreach (var vertex in meshFilter.sharedMesh.vertices)
                        {
                            lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(vertex).y);
                        }

                        builder.AppendLine($"{part.Cuttable.name}: idle={part.HasIdlePose} 最も低い点の高さ={lowest:F3}");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return builder.ToString();
        }
    }
}
