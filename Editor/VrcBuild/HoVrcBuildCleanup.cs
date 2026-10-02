#if HO_NDMF
using System.Collections.Generic;
using Hollow.HoUnityTools.BoneRendering;
using Hollow.HoUnityTools.RigConstraints.Import;
using nadena.dev.ndmf;
using nadena.dev.ndmf.fluent;
using UnityEngine;
using Object = UnityEngine.Object;

// ─────────────────────────────────────────────────────────────────────────────
// 上传 VRChat 前，把 HoTools 的作者工具组件从 avatar 构建副本上摘掉。
//
// 为什么需要：
//   VRChat 的 avatar 校验是「组件类型白名单制」——
//   com.vrchat.base/…/Validation/AvatarValidation.cs 的 ComponentTypeWhiteListCommon /
//   ComponentTypeWhiteListSdk3 里没有的类型，一律报错并中止构建，跟它重不重要无关。
//   MA / VRCFury 之所以挂着也能上传，是因为它们在预处理阶段就把自己 DestroyImmediate 掉了；
//   而 HoTools 的组件是作者工具，没有任何钩子会删它们。
//
// 为什么不是直接删掉：
//   NDMF 构建时会把 avatar **克隆**一份再处理。这里删的只是构建副本，
//   场景/预制件里的 HoTools 组件一个不少 —— 骨骼渲染、约束面板、gizmo 照常用。
//
// 加新组件：
//   以后再有「只给编辑器用、不该上传」的组件，在 CollectAuthoringComponents 里加一行就行。
// ─────────────────────────────────────────────────────────────────────────────

[assembly: ExportsPlugin(typeof(Hollow.HoUnityTools.Editor.VrcBuild.HoVrcBuildCleanupPlugin))]

namespace Hollow.HoUnityTools.Editor.VrcBuild
{
    /// <summary>
    /// 在 VRChat 构建过程中移除 HoTools 的作者工具组件，让 avatar 通过 VRCSDK 的白名单校验。
    /// </summary>
    internal sealed class HoVrcBuildCleanupPlugin : Plugin<HoVrcBuildCleanupPlugin>
    {
        public override string QualifiedName => "hollow.hounitytools.vrc-build-cleanup";

        public override string DisplayName => "Ho Tools VRC 构建清理";

        protected override void Configure()
        {
            // Transforming：在 Resolving → Transforming → Optimizing → PlatformFinish 之中，
            // 也就是所有 NDMF 插件跑完之后、VRCSDK 的 AvatarValidation 之前，正好赶得上。
            InPhase(BuildPhase.Transforming)
                .Run("移除 HoTools 作者工具组件", RemoveAuthoringComponents);
        }

        private static void RemoveAuthoringComponents(BuildContext context)
        {
            var root = context.AvatarRootObject;
            if (root == null) return;

            // 先收集再删：DestroyImmediate 会改动 hierarchy，不能边遍历边删。
            var doomed = new List<Component>();
            CollectAuthoringComponents(root, doomed);

            if (doomed.Count == 0) return;

            foreach (var component in doomed)
            {
                // 只删组件，不删 GameObject ——
                // HoImportedConstraintMarker.hostRole == VrcConstraintObject(2) 时，
                // 它所在的物体就是承载 VRC 约束的空物体，
                // 连物体一起删等于把用户配好的 VRC 约束也删了。
                if (component != null) Object.DestroyImmediate(component);
            }

            Debug.Log($"[HoUnityTools] VRC 构建：已从构建副本移除 {doomed.Count} 个作者工具组件。");
        }

        /// <summary>
        /// 收集所有不该进 VRChat 构建的 HoTools 作者工具组件。
        /// </summary>
        private static void CollectAuthoringComponents(GameObject root, List<Component> doomed)
        {
            // 骨骼渲染 gizmo（Scene 视图用，运行时本来就不做任何事）
            doomed.AddRange(root.GetComponentsInChildren<HoBoneRenderer>(true));

            // 约束导入标记（记录某根骨骼上哪些约束是导入器生成的）
            doomed.AddRange(root.GetComponentsInChildren<HoImportedConstraintMarker>(true));
        }
    }
}
#endif
