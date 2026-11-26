using UnityEngine;

namespace Hollow.HoUnityTools.Editor.ShadowResponse
{
    // Authoring data only. Materials reference the texture subasset, never this Editor type.
    [CreateAssetMenu(menuName = "HoShader/Shadow Response Curves", fileName = "ShadowResponse")]
    public sealed class HoShadowResponseAsset : ScriptableObject
    {
        public const string TextureName = "HoShadowResponseLUT";
        public const int Resolution = 1024;
        [SerializeField] private AnimationCurve red = AnimationCurve.Linear(0, .5f, 1, 1);
        [SerializeField] private AnimationCurve green = AnimationCurve.Linear(0, .5f, 1, 1);
        [SerializeField] private AnimationCurve blue = AnimationCurve.Linear(0, .5f, 1, 1);
        [SerializeField] private AnimationCurve alpha = AnimationCurve.Linear(0, 0, 1, 1);
        [SerializeField, HideInInspector] private Texture2D lookupTexture;
        [SerializeField, HideInInspector] private string bakedFingerprint;

        public Texture2D LookupTexture => lookupTexture;
        internal string BakedFingerprint { get => bakedFingerprint; set => bakedFingerprint = value; }
        internal void SetLookup(Texture2D value) => lookupTexture = value;
        public void SetCurves(AnimationCurve r, AnimationCurve g, AnimationCurve b, AnimationCurve a)
        { red = r; green = g; blue = b; alpha = a; HoShadowResponseBaker.Queue(this); }
        public Color Evaluate(float t) => new Color(red?.Evaluate(t) ?? Mathf.Lerp(.5f, 1, t),
            green?.Evaluate(t) ?? Mathf.Lerp(.5f, 1, t), blue?.Evaluate(t) ?? Mathf.Lerp(.5f, 1, t), Mathf.Clamp01(alpha?.Evaluate(t) ?? t));
        private void OnValidate() => HoShadowResponseBaker.Queue(this);
    }
}
