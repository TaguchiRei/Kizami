using System.Linq;
using Kizami.EngineAdapter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Kizami.EditorTools
{
    /// <summary>
    /// ArmModel のプレハブから、右腕に Two Bone IK を付けた一人称の腕のプレハブ（PlayerArm）を作る。
    /// 作り直すと、PlayerArm プレハブは上書きされる。
    /// </summary>
    public static class PlayerArmPrefabBuilder
    {
        private const string SOURCE_PATH = "Assets/Level/Prefabs/ArmModel.prefab";
        private const string MODEL_PATH = "Assets/Art/Models/Player/ArmModel.fbx";
        private const string OUTPUT_PATH = "Assets/Level/Prefabs/PlayerArm.prefab";

        /// <summary> 肘の向きを決める目印を、下腕の骨から置く位置（腕のプレハブの根から見た向き。右下の後ろ） </summary>
        private static readonly Vector3 HintOffset = new(0.15f, -0.2f, -0.15f);

        [MenuItem("Kizami/Player/Build Arm Prefab")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_PATH);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = "PlayerArm";

            try
            {
                var upperArm = FindBone(instance.transform, "upperArm_R");
                var underArm = FindBone(instance.transform, "underArm_R");
                var hand = FindBone(instance.transform, "hand_R");

                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                if (animator.avatar == null) animator.avatar = AssetDatabase.LoadAllAssetsAtPath(MODEL_PATH).OfType<Avatar>().FirstOrDefault();
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;

                var rigObject = new GameObject("Rig");
                rigObject.transform.SetParent(instance.transform, false);
                var rig = rigObject.AddComponent<Rig>();
                rig.weight = 1f;

                var target = CreateChild(rigObject.transform, "SwingTarget", hand);
                var hint = CreateChild(rigObject.transform, "SwingHint", underArm);
                hint.position += instance.transform.TransformVector(HintOffset);

                var ikObject = new GameObject("RightArmIK");
                ikObject.transform.SetParent(rigObject.transform, false);
                var constraint = ikObject.AddComponent<TwoBoneIKConstraint>();
                SetConstraint(constraint, upperArm, underArm, hand, target, hint);

                var rigBuilder = instance.AddComponent<RigBuilder>();
                rigBuilder.layers.Add(new RigLayer(rig, true));

                var adapter = instance.AddComponent<PlayerArmAdapter>();
                var adapterObject = new SerializedObject(adapter);
                adapterObject.FindProperty("_rig").objectReferenceValue = rig;
                adapterObject.FindProperty("_shoulder").objectReferenceValue = upperArm;
                adapterObject.FindProperty("_swingTarget").objectReferenceValue = target;
                adapterObject.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(instance, OUTPUT_PATH);
                Debug.Log($"腕のプレハブを作りました: {OUTPUT_PATH}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// 名前が name の骨を、根の子孫から探す。
        /// </summary>
        private static Transform FindBone(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }

            throw new System.InvalidOperationException($"骨 {name} が見つかりません。");
        }

        /// <summary>
        /// parent の子に、at と同じ位置と向きで空の GameObject を作る。
        /// </summary>
        private static Transform CreateChild(Transform parent, string name, Transform at)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.SetPositionAndRotation(at.position, at.rotation);
            return child;
        }

        private static void SetConstraint(TwoBoneIKConstraint constraint, Transform root, Transform mid, Transform tip,
            Transform target, Transform hint)
        {
            var serialized = new SerializedObject(constraint);
            serialized.FindProperty("m_Data.m_Root").objectReferenceValue = root;
            serialized.FindProperty("m_Data.m_Mid").objectReferenceValue = mid;
            serialized.FindProperty("m_Data.m_Tip").objectReferenceValue = tip;
            serialized.FindProperty("m_Data.m_Target").objectReferenceValue = target;
            serialized.FindProperty("m_Data.m_Hint").objectReferenceValue = hint;
            serialized.FindProperty("m_Data.m_TargetPositionWeight").floatValue = 1f;
            serialized.FindProperty("m_Data.m_TargetRotationWeight").floatValue = 1f;
            serialized.FindProperty("m_Data.m_HintWeight").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
