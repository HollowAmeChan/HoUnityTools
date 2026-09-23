using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 会话与调试器之间的**唯一联系点**：会话把"正在生效的影子 Animator"登记在这里，
    /// 调试组件（混合树观察台）从这里取。
    ///
    /// **为什么要这么绕**：影子台是运行期建的隐藏对象（`HideAndDontSave`，Hierarchy 里点不到、
    /// 挂在场景根下），调试组件既找不到它、也没法让用户在 Inspector 里拖它。让**生产端主动登记**，
    /// 两边就都不用知道对方，管线也不必为了调试改结构。
    ///
    /// 同一时刻只保留最后登记的那一个（多角色同时调试是以后的事）。
    /// </summary>
    public static class HoFaceShadowLink
    {
        /// <summary>当前正在生效的影子 Animator；没有会话时为 <c>null</c>。</summary>
        public static Animator Active { get; private set; }

        public static void Register(Animator animator)
        {
            if (animator != null) Active = animator;
        }

        public static void Unregister(Animator animator)
        {
            if (Active == animator) Active = null;
        }
    }
}
