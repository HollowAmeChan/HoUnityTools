using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 键名下拉：内置键名表（按语义分组，显示 `键名 (规范)`）+ 当前网格上实际存在但表里没有的键。
    /// 选中后把键名写进目标 SerializedProperty；文本框永远可以直接改。
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

        private readonly HoBlinkConstraint constraint;
        private readonly SerializedProperty property;

        private HoKeyNameDropdown(HoBlinkConstraint constraint, SerializedProperty property, AdvancedDropdownState state)
            : base(state)
        {
            this.constraint = constraint;
            this.property = property;
            minimumSize = new Vector2(280.0f, 420.0f);
        }

        public static void Show(Rect rect, HoBlinkConstraint constraint, SerializedProperty property)
        {
            HoKeyNameDropdown dropdown = new HoKeyNameDropdown(constraint, property, new AdvancedDropdownState());
            dropdown.Show(rect);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            AdvancedDropdownItem root = new AdvancedDropdownItem("形态键");

            AddSemanticGroup(root, "眼睑 · 闭合", HoBlinkKeySemantic.EyelidClosed);
            AddSemanticGroup(root, "眼睑 · 眯眼", HoBlinkKeySemantic.EyelidSquint);
            AddSemanticGroup(root, "眼睑 · 睁大", HoBlinkKeySemantic.EyelidWide);
            AddSemanticGroup(root, "凝视 · 上", HoBlinkKeySemantic.GazeUp);
            AddSemanticGroup(root, "凝视 · 下", HoBlinkKeySemantic.GazeDown);
            AddSemanticGroup(root, "凝视 · 左", HoBlinkKeySemantic.GazeLeft);
            AddSemanticGroup(root, "凝视 · 右", HoBlinkKeySemantic.GazeRight);
            AddSemanticGroup(root, "凝视 · 内（ARkit/PICO 族）", HoBlinkKeySemantic.GazeIn);
            AddSemanticGroup(root, "凝视 · 外（ARkit/PICO 族）", HoBlinkKeySemantic.GazeOut);

            List<HoBlinkKeyEntry> entries = new List<HoBlinkKeyEntry>(HoBlinkKeyTable.Entries);
            List<string> meshOnly = new List<string>();
            HashSet<string> known = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                known.Add(HoShapeKeyResolver.Normalize(entries[i].Name));
            }

            for (int i = 0; i < constraint.MeshCount; i++)
            {
                SkinnedMeshRenderer mesh = constraint.GetMesh(i);
                Mesh sharedMesh = mesh != null ? mesh.sharedMesh : null;
                if (sharedMesh == null)
                {
                    continue;
                }

                for (int k = 0; k < sharedMesh.blendShapeCount; k++)
                {
                    string name = sharedMesh.GetBlendShapeName(k);
                    if (known.Contains(HoShapeKeyResolver.Normalize(name)) || meshOnly.Contains(name))
                    {
                        continue;
                    }

                    meshOnly.Add(name);
                }
            }

            if (meshOnly.Count > 0)
            {
                AdvancedDropdownItem group = new AdvancedDropdownItem("网格上的其它键（自定义）");
                meshOnly.Sort();
                for (int i = 0; i < meshOnly.Count; i++)
                {
                    group.AddChild(new KeyItem(meshOnly[i], meshOnly[i]));
                }

                root.AddChild(group);
            }

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
                bool exists = constraint.KeyExists(entry.Name);
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
