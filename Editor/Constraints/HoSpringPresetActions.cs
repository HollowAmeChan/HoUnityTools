using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 弹簧驱动的预设入口。
    ///
    /// 一个组件 = 一根弹簧，所以"果冻眼"这个预设的形态是**加两个组件**（横向 / 纵向），
    /// 而不是在某一个组件里塞两套参数。这样它跟"多挂一个组件"这条设计原则一致，
    /// 而且加完之后用户看到的是两个各自独立、可以单独调频率 / 单独关掉的组件。
    ///
    /// 预设只做两件事（沿用眨眼约束的约定）：**搭结构 + 给驱动键**。目标键留空 ——
    /// 那是用户自己的键，猜不得。
    /// </summary>
    internal static class HoSpringPresetActions
    {
        [MenuItem("GameObject/HoUnityTools/果冻眼（弹簧驱动 ×2）", false, 40)]
        private static void AddJellyEyeMenu(MenuCommand command)
        {
            var host = command != null ? command.context as GameObject : null;
            if (host == null)
            {
                return;
            }

            HoSpringConstraint existing = host.GetComponent<HoSpringConstraint>();
            var meshes = existing != null && existing.Meshes.Count > 0
                ? new List<SkinnedMeshRenderer>(existing.Meshes)
                : CollectMeshes(host);
            AddJellyEye(host, meshes);
        }

        [MenuItem("GameObject/HoUnityTools/果冻眼（弹簧驱动 ×2）", true)]
        private static bool AddJellyEyeMenuValidate(MenuCommand command) =>
            command != null && command.context is GameObject;

        /// <summary>面板按钮用：在这台 GameObject 上加两根弹簧（横向 6Hz、纵向 8.5Hz）。</summary>
        public static void AddJellyEye(HoSpringConstraint component)
        {
            if (component == null)
            {
                return;
            }

            AddJellyEye(component.gameObject, component.Meshes.Count > 0
                ? new List<SkinnedMeshRenderer>(component.Meshes)
                : CollectMeshes(component.gameObject));
        }

        /// <summary>同一套网格和输入键，再加一根（频率自己去调）。</summary>
        public static void Duplicate(HoSpringConstraint component)
        {
            if (component == null)
            {
                return;
            }

            GameObject host = component.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(host, "再加一根弹簧");
            var copy = Undo.AddComponent<HoSpringConstraint>(host);
            copy.Meshes.AddRange(component.Meshes.Count > 0 ? component.Meshes : CollectMeshes(host));
            copy.KeyNames.AddRange(component.KeyNames);
            copy.Targets.Add(new HoSpringTarget(string.Empty, 1.0f));
            EditorUtility.SetDirty(copy);
        }

        private static void AddJellyEye(GameObject host, List<SkinnedMeshRenderer> meshes)
        {
            Undo.RegisterFullObjectHierarchyUndo(host, "加果冻眼（弹簧驱动 ×2）");
            var horizontal = Undo.AddComponent<HoSpringConstraint>(host);
            var vertical = Undo.AddComponent<HoSpringConstraint>(host);
            horizontal.Meshes.AddRange(meshes);
            vertical.Meshes.AddRange(meshes);
            HoSpringPresets.ConfigureJelly(horizontal, false);
            HoSpringPresets.ConfigureJelly(vertical, true);
            Selection.activeGameObject = host;
            EditorUtility.SetDirty(horizontal);
            EditorUtility.SetDirty(vertical);
        }

        /// <summary>
        /// 网格列表空着就从 Animator 根往下收一遍 —— 否则预设加完还是"什么都没配"，
        /// 用户得自己再去找网格、再手动挑键。收一遍才叫"给一个好底子"。
        /// </summary>
        private static List<SkinnedMeshRenderer> CollectMeshes(GameObject host)
        {
            var result = new List<SkinnedMeshRenderer>();
            if (host == null)
            {
                return result;
            }

            Animator animator = host.GetComponentInParent<Animator>();
            Transform root = animator != null ? animator.transform : host.transform;
            foreach (SkinnedMeshRenderer mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.sharedMesh != null && mesh.sharedMesh.blendShapeCount > 0)
                {
                    result.Add(mesh);
                }
            }

            return result;
        }
    }
}
