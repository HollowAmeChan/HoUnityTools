using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    // Skipping this camera's skybox draw must not change environment lighting or reflections.
    internal sealed class HoQuickCaptureSkyboxOff : IDisposable
    {
        private readonly Camera camera;
        private readonly CameraClearFlags previousFlags;
        private readonly Color previousBackground;
        private bool disposed;
        private HoQuickCaptureSkyboxOff(Camera target)
        {
            camera = target;
            previousFlags = target.clearFlags;
            previousBackground = target.backgroundColor;
            target.clearFlags = CameraClearFlags.SolidColor;
            target.backgroundColor = Color.clear;
        }
        public static HoQuickCaptureSkyboxOff Apply(Camera camera) => new HoQuickCaptureSkyboxOff(camera);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (camera == null) return;
            camera.clearFlags = previousFlags;
            camera.backgroundColor = previousBackground;
        }
    }
}
