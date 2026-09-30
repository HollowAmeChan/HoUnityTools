// HoQuickCaptureSkyboxOff.cs -- 拍透明图时**把天空盒整个关掉**
//
// 为什么"透明背景"不够：相机 `ClearFlags` + 背景 alpha=0 只解决了"清屏那一下"，
// 而**天空盒是画在背景之上的一层几何**（`Skybox` 组件 / `RenderSettings.skybox` 那个材质）。
// 于是不管背景多透明，天空那块永远被天空盒填满 —— 出来就是"背景透明了，天还是实的"。
//
// 用户要的语义很直接：「这个模式下天空盒直接认为是全部透明」。
// 做法就是把天空盒停掉，让那块区域落到相机的清屏色（透明黑）上：
//   ① `RenderSettings.skybox = null` —— 天空盒不再被画（这块变成透明）；
//   ② 相机 `ClearFlags = SolidColor` + 背景 `alpha = 0` —— 那块填成透明；
//   ③ 反射也一并掐掉 —— 否则天空盒还会从**反射**里透出来（金属/光滑材质上的亮边）。
//
// ⚠️ 借了三样东西，**全部在 finally 里原样还回去**（这个仓库的硬规矩）：
//   `RenderSettings.skybox` / `ambientMode` / `ambientLight` / `defaultReflectionMode` / `reflectionIntensity`。
//   它们是**场景级**设置，不还原会把整个场景的光照环境改掉 —— 比改一台相机严重得多。
//
// ⚠️ 已知取舍（写清楚，不假装没这回事）：
//   天空盒不只是"画个背景"，它同时是**环境光的来源**。把它设成 null 之后，
//   `ambientMode = Skybox` 的工程会临时退化成 `Flat` + 一个中灰环境光，
//   所以这一张的照明**和平时不完全一样**（差别在于间接光）。
//   想要"照明也不受影响"，正路是把天空盒材质换成纯透明，而不是设 null ——
//   那需要在工程里建一个材质资源，不是这个面板该擅自做的事。
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>临时把天空盒与反射停掉，离开时还原。`using` 用法。</summary>
    internal sealed class HoQuickCaptureSkyboxOff : IDisposable
    {
        private bool captured;
        private bool disposed;

        private Material previousSkybox;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private DefaultReflectionMode previousReflectionMode;
        private float previousReflectionIntensity;
        private bool changesAnything;

        private HoQuickCaptureSkyboxOff()
        {
        }

        /// <summary>
        /// 把天空盒停掉。返回 null 表示**本来就没有天空盒**，不需要做任何事
        ///（那种情况下也不该报错，因为结果已经是对的）。
        /// </summary>
        public static HoQuickCaptureSkyboxOff Apply()
        {
            var scope = new HoQuickCaptureSkyboxOff();
            scope.Capture();
            scope.Suppress();
            return scope;
        }

        /// <summary>是不是真的改动了什么（用来决定要不要在面板上说明）。</summary>
        public bool ChangedAnything => changesAnything;

        private void Capture()
        {
            if (captured)
            {
                return;
            }

            captured = true;
            previousSkybox = RenderSettings.skybox;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousReflectionMode = RenderSettings.defaultReflectionMode;
            previousReflectionIntensity = RenderSettings.reflectionIntensity;
        }

        private void Suppress()
        {
            // 没有天空盒就什么都不用做 —— 那块本来就已经是透明。
            if (previousSkybox == null)
            {
                return;
            }

            changesAnything = true;

            // ① 天空盒不再被画。
            RenderSettings.skybox = null;

            // ② 环境光：`Skybox` 模式失去来源会退化，显式给个中灰，免得场景整体变全黑。
            if (previousAmbientMode == AmbientMode.Skybox)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f, 1f);
            }

            // ③ 反射：不清掉的话天空盒会从反射里透出来（金属面上的那圈亮边）。
            //    用 Custom + 不指定 cubemap = 没有反射探针，于是反射给 0。
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;
        }

        public void Dispose()
        {
            if (disposed || !captured)
            {
                return;
            }

            disposed = true;

            if (!changesAnything)
            {
                return;
            }

            RenderSettings.skybox = previousSkybox;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            RenderSettings.defaultReflectionMode = previousReflectionMode;
            RenderSettings.reflectionIntensity = previousReflectionIntensity;
        }
    }
}
