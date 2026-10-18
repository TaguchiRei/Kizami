using System.Collections.Generic;
using System.Text;
using Kizami.EngineAdapter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Kizami.EditorTools
{
    /// <summary>
    /// 旧作（HDRPVerHookShot）のフィニッシャーのアニメーションを、kizami のフィニッシャーの体用に変換して書き出し、Animator のコントローラーとプレハブを設定する。
    /// 変換した姿勢は、旧作のモデルに旧作のアニメーションをかけた姿勢と数値で突き合わせて確かめられる。
    /// </summary>
    /// <remarks>
    /// 旧作の骨と kizami の部位は、名前の左右が逆で、位置で対応する。旧作は脚が Body の子、kizami は腕が体の根の子なので、
    /// 旧作の Body の高さの動きは、kizami の Body の高さ（旧作と違う）に足して、腕の高さにも足す。
    /// </remarks>
    public static class FinisherAnimationBuilder
    {
        private const string SAMPLE_DIRECTORY = "Assets/Art/sample/EnemyAnimation/";
        private const string OUTPUT_DIRECTORY = "Assets/Art/Animation/Finisher/";
        private const string CONTROLLER_PATH = OUTPUT_DIRECTORY + "FinisherEnemy.controller";
        private const string PREFAB_PATH = "Assets/Level/Prefabs/Enemy/FinisherEnemy.prefab";
        private const string SAMPLE_MODEL_PATH = "Assets/Art/sample/FinisherEnemy.fbx";

        /// <summary> 旧作の Body の初期の高さ（旧作のモデルの Body） </summary>
        private const string OLD_BODY = "FinisherEnemy/Body";

        private static readonly string[] ClipNames = { "Stay", "Move", "Absorption", "Aiming", "BeamStance" };

        /// <summary> 旧作の脚の名前の前半と、位置で対応する kizami の腕の名前の前半（左右が逆） </summary>
        private static readonly (string Old, string New)[] ArmNames =
        {
            ("BuckLeft", "BackRight"),
            ("BuckRight", "BackLeft"),
            ("FrontLeft", "FrontRight"),
            ("FrontRight", "FrontLeft")
        };

        [MenuItem("Kizami/Finisher/Build Animations")]
        public static void Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var oldModel = AssetDatabase.LoadAssetAtPath<GameObject>(SAMPLE_MODEL_PATH);
            var newBodyBase = prefab.transform.Find("Body").localPosition.y;
            var oldBodyBase = oldModel.transform.Find("Body").localPosition.y;

            System.IO.Directory.CreateDirectory(OUTPUT_DIRECTORY);

            var clips = new Dictionary<string, AnimationClip>();
            foreach (var name in ClipNames)
            {
                var clip = BuildClip(name, prefab.transform, newBodyBase - oldBodyBase);
                var path = OUTPUT_DIRECTORY + "Finisher" + name + ".anim";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(clip, path);
                clips[name] = clip;
            }

            BuildController(clips);
            AssetDatabase.SaveAssets();
            ApplyToPrefab();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 旧作のクリップ Finisher{name} を、kizami の体の骨の名前に直した新しいクリップにする。
        /// </summary>
        /// <param name="bodyOffset">kizami の Body の初期の高さ - 旧作の Body の初期の高さ</param>
        private static AnimationClip BuildClip(string name, Transform prefabRoot, float bodyOffset)
        {
            var oldClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SAMPLE_DIRECTORY + "Finisher" + name + ".anim");
            var clip = new AnimationClip { name = "Finisher" + name, frameRate = oldClip.frameRate };

            var bodyY = GetCurve(oldClip, OLD_BODY, "m_LocalPosition.y");
            var bodyBinding = Binding("Body", "m_LocalPosition.y");
            AnimationUtility.SetEditorCurve(clip, bodyBinding, Shifted(bodyY, bodyOffset));
            SetConstant(clip, "Body", "m_LocalPosition.x", prefabRoot.Find("Body").localPosition.x);
            SetConstant(clip, "Body", "m_LocalPosition.z", prefabRoot.Find("Body").localPosition.z);

            foreach (var (oldName, newName) in ArmNames)
            {
                var upper = newName + "UpperArm";
                var forearm = upper + "/" + newName + "Forearm";
                var upperTransform = prefabRoot.Find(upper);
                var forearmTransform = prefabRoot.Find(forearm);

                // 腕の付け根は Body に付いて動く。体の根から見た高さは、Body の高さ + 付け根の Body からの高さ
                var armHeight = upperTransform.localPosition.y - prefabRoot.Find("Body").localPosition.y;
                SetConstant(clip, upper, "m_LocalPosition.x", upperTransform.localPosition.x);
                SetConstant(clip, upper, "m_LocalPosition.z", upperTransform.localPosition.z);
                AnimationUtility.SetEditorCurve(clip, Binding(upper, "m_LocalPosition.y"),
                    Shifted(bodyY, bodyOffset + armHeight));

                SetConstant(clip, forearm, "m_LocalPosition.x", forearmTransform.localPosition.x);
                SetConstant(clip, forearm, "m_LocalPosition.y", forearmTransform.localPosition.y);
                SetConstant(clip, forearm, "m_LocalPosition.z", forearmTransform.localPosition.z);

                foreach (var axis in new[] { "x", "y", "z" })
                {
                    var property = "localEulerAnglesRaw." + axis;
                    CopyCurve(oldClip, OLD_BODY + "/" + oldName + "UpperLeg", property, clip, upper);
                    CopyCurve(oldClip, OLD_BODY + "/" + oldName + "UpperLeg/" + oldName + "UnderLeg", property, clip,
                        forearm);
                }
            }

            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(oldClip));
            return clip;
        }

        private static EditorCurveBinding Binding(string path, string property)
        {
            return EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
        }

        private static AnimationCurve GetCurve(AnimationClip clip, string path, string property)
        {
            return AnimationUtility.GetEditorCurve(clip, Binding(path, property));
        }

        /// <summary>
        /// 曲線の値をすべて offset だけずらした、新しい曲線を返す。接線は保つ。
        /// </summary>
        private static AnimationCurve Shifted(AnimationCurve curve, float offset)
        {
            var keys = curve.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].value += offset;
            }

            return new AnimationCurve(keys);
        }

        private static void SetConstant(AnimationClip clip, string path, string property, float value)
        {
            AnimationUtility.SetEditorCurve(clip, Binding(path, property), AnimationCurve.Constant(0f, 0f, value));
        }

        private static void CopyCurve(AnimationClip source, string sourcePath, string property, AnimationClip target,
            string targetPath)
        {
            var curve = GetCurve(source, sourcePath, property);
            if (curve == null) return;

            AnimationUtility.SetEditorCurve(target, Binding(targetPath, property), new AnimationCurve(curve.keys));
        }

        /// <summary>
        /// 旧作の Finisher.controller と同じ遷移のコントローラーを作る。Walking で Stay と Move、grab で Absorption → BeamStance → Aiming。
        /// </summary>
        private static void BuildController(Dictionary<string, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(CONTROLLER_PATH);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("grab", AnimatorControllerParameterType.Bool);

            var machine = controller.layers[0].stateMachine;
            var stay = machine.AddState("FinisherStay");
            stay.motion = clips["Stay"];
            var move = machine.AddState("FinisherMove");
            move.motion = clips["Move"];
            var absorption = machine.AddState("FinisherAbsorption");
            absorption.motion = clips["Absorption"];
            var stance = machine.AddState("FinisherBeamStance");
            stance.motion = clips["BeamStance"];
            var aiming = machine.AddState("FinisherAiming");
            aiming.motion = clips["Aiming"];
            machine.defaultState = stay;

            AddTransition(stay, move, 0.25f, false, 0f, ("Walking", true));
            AddTransition(move, stay, 0.25f, false, 0f, ("Walking", false));
            AddTransition(stay, absorption, 0.121f, false, 0f, ("grab", true));
            AddTransition(move, absorption, 0.08f, false, 0f, ("grab", true));
            AddTransition(absorption, stance, 0f, true, 0.996f);
            AddTransition(stance, aiming, 0f, true, 0.9967f);
            AddTransition(aiming, stay, 0f, false, 0f, ("grab", false));
        }

        private static void AddTransition(AnimatorState from, AnimatorState to, float duration, bool hasExitTime,
            float exitTime, params (string Parameter, bool Value)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = duration;
            transition.hasFixedDuration = true;
            foreach (var (parameter, value) in conditions)
            {
                transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
            }
        }

        /// <summary>
        /// フィニッシャーのプレハブに Animator を付けてコントローラーを設定し、EnemyLegs に渡す。
        /// </summary>
        private static void ApplyToPrefab()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            var root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var legs = root.GetComponent<EnemyLegs>();
                var serialized = new SerializedObject(legs);
                serialized.FindProperty("_animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 変換したクリップを kizami のフィニッシャーにかけた姿勢と、旧作のクリップを旧作のモデルにかけた姿勢を、時刻ごとに比べる。
        /// 比べるのは、Body から見た各腕の付け根・前腕の付け根・メッシュの中心の位置で、ずれの最大（m）を返す。
        /// </summary>
        public static string Verify()
        {
            var builder = new StringBuilder();
            var oldRoot = CreateOldHierarchy();
            var newRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
            var worst = 0f;

            try
            {
                foreach (var name in ClipNames)
                {
                    var oldClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SAMPLE_DIRECTORY + "Finisher" + name + ".anim");
                    var newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OUTPUT_DIRECTORY + "Finisher" + name + ".anim");
                    var clipWorst = 0f;

                    for (var i = 0; i <= 20; i++)
                    {
                        var time = oldClip.length * i / 20f;
                        oldClip.SampleAnimation(oldRoot, time);
                        newClip.SampleAnimation(newRoot, time);
                        clipWorst = Mathf.Max(clipWorst, CompareBodySpace(oldRoot.transform, newRoot.transform));
                    }

                    builder.AppendLine($"{name}: 最大のずれ {clipWorst:F5} m");
                    worst = Mathf.Max(worst, clipWorst);
                }
            }
            finally
            {
                Object.DestroyImmediate(oldRoot);
                Object.DestroyImmediate(newRoot);
            }

            builder.AppendLine($"全体の最大のずれ {worst:F5} m");
            return builder.ToString();
        }

        /// <summary>
        /// 旧作のプレハブの階層（外の根 → 内側の FinisherEnemy → Body の下に脚）を、旧作のモデルから作る。
        /// </summary>
        private static GameObject CreateOldHierarchy()
        {
            var wrapper = new GameObject("OldWrapper");
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SAMPLE_MODEL_PATH));
            model.name = "FinisherEnemy";
            model.transform.SetParent(wrapper.transform, false);

            var body = model.transform.Find("Body");
            foreach (var (oldName, _) in ArmNames)
            {
                var upper = model.transform.Find(oldName + "UpperLeg");
                upper.SetParent(body, true);
            }

            return wrapper;
        }

        /// <summary>
        /// 旧作と kizami の各腕の、Body から見た位置のずれの最大（m）を返す。
        /// </summary>
        private static float CompareBodySpace(Transform oldRoot, Transform newRoot)
        {
            var oldBody = oldRoot.Find(OLD_BODY);
            var newBody = newRoot.Find("Body");
            var worst = 0f;

            foreach (var (oldName, newName) in ArmNames)
            {
                var oldUpper = oldBody.Find(oldName + "UpperLeg");
                var oldLower = oldUpper.Find(oldName + "UnderLeg");
                var newUpper = newRoot.Find(newName + "UpperArm");
                var newLower = newUpper.Find(newName + "Forearm");

                worst = Mathf.Max(worst, Offset(oldBody, oldUpper, newBody, newUpper));
                worst = Mathf.Max(worst, Offset(oldBody, oldLower, newBody, newLower));

                // 前腕の先端の側へ 2m 先の点も比べ、回転のずれも見る
                var oldTip = oldBody.InverseTransformPoint(oldLower.TransformPoint(Vector3.down * 2f));
                var newTip = newBody.InverseTransformPoint(newLower.TransformPoint(Vector3.down * 2f));
                worst = Mathf.Max(worst, Vector3.Distance(oldTip, newTip));
            }

            return worst;
        }

        private static float Offset(Transform oldBody, Transform oldPart, Transform newBody, Transform newPart)
        {
            var oldPosition = oldBody.InverseTransformPoint(oldPart.position);
            var newPosition = newBody.InverseTransformPoint(newPart.position);
            return Vector3.Distance(oldPosition, newPosition);
        }
    }
}
