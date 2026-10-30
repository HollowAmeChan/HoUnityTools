#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using Hollow.HoUnityTools.MmdMotion;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [ScriptedImporter(1, "vmd")]
    public sealed class HoVmdScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            HoVmdAnimation animation=HoVmdAnimation.Parse(File.ReadAllBytes(context.assetPath));
            animation.name=Path.GetFileNameWithoutExtension(context.assetPath);
            context.AddObjectToAsset(animation.name,animation);
            context.SetMainObject(animation);
        }
    }
}
#endif
