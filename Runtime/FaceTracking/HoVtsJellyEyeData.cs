using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Serializable]
    public sealed class HoVtsJellyInputs
    {
        [Tooltip("从 IR 最终形态键读取，取最大值；可填左眨眼、右眨眼，也可使用其他激励键。")]
        public List<string> blinkKeys = new List<string>();
        public string lookLeft, lookRight, lookUp, lookDown;
    }

    [Serializable]
    public sealed class HoVtsJellyResponse
    {
        [Range(0f, 2f)] public float strength = 1f;
        [Range(0.1f, 30f)] public float frequency = 6f;
        [Range(0.05f, 2f)] public float damping = 0.35f;
        [Range(0f, 0.2f)] public float inputSmoothing = 0.01f;
        [Range(0f, 100f)] public float maxAmplitude = 35f;
    }

    [Serializable]
    public sealed class HoVtsJellyMovement
    {
        public string left, right, up, down;
        [Tooltip("眨眼变化对水平位移的影响；0 表示只使用视线形态键。")]
        [Range(-1f, 1f)] public float blinkHorizontal;
        [Tooltip("眨眼变化对垂直位移的影响；负值表示闭眼时先向上弹。")]
        [Range(-1f, 1f)] public float blinkVertical = 0.35f;
        public HoVtsJellyResponse response = new HoVtsJellyResponse();
    }

    [Serializable]
    public sealed class HoVtsJellyScale
    {
        public string enlarge, shrink;
        public HoVtsJellyResponse response = new HoVtsJellyResponse();
    }

    [Serializable]
    public sealed class HoVtsJellySquash
    {
        public string squash, stretch;
        public HoVtsJellyResponse response = new HoVtsJellyResponse();
    }
}
