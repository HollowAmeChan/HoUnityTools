using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 弹簧驱动的预设动作。
    ///
    /// **预设只改当前这一个组件的参数**：跟随 / 回弹 / 增益 / 输入键（必要时先补网格），
    /// 以及目标为空时补两行"待填"的挤压 / 回弹。
    ///
    /// **它不加组件、也不动场景里别的组件。** 一个组件就是一弹簧，果冻眼要两个自由度 ——
    /// 那是用户自己再挂一根的事（跟其它约束一样：要几个就挂几个），
    /// 预设替用户做这个决定只会让人看不懂"我只是点了下预设，怎么多出两个组件"。
    /// </summary>
    internal static class HoSpringPresetActions
    {
        /// <summary>把这根配成"果冻眼"的横向（6Hz）或纵向（8.5Hz）。</summary>
        public static void ApplyJelly(HoSpringConstraint component, bool vertical)
        {
            if (component == null)
            {
                return;
            }

            Undo.RecordObject(component, vertical ? "果冻眼预设（纵向）" : "果冻眼预设（横向）");
            CollectMeshes(component);
            component.ApplyJellyPreset(vertical);
            EditorUtility.SetDirty(component);
        }

        /// <summary>
        /// 网格列表空着就从 Animator 根往下收一遍 —— 否则预设匹配不到眨眼键，
        /// 点完还是"输入 未指定"，用户得自己再去找网格、再手动挑键。
        /// （注视/眨眼那两套预设也做同样的收集。）
        /// </summary>
        private static void CollectMeshes(HoSpringConstraint component)
        {
            if (component.Meshes.Count > 0)
            {
                return;
            }

            Animator animator = component.GetComponentInParent<Animator>();
            Transform root = animator != null ? animator.transform : component.transform;
            foreach (SkinnedMeshRenderer mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.sharedMesh != null && mesh.sharedMesh.blendShapeCount > 0)
                {
                    component.Meshes.Add(mesh);
                }
            }
        }
    }
}
