using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Serializable]
    public sealed class HoVtsJellyInputs
    {
        [Tooltip("同眼的情绪／闭合阶段形态键放在同一组，加权相加后限制在 0～1；不同组取最大值。")]
        public List<HoVtsJellyInputGroup> blinkGroups = new List<HoVtsJellyInputGroup>();
        [Tooltip("旧配置的独立激励键，保持取最大值的原语义；配置了有名称的分组键后改用分组，不再叠加此列表。")]
        public List<string> blinkKeys = new List<string>();
        public string lookLeft, lookRight, lookUp, lookDown;

        public bool UsesBlinkGroups
        {
            get
            {
                if (blinkGroups != null) foreach (var group in blinkGroups)
                    if (group?.keys != null) foreach (var item in group.keys)
                        if (item != null && !string.IsNullOrEmpty(item.key)) return true;
                return false;
            }
        }
    }

    [Serializable]
    public sealed class HoVtsJellyInputGroup
    {
        public string name = "输入组";
        public List<HoVtsJellyInputWeight> keys = new List<HoVtsJellyInputWeight>();
    }

    [Serializable]
    public sealed class HoVtsJellyInputWeight
    {
        [Tooltip("IR 已实际写出的形态键名。")]
        public string key;
        [Tooltip("这个形态键对闭合程度的贡献；0 跳过。应按当前 IR 各控制点的语义设置，不只按模型键名猜测。")]
        [Min(0f)] public float weight = 1f;
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
