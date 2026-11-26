using System;
using LWGUI;
using UnityEditor;
using UnityEngine;
using LwguiGradient = LWGUI.Runtime.LwguiGradient.LwguiGradient;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.ShadowResponse.LwguiIntegration
{
    // Optional adapter: the authoring asset and its baker stay independent of LWGUI.
    internal static class HoShadowResponseLwguiProvider
    {
        [InitializeOnLoadMethod]
        private static void Register() => ShadowRampStorage.Register(Resolve);

        private static IShadowRampSource Resolve(Texture2D texture)
        {
            if (texture == null) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return null;
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                if (candidate is HoShadowResponseAsset profile && AssetDatabase.IsMainAsset(profile)
                    && profile.LookupTexture == texture)
                    return new Source(profile, texture);
            return null;
        }

        private sealed class Source : IShadowRampSource
        {
            private static readonly string[] Channels = { "red", "green", "blue", "alpha" };
            private readonly HoShadowResponseAsset profile;
            private readonly Texture2D texture;

            public Source(HoShadowResponseAsset profile, Texture2D texture)
            {
                this.profile = profile;
                this.texture = texture;
            }

            public Object Owner => profile;
            public Texture2D Texture => texture;
            public bool Editable => profile != null && texture != null && profile.LookupTexture == texture
                && AssetDatabase.IsMainAsset(profile) && AssetDatabase.IsOpenForEdit(profile)
                && AssetDatabase.GetAssetPath(profile) == AssetDatabase.GetAssetPath(texture);

            public LwguiGradient ReadGradient()
            {
                if (profile == null) throw new InvalidOperationException("The shadow response authoring asset no longer exists.");
                var result = new LwguiGradient();
                using (var serialized = new SerializedObject(profile))
                {
                    serialized.UpdateIfRequiredOrScript();
                    for (int channel = 0; channel < Channels.Length; ++channel)
                    {
                        // rawCurves accepts an empty authored curve; the convenience
                        // SetRgbaCurves API would replace it with an all-white curve.
                        result.rawCurves[channel] = CopyCurve(serialized.FindProperty(Channels[channel]).animationCurveValue, channel);
                    }
                }
                return result;
            }

            public void WriteGradient(LwguiGradient gradient)
            {
                if (gradient == null) throw new ArgumentNullException(nameof(gradient));
                RequireEditable();
                var curves = gradient.rawCurves;
                profile.SetCurves(CopyCurve(curves[0], 0), CopyCurve(curves[1], 1),
                    CopyCurve(curves[2], 2), CopyCurve(curves[3], 3));
                // The framework already records owner + texture in its Undo group.
                // Reuse the existing baker and texture instead of creating a new LUT.
                HoShadowResponseBaker.Bake(profile, false);
            }

            public void Save()
            {
                RequireEditable();
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            private void RequireEditable()
            {
                if (!Editable) throw new InvalidOperationException("The shadow response asset is no longer writable or no longer owns this LUT.");
            }

            private static AnimationCurve CopyCurve(AnimationCurve source, int channel)
            {
                if (source == null) return channel == 3
                    ? AnimationCurve.Linear(0, 0, 1, 1) : AnimationCurve.Linear(0, .5f, 1, 1);
                return new AnimationCurve(source.keys)
                {
                    preWrapMode = source.preWrapMode,
                    postWrapMode = source.postWrapMode
                };
            }
        }
    }
}
