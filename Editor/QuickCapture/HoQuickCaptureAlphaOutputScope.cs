// HoQuickCaptureAlphaOutputScope.cs -- 拍透明图时临时打开管线的「后处理保留 alpha」
//
// ⚠️⚠️ 这个文件**删过一次又加回来了**，原因记在最前面，别再删第三次：
//
// 用户说过「不需要做多余的隐式设置，除非是必要的」，我据此把它整个删掉、
// 改成"要透明就自己去 URP Asset 上勾"。**结果立刻坏了**：出图背景变成纯色，
// 而且**连抗锯齿也没了**。因为它是"必要的"那一类，不是"多余的"：
//
//   UniversalRenderPipeline.cs:1768
//     bool allowAlphaOutput = !cameraData.postProcessEnabled
//         || (cameraData.postProcessEnabled && settings.allowPostProcessAlphaOutput);
//     cameraData.isAlphaOutputEnabled = cameraData.isAlphaOutputEnabled && allowAlphaOutput;
//
// 只要后处理参与渲染，而 `Allow Post Process Alpha Output` 是关的（默认 false），
// URP 就把 `isAlphaOutputEnabled` 置 false ⇒ 最终 blit 不带 `_ENABLE_ALPHA_OUTPUT`
// ⇒ **alpha 被写成 1** ⇒ 透明没了（背景成了纯色）。
//
// "连抗锯齿也没了"是同一个开关的连带效果，不是第二件事：
//   · TAA 在 URP 里**属于后处理**（`UniversalCameraData.IsTemporalAAEnabled()` 要求
//     `postProcessEnabled` 为真）；
//   · 而"后处理把 alpha 写回 1"本身就证明**后处理确实跑了** —— 所以"背景变纯色"
//     和"后处理生效"是同一件事的两面。用户报"纯色 + 没 AA"时，别去查两处。
//
// 一句话：**关了它就没透明**，而且这个因果关系只能在这里讲清楚，用户猜不到。
//
// 为什么不是"让用户自己勾"？能勾当然也行，但那样默认路径就是坏的。
// 这里动的是**用户工程里的资产**（`Assets/Settings/PC_RPAsset.asset`），所以：
//   · 改成一个**没保存的内存值**：只在内存里改，**不写盘** —— 磁盘上的文件保持原样，
//     所以 git diff 是干净的、也不会留下一个没人记得为什么变成 1 的资产；
//   · 拍完**立刻改回去**（`finally`，和天空盒那套一个道理）；
//   · 如果用户**本来就把它打开了**，我们什么都不做，也不会有任何副作用。
//
// ⚠️ 另一个已知点：整个"临时打开"都有个前提 —— **渲染管线是 URP**。
// 内建管线（Built-in）没有这个开关，也不需要它（内建的后处理本来就不碰 alpha）。
// 所以找不到那个字段时静默跳过，不报错。
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>临时打开「后处理保留 alpha」，离开时改回原值。`using` 用法。</summary>
    internal sealed class HoQuickCaptureAlphaOutputScope : IDisposable
    {
        private const string AlphaOutputField = "m_AllowPostProcessAlphaOutput";

        private bool captured;
        private bool disposed;
        private bool changed;
        private bool previousValue;
        private SerializedObject serialized;
        private SerializedProperty property;

        private HoQuickCaptureAlphaOutputScope()
        {
        }

        /// <summary>
        /// 需要时才打开。返回的对象负责还原；不管有没有改都返回一个可用的实例（不会返回 null）。
        /// </summary>
        /// <param name="note">非空 = 有件事该告诉用户（改了 / 本来是开的 / 找不到开关）。</param>
        public static HoQuickCaptureAlphaOutputScope Apply(out string note)
        {
            note = null;
            var scope = new HoQuickCaptureAlphaOutputScope();
            scope.TryEnable(out note);
            return scope;
        }

        /// <summary>这次是不是真的动过那个开关。</summary>
        public bool ChangedPipelineSetting => changed;

        private void TryEnable(out string note)
        {
            note = null;

            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                // 内建管线：没这个开关，也不需要 —— 内建后处理本来就不碰 alpha。
                return;
            }

            try
            {
                serialized = new SerializedObject(asset);
                property = serialized.FindProperty(AlphaOutputField);
            }
            catch (Exception)
            {
                property = null;
            }

            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            {
                // 不是 URP，或者 URP 改了这个字段名 —— 不报错，只是"这一条帮不上忙"。
                // 用户仍然可以自己去 RP Asset 上勾。
                note = "没在渲染管线资产上找到「后处理保留 alpha」这个开关（可能不是 URP，或 URP 改了字段名）—— "
                    + "如果出图 alpha 全是 1，请自己去 URP Asset 上勾 `Allow Post Process Alpha Output`。";
                return;
            }

            captured = true;
            previousValue = property.boolValue;

            if (previousValue)
            {
                // 本来就是开的：什么都不用做，也就没有任何副作用。
                // 本来就是开的：什么都不用做，也没什么好说的（用户要求少提示）。
                return;
            }

            try
            {
                property.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changed = true;

                // 这是**内存里**的改动，没写盘 —— 渲染会用到新值，磁盘上的资产保持原样。
                // 正常情况不吭声（用户要求少提示）；只有失败了才说话。
            }
            catch (Exception exception)
            {
                changed = false;
                note = "临时打开「后处理保留 alpha」失败了：" + exception.Message;
            }
        }

        public void Dispose()
        {
            if (disposed || !captured || !changed)
            {
                return;
            }

            disposed = true;

            try
            {
                // 重新取一遍：期间可能发生过重导入，旧句柄会失效。
                RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
                if (asset == null)
                {
                    return;
                }

                var current = new SerializedObject(asset);
                SerializedProperty target = current.FindProperty(AlphaOutputField);
                if (target == null)
                {
                    return;
                }

                target.boolValue = previousValue;
                current.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] 还原「后处理保留 alpha」失败，请手动确认 URP Asset 上那个开关："
                    + exception.Message);
            }
        }

        /// <summary>
        /// 只看一眼当前状态，**不改任何东西**。给「诊断」按钮用：
        /// 用于回答"那个隐式覆写到底有没有生效" —— 也就是"背景是纯色 / 没有后处理"时
        /// 最先要排除的那一条。
        /// </summary>
        /// <param name="assetName">当前管线资产的名字（没在用 SRP 时为空）。</param>
        /// <param name="fieldFound">有没有在资产上找到那个序列化字段。</param>
        /// <param name="value">字段当前的值（没找到时为 false）。</param>
        public static void Inspect(out string assetName, out bool fieldFound, out bool value)
        {
            assetName = null;
            fieldFound = false;
            value = false;

            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                return;
            }

            assetName = asset.name;

            try
            {
                var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.FindProperty(AlphaOutputField);
                if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                {
                    return;
                }

                fieldFound = true;
                value = property.boolValue;
            }
            catch (Exception)
            {
                fieldFound = false;
            }
        }
    }
}
