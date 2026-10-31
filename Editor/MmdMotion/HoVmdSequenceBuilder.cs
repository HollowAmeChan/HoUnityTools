using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.Editor.Pmx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    public static class HoVmdSequenceBuilder
    {
        public static void Configure(HoVmdSequencePlayer sequence)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出播放模式再生成配置。");
            Animator animator = sequence.TargetAnimator;
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("请选择主骨架对应的 Humanoid Animator。");
            if (sequence.ReferenceRigAsset == null || !AssetDatabase.GetAssetPath(sequence.ReferenceRigAsset).EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择一次参考 PMX（作者 sizing 骨架或 Sour 均可）。仅读取尺寸和骨骼位置，不需要使用 PMX 角色渲染。");
            if (sequence.MotionClip == null) throw new InvalidOperationException("请选择动作 VMD。");
            GameObject actor = animator.gameObject;
            var roots = actor.scene.GetRootGameObjects();
            var sequenceObject = sequence.gameObject;
            var referencePositions = ReferencePositions(AssetDatabase.GetAssetPath(sequence.ReferenceRigAsset));
            // A correct FK pose is not enough when the mesh is skinned to MCH
            // followers. Reuse the associated FBX manifest instead of guessing
            // constraints from bone names or adding a second deformation system.
            Hollow.HoUnityTools.Editor.RigConstraints.HoFbxConstraintImportUtility.EnsurePrimaryAvatarConfigured(animator);
            ClearGenerated(sequence);
            Transform sourceRoot = Child(sequenceObject.transform, "01 MMD 参考骨架 · 仅驱动");
            var positions = referencePositions;
            var source = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var pair in positions)
            {
                Transform bone = Child(sourceRoot, pair.Key);
                bone.localPosition = pair.Value;
                source.Add(pair.Key, bone);
            }
            foreach (var pair in source)
            {
                string parent = CanonicalParent(pair.Key);
                if (parent != null && source.TryGetValue(parent, out Transform parentBone)) pair.Value.SetParent(parentBone, true);
            }
            var sourceIk = new Dictionary<string, HoIkConstraint>();
            foreach (string side in new[] { "左", "右" })
            {
                var leg = source[side + "足ＩＫ"].gameObject.AddComponent<HoIkConstraint>();
                leg.IkName = side + "足ＩＫ"; leg.Effector = source[side + "足首"]; leg.Target = source[side + "足ＩＫ"];
                leg.Mode = HoIkConstraint.UpdateMode.Manual; leg.UsePole = false; leg.Iterations = 40;
                leg.Links.Add(new HoIkConstraint.Link { bone = source[side + "ひざ"], useAxisMask = true,
                    useX = true, useY = false, useZ = false, useLocalEulerLimits = true,
                    localEulerMin = new Vector3(.05f, 0, 0), localEulerMax = new Vector3(175, 0, 0) });
                leg.Links.Add(new HoIkConstraint.Link { bone = source[side + "足"] });
                leg.CaptureBindPose(); sourceIk.Add(leg.IkName, leg);
                var toe = source[side + "つま先ＩＫ"].gameObject.AddComponent<HoIkConstraint>();
                toe.IkName = side + "つま先ＩＫ"; toe.Effector = source[side + "つま先"]; toe.Target = source[side + "つま先ＩＫ"];
                toe.Mode = HoIkConstraint.UpdateMode.Manual; toe.UsePole = false; toe.Iterations = 6;
                toe.Links.Add(new HoIkConstraint.Link { bone = source[side + "足首"] });
                toe.CaptureBindPose(); sourceIk.Add(toe.IkName, toe);
            }
            HoVmdMotionPlayer body = sourceRoot.gameObject.AddComponent<HoVmdMotionPlayer>();
            body.Animation = sequence.MotionClip;
            body.ModelRoot = sourceRoot; body.SkeletonRoot = sourceRoot;
            ConfigurePlayer(body, false, true, false);
            sequence.Body = body;
            foreach (var old in actor.GetComponentsInChildren<HoVmdMotionPlayer>(true)) old.enabled = false;

            HoVmdHumanoidRetargeter retargeter = Undo.AddComponent<HoVmdHumanoidRetargeter>(sequenceObject);
            retargeter.SourceSpace = sourceRoot; retargeter.TargetSpace = actor.transform;
            retargeter.SourceHips = source["下半身"]; retargeter.TargetHips = animator.GetBoneTransform(HumanBodyBones.Hips);
            retargeter.TranslationScale = actor.transform.InverseTransformPoint(retargeter.TargetHips.position).y / sourceRoot.InverseTransformPoint(source["下半身"].position).y;
            foreach (var pair in source)
            {
                HumanBodyBones human = HoVmdMotionPlayer.HumanBodyBone(pair.Key);
                if (human == HumanBodyBones.LastBone || pair.Key == "腰") continue;
                Transform target = animator.GetBoneTransform(human);
                if (target == null || retargeter.Bones.Any(x => x.target == target)) continue;
                Quaternion offset = Quaternion.Inverse(sourceRoot.rotation) * target.rotation;
                retargeter.Bones.Add(new HoVmdHumanoidRetargeter.Bone { source = pair.Value, target = target,
                    rotationOffset = offset, appendRotation = human == HumanBodyBones.LeftEye || human == HumanBodyBones.RightEye ? source["両目"] : null });
            }
            var ordered = retargeter.Bones.OrderBy(x => Depth(x.target)).ToArray(); retargeter.Bones.Clear();
            foreach (var item in ordered) retargeter.Bones.Add(item);
            SetRotationOffsets(retargeter, animator);
            // Reference IK is solved first. Target IK only corrects the new proportions.
            var targetControls = actor.GetComponentsInChildren<HoIkConstraint>(true);
            Transform generatedControls = null;
            foreach (string side in new[] { "左", "右" })
            {
                bool left = side == "左";
                Transform foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform knee = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var targetIk = targetControls.FirstOrDefault(x => x.Effector == foot);
                if (targetIk == null)
                {
                    if (generatedControls == null) generatedControls = Child(sequence.transform, "05 自动脚 IK");
                    Transform goal = Child(generatedControls, side + "足控制点");
                    goal.position = foot.position;
                    Transform pole = Child(generatedControls, side + "膝指向");
                    pole.position = knee.position + actor.transform.forward * .5f;
                    targetIk = goal.gameObject.AddComponent<HoIkConstraint>();
                    targetIk.IkName = side + "足ＩＫ"; targetIk.Target = goal; targetIk.Pole = pole; targetIk.Effector = foot;
                    targetIk.Links.Add(new HoIkConstraint.Link { bone = knee });
                    targetIk.Links.Add(new HoIkConstraint.Link { bone = thigh });
                    targetIk.CaptureBindPose();
                }
                targetIk.Mode = HoIkConstraint.UpdateMode.Manual;
                retargeter.Feet.Add(new HoVmdHumanoidRetargeter.Foot { sourceIk = sourceIk[side + "足ＩＫ"], targetIk = targetIk });
                Transform toes = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                var toeIk = targetControls.FirstOrDefault(x => toes != null && x.Effector == toes);
                if (toeIk != null)
                {
                    toeIk.Mode = HoIkConstraint.UpdateMode.Manual;
                    retargeter.Feet.Add(new HoVmdHumanoidRetargeter.Foot { sourceIk = sourceIk[side + "つま先ＩＫ"], targetIk = toeIk });
                }
            }
            retargeter.CaptureRestPositions(); sequence.Retargeter = retargeter;

            HoVmdMotionPlayer face = Child(sequenceObject.transform, "02 表情 · 角色现有形态键").gameObject.AddComponent<HoVmdMotionPlayer>();
            face.Animation = sequence.ExpressionClip;
            face.ModelRoot = actor.transform; face.SkeletonRoot = sourceRoot;
            ConfigurePlayer(face, false, true, true);
            sequence.Face = face;
            // Capture the rest basis once, before applying any animated pose.
            body.RebuildBindings(); face.RebuildBindings();
            foreach (Behaviour behaviour in actor.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != null && (behaviour.GetType().Namespace?.Contains("FaceTracking") == true ||
                    behaviour.GetType().Namespace?.Contains("BoneRendering") == true)) behaviour.enabled = false;
            foreach (var camera in roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)))
            {
                camera.enabled = false; camera.tag = "Untagged";
                foreach (var listener in camera.GetComponents<AudioListener>()) listener.enabled = false;
                foreach (var behaviour in camera.GetComponents<Behaviour>())
                    if (behaviour.GetType().Name == "HoSceneToGameViewSync") behaviour.enabled = false;
                if (camera.GetComponent<HoVmdCameraPlayer>() != null) camera.GetComponent<HoVmdCameraPlayer>().enabled = false;
            }
            Camera videoCamera = Child(sequenceObject.transform, "03 主相机 · 作者运镜").gameObject.AddComponent<Camera>();
            videoCamera.gameObject.AddComponent<AudioListener>();
            Camera previousCamera = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).First();
            videoCamera.CopyFrom(previousCamera); videoCamera.enabled = true; videoCamera.tag = "MainCamera";
            videoCamera.nearClipPlane = .03f; videoCamera.farClipPlane = 200;
            var cameraData = videoCamera.GetUniversalAdditionalCameraData();
            var previousData = previousCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true; cameraData.volumeLayerMask = previousData.volumeLayerMask;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            HoVmdCameraPlayer cameraPlayer = videoCamera.gameObject.AddComponent<HoVmdCameraPlayer>();
            cameraPlayer.Animation = sequence.CameraClip;
            cameraPlayer.playOnEnable = false; cameraPlayer.motionOrigin = actor.transform;
            cameraPlayer.mmdToUnityScale = .08f * retargeter.TranslationScale;
            cameraPlayer.distanceMultiplier = 1;
            cameraPlayer.portraitFraming = true;
            cameraPlayer.centerOffset = new Vector3(0, .08f, 0);
            sequence.CameraPlayer = cameraPlayer;
            AudioSource music = Child(sequenceObject.transform, "04 BGM").gameObject.AddComponent<AudioSource>();
            music.clip = sequence.Soundtrack;
            music.playOnAwake = false; music.spatialBlend = 0; music.loop = false;
            sequence.Music = music;
            cameraPlayer.ApplyAtFrame(0);

            EditorPrefs.SetString("HoVmdSequence.ReferenceRig", AssetDatabase.GetAssetPath(sequence.ReferenceRigAsset));
            EditorUtility.SetDirty(sequence);
        }

        static void ClearGenerated(HoVmdSequencePlayer sequence)
        {
            var old = new Component[] { sequence.Body, sequence.Face, sequence.CameraPlayer, sequence.Music };
            foreach (var item in old)
                if (item != null && item.transform != sequence.transform && item.transform.IsChildOf(sequence.transform))
                    Undo.DestroyObjectImmediate(item.gameObject);
            if (sequence.Retargeter != null && sequence.Retargeter.gameObject == sequence.gameObject)
                Undo.DestroyObjectImmediate(sequence.Retargeter);
            Transform generatedIk = sequence.transform.Find("05 自动脚 IK");
            if (generatedIk != null) Undo.DestroyObjectImmediate(generatedIk.gameObject);
        }

        public static void SetRotationOffsets(HoVmdHumanoidRetargeter rig, Animator animator)
        {
            var source = rig.SourceSpace.GetComponentsInChildren<Transform>().ToDictionary(x => x.name, x => x);
            var armOffsets = new Dictionary<string, Quaternion>();
            foreach (string side in new[] { "左", "右" })
            {
                HumanBodyBones upper = side == "左" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                HumanBodyBones lower = side == "左" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                Vector3 targetDirection = rig.TargetSpace.InverseTransformDirection(animator.GetBoneTransform(lower).position - animator.GetBoneTransform(upper).position);
                Vector3 sourceDirection = rig.SourceSpace.InverseTransformDirection(source[side + "ひじ"].position - source[side + "腕"].position);
                armOffsets.Add(side, Quaternion.FromToRotation(targetDirection, sourceDirection));
            }
            foreach (var bone in rig.Bones)
            {
                bone.rotationOffset = Quaternion.Inverse(rig.TargetSpace.rotation) * bone.target.rotation;
                string name = bone.source.name;
                bool arm = name.Length > 1 && (name.Contains("腕") || name.Contains("ひじ") || name.Contains("手首") || name.Contains("指"));
                if (arm && armOffsets.TryGetValue(name.Substring(0, 1), out var offset))
                    bone.rotationOffset = offset * bone.rotationOffset;
            }
        }

        static void ConfigurePlayer(HoVmdMotionPlayer player, bool play, bool bones, bool morphs)
        {
            var serialized = new SerializedObject(player);
            serialized.FindProperty("playOnEnable").boolValue = play;
            serialized.FindProperty("applyBones").boolValue = bones;
            serialized.FindProperty("applyMorphs").boolValue = morphs;
            serialized.FindProperty("applyIkToggles").boolValue = !morphs;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            player.Playing = false;
        }

        static Dictionary<string, Vector3> ReferencePositions(string pmx)
        {
            var doc = HoPmxReader.Read(Path.GetFullPath(pmx));
            var positions = doc.Bones.ToDictionary(x => CanonicalName(x.Name), x => new Vector3(-x.Position.X, x.Position.Y, -x.Position.Z) * .08f);
            // This author's sizing PMX contains positions but has a deliberately
            // flattened/invalid hierarchy and omits right fingers. Use the canonical
            // semantic MMD hierarchy, and mirror only absent right-hand rest points.
            positions["全ての親"] = Vector3.zero;
            foreach (var pair in positions.ToArray())
                if (pair.Key.StartsWith("左", StringComparison.Ordinal) && pair.Key.Contains("指"))
                {
                    string right = "右" + pair.Key.Substring(1);
                    if (!positions.ContainsKey(right)) positions[right] = new Vector3(-pair.Value.x, pair.Value.y, pair.Value.z);
                }
            if (!positions.ContainsKey("下半身")) throw new InvalidDataException("参考 PMX 缺少下半身骨骼。");
            foreach (string required in new[] { "センター", "上半身", "首", "頭" })
                if (!positions.ContainsKey(required)) throw new InvalidDataException("参考 PMX 缺少：" + required);
            if (!positions.ContainsKey("グルーブ")) positions["グルーブ"] = positions["センター"] + Vector3.up * .016f;
            if (!positions.ContainsKey("上半身2")) positions["上半身2"] = Vector3.Lerp(positions["上半身"], positions["首"], .5f);
            if (!positions.ContainsKey("両目")) positions["両目"] = positions["頭"] + Vector3.up * .1f;
            if (!positions.ContainsKey("腰")) positions["腰"] = positions["下半身"];
            foreach (string side in new[] { "左", "右" })
            {
                foreach (string required in new[] { "足", "ひざ", "足首", "つま先", "腕", "ひじ", "手首" })
                    if (!positions.ContainsKey(side + required)) throw new InvalidDataException("参考 PMX 缺少：" + side + required);
                if (!positions.ContainsKey(side + "腕捩")) positions[side + "腕捩"] = (positions[side + "腕"] + positions[side + "ひじ"]) * .5f;
                if (!positions.ContainsKey(side + "手捩")) positions[side + "手捩"] = (positions[side + "ひじ"] + positions[side + "手首"]) * .5f;
                if (!positions.ContainsKey(side + "足ＩＫ")) positions[side + "足ＩＫ"] = positions[side + "足首"];
                if (!positions.ContainsKey(side + "つま先ＩＫ")) positions[side + "つま先ＩＫ"] = positions[side + "つま先"];
                if (!positions.ContainsKey(side + "足IK親"))
                {
                    Vector3 foot = positions[side + "足首"];
                    positions[side + "足IK親"] = new Vector3(foot.x, 0, foot.z);
                }
            }
            var core = new HashSet<string> { "全ての親", "センター", "グルーブ", "腰", "両目", "左腕捩", "右腕捩", "左手捩", "右手捩",
                "左足ＩＫ", "右足ＩＫ", "左つま先ＩＫ", "右つま先ＩＫ", "左足IK親", "右足IK親" };
            return positions.Where(x => core.Contains(x.Key) || HoVmdMotionPlayer.HumanBodyBone(x.Key) != HumanBodyBones.LastBone)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        static string CanonicalName(string name)
        {
            name = name.Normalize(System.Text.NormalizationForm.FormKC);
            if (name.EndsWith("IK", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 2) + "ＩＫ";
            if (name.Contains("指") && name.Length > 0 && name[name.Length - 1] >= '0' && name[name.Length - 1] <= '3')
                name = name.Substring(0, name.Length - 1) + "０１２３"[name[name.Length - 1] - '0'];
            return name;
        }

        static string CanonicalParent(string name)
        {
            switch (name)
            {
                case "全ての親": return null;
                case "センター": case "左足IK親": case "右足IK親": return "全ての親";
                case "グルーブ": return "センター";
                case "上半身": case "下半身": return "グルーブ";
                case "腰": return "下半身";
                case "上半身2": return "上半身";
                case "首": return "上半身2";
                case "頭": return "首";
                case "両目": case "左目": case "右目": return "頭";
            }
            string side = name.StartsWith("左") ? "左" : name.StartsWith("右") ? "右" : null;
            if (side == null) return name == "下半身先" ? "下半身" : name == "上半身2先" ? "上半身2" : name == "頭先" ? "頭" : "全ての親";
            string part = name.Substring(1);
            switch (part)
            {
                case "肩": return "上半身2";
                case "腕": return side + "肩";
                case "腕捩": return side + "腕";
                case "ひじ": return side + "腕捩";
                case "手捩": return side + "ひじ";
                case "手首": return side + "手捩";
                case "手先": return side + "手首";
                case "足": return "腰";
                case "ひざ": return side + "足";
                case "足首": return side + "ひざ";
                case "つま先": return side + "足首";
                case "足ＩＫ": return side + "足IK親";
                case "つま先ＩＫ": case "足ＩＫ先": return side + "足ＩＫ";
                case "つま先ＩＫ先": return side + "つま先ＩＫ";
            }
            string normalized = part.Normalize(System.Text.NormalizationForm.FormKC);
            if (normalized.Contains("指") && char.IsDigit(normalized[normalized.Length - 1]))
            {
                int index = normalized[normalized.Length - 1] - '0';
                bool thumb = normalized.StartsWith("親");
                if (index == (thumb ? 0 : 1)) return side + "手首";
                return side + part.Substring(0, part.Length - 1) + "０１２３"[index - 1];
            }
            return side + "手首";
        }

        static int Depth(Transform t) { int count = 0; while (t.parent != null) { ++count; t = t.parent; } return count; }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(t.gameObject, "配置 VMD 影片");
            return t;
        }
    }
}
