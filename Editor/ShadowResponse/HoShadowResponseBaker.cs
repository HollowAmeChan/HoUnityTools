using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.ShadowResponse
{
    [InitializeOnLoad]
    public static class HoShadowResponseBaker
    {
        static readonly ConcurrentQueue<HoShadowResponseAsset> Pending = new();
        static bool baking;
        static HoShadowResponseBaker()
        {
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += () =>
            {
                foreach (var profile in Resources.FindObjectsOfTypeAll<HoShadowResponseAsset>()) Queue(profile);
            };
        }
        // OnValidate may run during loading. Queue only managed references here.
        public static void Queue(HoShadowResponseAsset profile) { if (!ReferenceEquals(profile, null)) Pending.Enqueue(profile); }
        static void Update()
        {
            if (baking || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var unique = new System.Collections.Generic.HashSet<HoShadowResponseAsset>();
            while (Pending.TryDequeue(out var profile)) if (profile != null) unique.Add(profile);
            foreach (var profile in unique)
            {
                try { if (AssetDatabase.Contains(profile)) Bake(profile, false); }
                catch (Exception e) { Debug.LogException(e, profile); }
            }
        }
        public static void Bake(HoShadowResponseAsset profile, bool recordUndo = true)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsMainAsset(profile) || !AssetDatabase.IsOpenForEdit(profile))
                throw new InvalidOperationException("Save the editable curve asset before baking its LUT.");
            var pixels = new Color[HoShadowResponseAsset.Resolution];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = profile.Evaluate(i / (float)(pixels.Length - 1));
                foreach (float v in new[] { pixels[i].r, pixels[i].g, pixels[i].b, pixels[i].a })
                    if (float.IsNaN(v) || float.IsInfinity(v) || Mathf.Abs(v) > 65504) throw new InvalidOperationException("Shadow curve exceeds finite RGBAHalf range.");
            }
            string fingerprint;
            using (var sha = SHA256.Create()) fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Samples { values = pixels })))).Replace("-", "");
            var owned = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().Where(t => t.name == HoShadowResponseAsset.TextureName).ToArray();
            if (owned.Length > 1) throw new InvalidOperationException("The curve asset contains multiple response LUT subassets.");
            var texture = owned.SingleOrDefault();
            if (texture != null && profile.LookupTexture == texture && profile.BakedFingerprint == fingerprint
                && texture.width == pixels.Length && texture.height == 1 && texture.format == TextureFormat.RGBAHalf
                && texture.filterMode == FilterMode.Bilinear && texture.wrapMode == TextureWrapMode.Clamp && !texture.isDataSRGB) return;
            baking = true;
            try
            {
                if (recordUndo) Undo.RecordObject(profile, "Bake Shadow Response");
                if (texture == null)
                {
                    texture = new Texture2D(pixels.Length, 1, TextureFormat.RGBAHalf, false, true) { name = HoShadowResponseAsset.TextureName, hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(texture, profile);
                    // Derived cache identity survives Undo; undoing curves rebuilds this same object.
                }
                else
                {
                    if (recordUndo) Undo.RecordObject(texture, "Bake Shadow Response");
                    if (texture.width != pixels.Length || texture.height != 1 || texture.format != TextureFormat.RGBAHalf)
                        texture.Reinitialize(pixels.Length, 1, TextureFormat.RGBAHalf, false);
                }
                texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear; texture.anisoLevel = 0;
                texture.SetPixels(pixels); texture.Apply(false, false);
                profile.SetLookup(texture); profile.BakedFingerprint = fingerprint;
                EditorUtility.SetDirty(texture); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            }
            finally { baking = false; }
        }
        [Serializable] sealed class Samples { public Color[] values; }
    }
}
