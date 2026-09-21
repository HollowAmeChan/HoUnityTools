using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 键名下拉。**只列事实，不做猜测**：
    /// 1. 网格上真实存在的键（按名字排序，标出它出现在几个网格上、有没有被别的目标用着）；
    /// 2. 内置键名表按语义分组，作为"标准命名"的参考，网格上没有的会标出来。
    /// 选中后把键名写进 SerializedProperty；文本框永远可以直接改。
    /// </summary>
    internal sealed class HoKeyNameDropdown : AdvancedDropdown
    {
        private sealed class KeyItem : AdvancedDropdownItem
        {
            public KeyItem(string displayName, string keyName)
                : base(displayName)
            {
                KeyName = keyName;
            }

            public string KeyName { get; }
        }

        private readonly IHoShapeKeyMeshProvider meshes;
        private readonly SerializedProperty property;
        private readonly HashSet<string> usedElsewhere;

        private HoKeyNameDropdown(IHoShapeKeyMeshProvider meshes, SerializedProperty property, HashSet<string> usedElsewhere, AdvancedDropdownState state)
            : base(state)
        {
            this.meshes = meshes;
            this.property = property;
            this.usedElsewhere = usedElsewhere;
            minimumSize = new Vector2(300.0f, 460.0f);
        }

        /// <param name="usedElsewhere">别处已经用掉的键（归一化后的名字），用来标「已在用」。</param>
        public static void Show(Rect rect, IHoShapeKeyMeshProvider meshes, SerializedProperty property, HashSet<string> usedElsewhere = null)
        {
            HoKeyNameDropdown dropdown = new HoKeyNameDropdown(meshes, property, usedElsewhere, new AdvancedDropdownState());
            dropdown.Show(rect);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            AdvancedDropdownItem root = new AdvancedDropdownItem("形态键");

            // ① 网格上真实存在的键（去重 + 计数），这是用户真正要选的东西
            List<string> names = new List<string>();
            Dictionary<string, int> meshCounts = new Dictionary<string, int>();
            for (int i = 0; i < meshes.MeshCount; i++)
            {
                SkinnedMeshRenderer renderer = meshes.GetMesh(i);
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                HashSet<string> onThisMesh = new HashSet<string>();
                for (int k = 0; k < mesh.blendShapeCount; k++)
                {
                    string name = mesh.GetBlendShapeName(k);
                    if (string.IsNullOrEmpty(name) || !onThisMesh.Add(name))
                    {
                        continue;
                    }

                    if (!meshCounts.ContainsKey(name))
                    {
                        meshCounts[name] = 0;
                        names.Add(name);
                    }

                    meshCounts[name]++;
                }
            }

            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (names.Count > 0)
            {
                AdvancedDropdownItem group = new AdvancedDropdownItem("网格上的键（" + names.Count + "）");
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    string suffix = meshCounts[name] > 1 ? "   · " + meshCounts[name] + " 个网格" : string.Empty;
                    if (usedElsewhere != null && usedElsewhere.Contains(HoShapeKeyResolver.Normalize(name)))
                    {
                        suffix += "   · 已在用";
                    }

                    group.AddChild(new KeyItem(name + suffix, name));
                }

                root.AddChild(group);
            }

            AddSemanticGroup(root, "眼睑 · 闭合", HoBlinkKeySemantic.EyelidClosed);
            AddSemanticGroup(root, "眼睑 · 眯眼", HoBlinkKeySemantic.EyelidSquint);
            AddSemanticGroup(root, "眼睑 · 睁大", HoBlinkKeySemantic.EyelidWide);
            AddSemanticGroup(root, "凝视 · 上", HoBlinkKeySemantic.GazeUp);
            AddSemanticGroup(root, "凝视 · 下", HoBlinkKeySemantic.GazeDown);
            AddSemanticGroup(root, "凝视 · 左", HoBlinkKeySemantic.GazeLeft);
            AddSemanticGroup(root, "凝视 · 右", HoBlinkKeySemantic.GazeRight);
            AddSemanticGroup(root, "凝视 · 内（ARkit/PICO 族）", HoBlinkKeySemantic.GazeIn);
            AddSemanticGroup(root, "凝视 · 外（ARkit/PICO 族）", HoBlinkKeySemantic.GazeOut);

            if (root.children == null || !root.children.GetEnumerator().MoveNext())
            {
                root.AddChild(new KeyItem("（先在「目标网格」里放网格）", string.Empty));
            }

            return root;
        }

        private void AddSemanticGroup(AdvancedDropdownItem root, string title, HoBlinkKeySemantic semantic)
        {
            List<HoBlinkKeyEntry> entries = HoBlinkKeyTable.BySemantic(semantic);
            if (entries.Count == 0)
            {
                return;
            }

            AdvancedDropdownItem group = new AdvancedDropdownItem(title);
            for (int i = 0; i < entries.Count; i++)
            {
                HoBlinkKeyEntry entry = entries[i];
                bool exists = meshes.KeyExists(entry.Name);
                string display = entry.Display + (exists ? string.Empty : "   · 网格上没有");
                group.AddChild(new KeyItem(display, entry.Name));
            }

            root.AddChild(group);
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is KeyItem keyItem && !string.IsNullOrEmpty(keyItem.KeyName) && property != null)
            {
                property.stringValue = keyItem.KeyName;
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
