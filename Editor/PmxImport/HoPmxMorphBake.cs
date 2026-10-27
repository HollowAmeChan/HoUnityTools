using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    internal static class HoPmxMorphBake
    {
        internal static void Apply(HoPmxDocument doc, List<HoPmxMorphSetting> settings, HoPmxImportReport report)
        {
            var weights = new float[doc.Morphs.Count];
            foreach (var setting in settings)
            {
                if (setting.morphIndex < 0 || setting.morphIndex >= weights.Length || setting.weight == 0) continue;
                if (float.IsNaN(setting.weight) || float.IsInfinity(setting.weight))
                { report.warnings.Add("表情权重不是有限数，已跳过。"); continue; }
                AddWeight(doc, setting.morphIndex, setting.weight, weights, new HashSet<int>(), report);
            }
            for (int i = 0; i < weights.Length; ++i)
            {
                if (weights[i] == 0) continue;
                var morph = doc.Morphs[i];
                float weight = weights[i];
                if (morph.Type == 1)
                    foreach (var offset in morph.VertexOffsets)
                    {
                        var vertex = doc.Vertices[offset.VertexIndex];
                        vertex.Position = Add(vertex.Position, offset.Offset, weight);
                        doc.Vertices[offset.VertexIndex] = vertex;
                    }
                else if (morph.Type >= 3 && morph.Type <= 7)
                    foreach (var offset in morph.UvOffsets)
                    {
                        var vertex = doc.Vertices[offset.VertexIndex];
                        if (morph.Type == 3)
                            vertex.Uv = new HoPmxVector2(vertex.Uv.X + offset.Offset.X * weight, vertex.Uv.Y + offset.Offset.Y * weight);
                        else if (vertex.AdditionalUvs != null && morph.Type - 4 < vertex.AdditionalUvs.Length)
                            vertex.AdditionalUvs[morph.Type - 4] = Add(vertex.AdditionalUvs[morph.Type - 4], offset.Offset, weight);
                        doc.Vertices[offset.VertexIndex] = vertex;
                    }
                else if (morph.Type != 8 && morph.Type != 0)
                    report.warnings.Add("表情「" + morph.Name + "」的骨骼/Flip/物理行为未烘焙。");
                if (morph.Name != null && morph.Name.IndexOf("AL+", StringComparison.OrdinalIgnoreCase) >= 0)
                    report.warnings.Add("「" + morph.Name + "」只烘焙 PMX 材质数值；AutoLuminous 发光需在提取后的材质中设置。");
            }
            // PMX specifies base * allMultipliers + allAdditions, independent of list ordering.
            for (byte operation = 0; operation < 2; ++operation)
            for (int i = 0; i < weights.Length; ++i)
            {
                if (weights[i] == 0 || doc.Morphs[i].Type != 8) continue;
                foreach (var o in doc.Morphs[i].MaterialOffsets)
                {
                    if (o.Operation != operation) continue;
                    for (int m = 0; m < doc.Materials.Count; ++m)
                    {
                        if (o.MaterialIndex != -1 && o.MaterialIndex != m) continue;
                        HoPmxMaterial target = doc.Materials[m];
                        float w = weights[i];
                        target.Diffuse = Combine(target.Diffuse, o.Diffuse, w, operation);
                        target.Ambient = Combine(target.Ambient, o.Ambient, w, operation);
                        target.Specular = Combine(target.Specular, o.Specular, w, operation);
                        target.Shininess = Combine(target.Shininess, o.Shininess, w, operation);
                        target.EdgeColor = Combine(target.EdgeColor, o.EdgeColor, w, operation);
                        target.EdgeSize = Combine(target.EdgeSize, o.EdgeSize, w, operation);
                    }
                    if (!Neutral(o.TextureTint, operation) || !Neutral(o.ToonTint, operation) || !Neutral(o.SphereTint, operation))
                        report.warnings.Add("材质表情「" + doc.Morphs[i].Name + "」的贴图乘加系数未烘焙。");
                }
            }
        }

        private static void AddWeight(HoPmxDocument doc, int index, float value, float[] weights,
            HashSet<int> chain, HoPmxImportReport report)
        {
            if (index < 0 || index >= doc.Morphs.Count)
            { report.warnings.Add("Group 表情包含无效目标，已跳过。"); return; }
            if (chain.Count >= 64 || float.IsNaN(value) || float.IsInfinity(value))
            { report.warnings.Add("Group 表情层级过深或权重溢出，已跳过。"); return; }
            if (!chain.Add(index)) { report.warnings.Add("循环 Group 表情：" + doc.Morphs[index].Name); return; }
            var morph = doc.Morphs[index];
            if (morph.Type == 0)
                foreach (var entry in morph.GroupOffsets)
                    AddWeight(doc, entry.MorphIndex, value * entry.Weight, weights, chain, report);
            else weights[index] += value;
            chain.Remove(index);
        }

        private static bool Neutral(HoPmxVector4 v, byte operation)
        {
            float target = operation == 0 ? 1 : 0;
            return Mathf.Approximately(v.X, target) && Mathf.Approximately(v.Y, target) &&
                   Mathf.Approximately(v.Z, target) && Mathf.Approximately(v.W, target);
        }
        private static float Combine(float a, float b, float weight, byte operation)
        { return operation == 0 ? a * (1 + (b - 1) * weight) : a + b * weight; }
        private static HoPmxVector3 Combine(HoPmxVector3 a, HoPmxVector3 b, float w, byte o)
        { return new HoPmxVector3(Combine(a.X, b.X, w, o), Combine(a.Y, b.Y, w, o), Combine(a.Z, b.Z, w, o)); }
        private static HoPmxVector4 Combine(HoPmxVector4 a, HoPmxVector4 b, float w, byte o)
        { return new HoPmxVector4(Combine(a.X, b.X, w, o), Combine(a.Y, b.Y, w, o), Combine(a.Z, b.Z, w, o), Combine(a.W, b.W, w, o)); }
        private static HoPmxVector3 Add(HoPmxVector3 a, HoPmxVector3 b, float w)
        { return new HoPmxVector3(a.X + b.X * w, a.Y + b.Y * w, a.Z + b.Z * w); }
        private static HoPmxVector4 Add(HoPmxVector4 a, HoPmxVector4 b, float w)
        { return new HoPmxVector4(a.X + b.X * w, a.Y + b.Y * w, a.Z + b.Z * w, a.W + b.W * w); }
    }
}
