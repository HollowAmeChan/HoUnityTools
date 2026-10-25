using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Hollow.HoUnityTools.Armatures
{
    public enum HoCombineArmatureMode
    {
        Merge,
        Constraints,
    }

    /// <summary>
    /// Authoring settings only. The editor processor applies these settings to a play/build/bake copy.
    /// Put this component on the outfit armature, with meshes and switching objects outside that subtree.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Ho CombineArmature")]
    [MovedFrom(true, "Hollow.HoUnityTools.Armatures", "HoUnityTools.Armatures.Runtime", "CombineArmature")]
    public sealed class HoCombineArmature : MonoBehaviour
    {
        [Tooltip("本体中对应的骨架根。组件自身所在的 Transform 是衣服骨架根。")]
        public Transform targetArmature;

        [Tooltip("完整角色的共同父对象，须包含本体骨架、衣服骨架和衣服网格；不是两套骨架之一。留空时自动寻找。")]
        public Transform characterRoot;

        public HoCombineArmatureMode mode = HoCombineArmatureMode.Merge;

        [Tooltip("匹配衣服骨骼时移除的名称前缀。骨架根本身不按名称匹配。")]
        public string bonePrefix = string.Empty;

        [Tooltip("匹配衣服骨骼时移除的名称后缀。")]
        public string boneSuffix = string.Empty;

        [Tooltip("进入 Play 和构建场景时自动处理副本。Prefab/AssetBundle 导出请调用处理 API 或先烘焙副本。")]
        public bool processAutomatically = true;
    }
}
