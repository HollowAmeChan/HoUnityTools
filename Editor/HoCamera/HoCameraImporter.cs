#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Hollow.HoUnityTools.HoCamera;

namespace Hollow.HoUnityTools.Editor.HoCamera
{
    [Serializable]
    internal sealed class HoCameraJson
    {
        public string schema;
        public string version;
        public string sourceCamera;
        public string projection;
        public float frameRate = 24f;
        public int startFrame;
        public int endFrame;
        public HoCameraJsonFrame[] frames;
        // Blender exporter writes the initial camera state at the root as well.
        public HoCameraJsonFrame camera;
        public HoCameraJsonFrame initial;
    }

    [Serializable]
    internal sealed class HoCameraJsonFrame
    {
        public float frame;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool orthographic;
        public float fieldOfView;
        public float fovY;
        public float focalLength;
        public float orthographicSize;
        public float nearClipPlane;
        public float farClipPlane;
        public Vector2 lensShift;
    }

    [ScriptedImporter(1, "hocamera", 100)]
    public sealed class HoCameraImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            try
            {
                string json = File.ReadAllText(context.assetPath);
                HoCameraJson source = JsonUtility.FromJson<HoCameraJson>(json);
                if (source == null) throw new InvalidDataException("HoCamera JSON 为空。");
                HoCameraJsonFrame[] input = source.frames ?? Array.Empty<HoCameraJsonFrame>();
                if (input.Length == 0 && source.camera != null) input = new[] { source.camera };
                if (input.Length == 0 && source.initial != null) input = new[] { source.initial };
                if (input.Length == 0) throw new InvalidDataException("HoCamera 至少需要一个 camera frame。");

                var frames = new List<HoCameraFrame>(input.Length);
                for (int i = 0; i < input.Length; ++i)
                {
                    HoCameraJsonFrame f = input[i] ?? new HoCameraJsonFrame();
                    frames.Add(Convert(f, source.projection));
                }
                frames.Sort((a, b) => a.frame.CompareTo(b.frame));
                HoCameraFrame initial = frames[0];
                int first = source.startFrame != 0 ? source.startFrame : Mathf.RoundToInt(initial.frame);
                int last = source.endFrame != 0 ? source.endFrame : Mathf.RoundToInt(frames[frames.Count - 1].frame);
                float fps = source.frameRate > 0f && !float.IsNaN(source.frameRate) ? source.frameRate : 24f;
                AnimationClip clip = BuildClip(frames, fps, first, last);
                clip.name = Path.GetFileNameWithoutExtension(context.assetPath) + "_Animation";
                var asset = ScriptableObject.CreateInstance<HoCameraAsset>();
                asset.name = Path.GetFileNameWithoutExtension(context.assetPath);
                asset.SetData(source.sourceCamera, fps, first, last, initial, frames, clip);
                context.AddObjectToAsset("HoCameraAsset", asset);
                context.AddObjectToAsset("Animation", clip);
                context.SetMainObject(asset);
            }
            catch (Exception e)
            {
                context.LogImportError("HoCamera 导入失败：" + e.Message);
            }
        }

        private static HoCameraFrame Convert(HoCameraJsonFrame f, string projection)
        {
            bool ortho = f.orthographic || string.Equals(projection, "orthographic", StringComparison.OrdinalIgnoreCase);
            float fov = f.fieldOfView != 0f ? f.fieldOfView : (f.fovY != 0f ? f.fovY : 60f);
            return new HoCameraFrame
            {
                frame = f.frame, position = f.position, rotation = Normalize(f.rotation), orthographic = ortho,
                fieldOfView = fov, orthographicSize = f.orthographicSize != 0f ? f.orthographicSize : 5f,
                nearClipPlane = f.nearClipPlane != 0f ? f.nearClipPlane : 0.01f,
                farClipPlane = f.farClipPlane != 0f ? f.farClipPlane : 1000f, lensShift = f.lensShift
            };
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 0.000001f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }

        private static AnimationClip BuildClip(List<HoCameraFrame> frames, float fps, int first, int last)
        {
            var clip = new AnimationClip { frameRate = fps, legacy = false, wrapMode = WrapMode.Once };
            var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
            var qx = new AnimationCurve(); var qy = new AnimationCurve(); var qz = new AnimationCurve(); var qw = new AnimationCurve();
            var fov = new AnimationCurve(); var ortho = new AnimationCurve(); var os = new AnimationCurve();
            var near = new AnimationCurve(); var far = new AnimationCurve(); var sx = new AnimationCurve(); var sy = new AnimationCurve();
            foreach (HoCameraFrame f in frames)
            {
                float t = (f.frame - first) / fps;
                px.AddKey(t, f.position.x); py.AddKey(t, f.position.y); pz.AddKey(t, f.position.z);
                qx.AddKey(t, f.rotation.x); qy.AddKey(t, f.rotation.y); qz.AddKey(t, f.rotation.z); qw.AddKey(t, f.rotation.w);
                fov.AddKey(t, f.fieldOfView); ortho.AddKey(t, f.orthographic ? 1f : 0f); os.AddKey(t, f.orthographicSize);
                near.AddKey(t, f.nearClipPlane); far.AddKey(t, f.farClipPlane); sx.AddKey(t, f.lensShift.x); sy.AddKey(t, f.lensShift.y);
            }
            Set(clip, typeof(Transform), "m_LocalPosition.x", px); Set(clip, typeof(Transform), "m_LocalPosition.y", py); Set(clip, typeof(Transform), "m_LocalPosition.z", pz);
            Set(clip, typeof(Transform), "m_LocalRotation.x", qx); Set(clip, typeof(Transform), "m_LocalRotation.y", qy); Set(clip, typeof(Transform), "m_LocalRotation.z", qz); Set(clip, typeof(Transform), "m_LocalRotation.w", qw);
            Set(clip, typeof(Camera), "field of view", fov); Set(clip, typeof(Camera), "orthographic", ortho); Set(clip, typeof(Camera), "orthographic size", os);
            Set(clip, typeof(Camera), "near clip plane", near); Set(clip, typeof(Camera), "far clip plane", far); Set(clip, typeof(Camera), "m_LensShift.x", sx); Set(clip, typeof(Camera), "m_LensShift.y", sy);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static void Set(AnimationClip clip, Type type, string property, AnimationCurve curve)
        {
            clip.SetCurve(string.Empty, type, property, curve);
        }
    }
}
#endif
