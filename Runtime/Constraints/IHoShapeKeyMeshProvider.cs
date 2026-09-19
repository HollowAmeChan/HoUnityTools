using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 提供"形态键写在哪些网格上"的宿主（眨眼约束与注视约束都实现它），
    /// 供键名下拉、缺失键提示等编辑器工具复用。
    /// </summary>
    public interface IHoShapeKeyMeshProvider
    {
        int MeshCount { get; }

        SkinnedMeshRenderer GetMesh(int index);

        bool KeyExists(string keyName);
    }
}
