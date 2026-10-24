using UnityEngine;

namespace Hollow.HoUnityTools.Armatures
{
    public enum CombineArmatureMode
    {
        Merge,
        Constraints,
    }

    /// <summary>
    /// Authoring settings only. The editor processor applies these settings to a play/build/bake copy.
    /// Put this component on the outfit armature, with meshes and switching objects outside that subtree.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/CombineArmature")]
    public sealed class CombineArmature : MonoBehaviour
    {
        [Tooltip("本体中对应的骨架根。组件自身所在的 Transform 是衣服骨架根。")]
        public Transform targetArmature;

        [Tooltip("包含本体、衣服和所有相关引用的角色根。留空时使用共同祖先及其最近的 Animator。")]
        public Transform characterRoot;

        public CombineArmatureMode mode = CombineArmatureMode.Merge;

        [Tooltip("匹配衣服骨骼时移除的名称前缀。骨架根本身不按名称匹配。")]
        public string bonePrefix = string.Empty;

        [Tooltip("匹配衣服骨骼时移除的名称后缀。")]
        public string boneSuffix = string.Empty;

        [Tooltip("进入 Play 和构建场景时自动处理副本。Prefab/AssetBundle 导出请调用处理 API 或先烘焙副本。")]
        public bool processAutomatically = true;
    }
}
