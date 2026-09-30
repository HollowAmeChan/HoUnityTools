using System;
using System.Reflection;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    internal sealed class HoQuickCaptureAlphaOutputScope : IDisposable
    {
        private const string AlphaOutputField = "m_AllowPostProcessAlphaOutput";
        private readonly RenderPipelineAsset asset;
        private readonly FieldInfo field;
        private readonly bool previousValue;
        private bool disposed;
        private bool changed;
        private HoQuickCaptureAlphaOutputScope()
        {
            asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return;
            field = FindField(asset);
            if (field == null)
                throw new NotSupportedException("当前管线未提供后处理 alpha 输出开关，无法保证透明截帧。");
            previousValue = (bool)field.GetValue(asset);
            if (!previousValue)
            {
                // SerializedObject writes can dirty assets and trigger OnValidate/rebuilds.
                // Change only the backing field for the duration of the request.
                field.SetValue(asset, true);
                changed = true;
            }
        }
        public static HoQuickCaptureAlphaOutputScope Apply() => new HoQuickCaptureAlphaOutputScope();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (changed && asset != null) field.SetValue(asset, previousValue);
        }
        private static FieldInfo FindField(RenderPipelineAsset asset)
        {
            for (Type type = asset.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(AlphaOutputField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool)) return field;
            }
            return null;
        }
    }
}
