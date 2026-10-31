using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>Explicit, transaction-backed access to the same HoFBX constraint import used by the editor window.</summary>
    public static class HoFbxConstraintImportUtility
    {
        [Serializable] sealed class Manifest { public Entry[] files; }
        [Serializable] sealed class Entry { public string kind, file; }

        public static int ApplyHoAux(TextAsset config, GameObject skeleton, Transform host)
        {
            if (config == null || skeleton == null) throw new ArgumentNullException("请选择约束 IR 与主骨架。");
            if (Application.isPlaying || EditorUtility.IsPersistent(skeleton) || !skeleton.scene.IsValid())
                throw new InvalidOperationException("在编辑模式的场景实例或 Prefab 内容中应用辅助约束。");
            return HoFbxImportProcessingWindow.ApplyHoAuxFromAutomation(config, skeleton, host);
        }

        public static int EnsurePrimaryAvatarConfigured(Animator animator)
        {
            if (animator == null || !animator.isHuman || animator.avatar == null) return 0;
            string path = AssetDatabase.GetAssetPath(animator.avatar);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return 0;
            string directory = Path.GetDirectoryName(path).Replace('\\', '/') + "/HoFBX";
            string manifestPath = directory + "/" + Path.GetFileNameWithoutExtension(path) + "_unity.json";
            if (!File.Exists(manifestPath)) return 0;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest?.files == null) return 0;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var entry in manifest.files.Where(x => x.kind == "rigConstraintIR"))
            {
                string configPath = directory + "/" + entry.file;
                if (!File.Exists(configPath)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(configPath);
                var config = JsonUtility.FromJson<ConstraintConfig>(asset.text);
                Transform skeleton = hips;
                while (skeleton != null && skeleton != animator.transform && skeleton.name != config.armatureName) skeleton = skeleton.parent;
                if (skeleton == null || skeleton.name != config.armatureName) continue;
                // Existing disabled rigs are an intentional author choice.
                var existing = animator.GetComponentsInChildren<HoAuxRig>(true)
                    .FirstOrDefault(x => x.SourceArmature == config.armatureName && x.OperationCount > 0);
                if (existing != null) return existing.OperationCount;
                Transform parent = animator.transform.Find("_Constraint");
                if (parent == null) { parent = new GameObject("_Constraint").transform; parent.SetParent(animator.transform, false); }
                Transform host = new GameObject("HoAux · 主骨架").transform; host.SetParent(parent, false);
                return ApplyHoAux(asset, skeleton.gameObject, host);
            }
            return 0;
        }
    }
}
