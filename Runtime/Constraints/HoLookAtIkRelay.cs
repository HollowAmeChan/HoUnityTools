using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 把 <c>OnAnimatorIK</c> 转发给挂在**别的物体**上的注视约束。
    ///
    /// Unity 只把 OnAnimatorIK 发给"Animator 所在的那个 GameObject"（和 OnAnimatorMove 一样），
    /// 而约束常常挂在约束层 / 骨骼等子物体上，于是回调永远不来。组件会自动在 Animator 物体上
    /// 挂这个转发器（只在播放模式添加，避免编辑模式把场景标脏），禁用时自动清掉 owner。
    /// 不要手动挂它。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HoLookAtIkRelay : MonoBehaviour
    {
        private HoLookAtConstraint owner;

        internal HoLookAtConstraint Owner
        {
            get => owner;
            set => owner = value;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (owner != null && owner.isActiveAndEnabled)
            {
                owner.HandleAnimatorIK(layerIndex);
            }
        }
    }
}
