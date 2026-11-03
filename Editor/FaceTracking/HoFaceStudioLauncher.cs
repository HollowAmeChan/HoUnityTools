#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    internal static class HoFaceStudioLauncher
    {
        public static void Open()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string launcher = Path.Combine(projectRoot, "Tools~", "HoFaceStudio", "Start-HoFaceStudio.cmd");
            if (!File.Exists(launcher)) throw new FileNotFoundException("找不到 HoFaceStudio 启动脚本。", launcher);
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = Path.GetDirectoryName(launcher),
                UseShellExecute = true
            });
        }
    }
}
#endif
