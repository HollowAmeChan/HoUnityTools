#if HO_VRCSDK
using VRC.SDKBase;
#endif

namespace Hollow.HoUnityTools
{
    /// <summary>
    /// 和 NDMF 的 <c>INDMFEditorOnly</c> 同一个套路：装了 VRChat SDK 时它就是 <c>VRC.SDKBase.IEditorOnly</c>，
    /// 否则是个空接口 —— 这样组件只需要写一个接口名，不用到处 <c>#if</c>，HoTools 也仍然能在纯 Unity 工程里编译。
    ///
    /// 为什么需要它：
    /// VRChat 的 avatar 校验用的是一份**组件类型白名单**，不在名单上的类型一律标红并中止构建 ——
    /// 跟它重不重要无关。唯一的例外是「EditorOnly」：
    ///
    /// * **面板不标红**：校验走的是 <c>FindIllegalComponents(..., excludeEditorOnly: true)</c>，
    ///   实现 <c>IEditorOnly</c> 的组件会被跳过（MA 的标签组件就是这么做的，所以它们从不被标红）。
    /// * **构建时自动消失**：VRCSDK 的 <c>RemoveAvatarEditorOnly</c> 回调（order -1024）会把
    ///   EditorOnly 的物体和 <c>IEditorOnly</c> 组件从 avatar 上删掉 —— 不需要任何构建钩子。
    ///
    /// 于是组件可以「在编辑器里一直留着、上传时自动不进包」，两边都干净。
    ///
    /// ⚠️ 只对**不该进构建**的编辑器辅助组件用，别给运行时组件加。
    /// </summary>
    public interface IHoEditorOnly
#if HO_VRCSDK
        : IEditorOnly
#endif
    {
    }
}
