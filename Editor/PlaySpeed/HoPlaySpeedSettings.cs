// HoPlaySpeedSettings.cs -- 播放速度面板的配置：存什么、存哪、怎么清洗
//
// 存 **EditorPrefs**（每台机器一份，和参考实现 ChronoHelper 一样），不落进工程：
// 速度点是"我这台机器顺手"的东西，塞进仓库只会变成每次拉代码都打架的资产。
// 序列化用 JsonUtility，所以整个 Data 是 [Serializable] 的公开字段 —— **别改字段名**（改了等于旧配置全丢）。
//
// 读回来一定要过 `HoPlaySpeedPresets.Sanitize`：EditorPrefs 里的字符串可能来自旧版本、也可能被手改过，
// 少一颗 0× 或者冒出个负速度都不该让面板崩掉或变得没法用。
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>速度点与滑杆谁在左（面板只有一行，所以只分左右）。</summary>
    public enum HoPlaySpeedBlockOrder
    {
        Normal,
        Reversed,
    }

    /// <summary>速度点按钮的宽度：等宽（对齐好看）/ 按文字自适应。</summary>
    public enum HoPlaySpeedButtonWidth
    {
        Equal,
        AsIs,
    }

    /// <summary>什么时候闪警告（有人在面板背后改了 Time.timeScale）。面板永远跟随外部，所以只有两档。</summary>
    public enum HoPlaySpeedWarning
    {
        /// <summary>从不。</summary>
        Never,

        /// <summary>只要值对不上就闪。默认这档：外部改动本来就该知道一声。</summary>
        Always,
    }

    /// <summary>面板的全部可存状态。</summary>
    [Serializable]
    public sealed class HoPlaySpeedSettingsData
    {
        /// <summary>期望速度：面板的权威值，播放模式下会被写进 Time.timeScale。</summary>
        public float speed = 1f;

        /// <summary>播放结束时把期望速度复位成进播放时那个值（参考实现的默认行为）。</summary>
        public bool resetOnPlayEnd = true;

        /// <summary>显示「复位」按钮。</summary>
        public bool showResetButton = true;

        public HoPlaySpeedBlockOrder blockOrder = HoPlaySpeedBlockOrder.Normal;
        public HoPlaySpeedButtonWidth buttonWidth = HoPlaySpeedButtonWidth.Equal;
        public HoPlaySpeedFormat format = HoPlaySpeedFormat.Compact;

        public HoPlaySpeedWarning warning = HoPlaySpeedWarning.Always;

        /// <summary>警告背景色（alpha 决定多显眼）。默认取设计令牌里的「提醒橙」，和约束面板的提醒色是一家人。</summary>
        public Color warningColor = new Color(0.910f, 0.639f, 0.239f, 0.75f);

        /// <summary>闪烁周期（秒）。</summary>
        public float warningBlinkPeriod = 2f;

        /// <summary>一次警告闪多久（秒），到点自动停。</summary>
        public float warningBlinkDuration = 4f;

        /// <summary>速度点。保证含 0× 与 1×（固定点，删不掉）。</summary>
        public List<HoPlaySpeedPreset> presets = HoPlaySpeedPresets.CreateDefault();

        /// <summary>按值排序。</summary>
        public void SortPresets()
        {
            HoPlaySpeedPresets.Sort(presets);
        }

        /// <summary>速度点里最大的那个值（滑杆的上限参照它，至少 1）。</summary>
        public float MaxPresetValue()
        {
            float max = 1f;
            if (presets != null)
            {
                for (int i = 0; i < presets.Count; i++)
                {
                    if (presets[i] != null && presets[i].value > max)
                    {
                        max = presets[i].value;
                    }
                }
            }

            return max;
        }
    }

    /// <summary>配置的载体：单例 + EditorPrefs 读写 + 变更通知。</summary>
    internal static class HoPlaySpeedSettings
    {
        /// <summary>速度硬上限。参考实现是 100×，照抄；再快下去 Unity 的固定步长会直接放弃追帧。</summary>
        public const float MaxSpeed = 100f;

        /// <summary>EditorPrefs 键。带包名，免得和别的工具撞。</summary>
        public const string PrefsKey = "com.hollow.hounitytools.playspeed";

        private static HoPlaySpeedSettingsData data;
        private static int revision;

        /// <summary>配置变了（设置窗口改的、恢复默认、外部改动被采纳）。速度条据此重建按钮并重画。</summary>
        public static event Action Changed;

        public static HoPlaySpeedSettingsData Data
        {
            get
            {
                if (data == null)
                {
                    Load();
                }

                return data;
            }
        }

        /// <summary>每变一次加一。速度条拿它判断"我缓存的那排按钮过期了没有"。</summary>
        public static int Revision => revision;

        /// <summary>改完配置调它：通知 + 存盘。离散操作（点按钮、切开关、改列表）才调，拖滑杆别调。</summary>
        public static void NotifyChanged()
        {
            revision++;
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }

            Save();
        }

        public static void Save()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[播放速度] 配置存不进 EditorPrefs：" + exception.Message);
            }
        }

        public static void Load()
        {
            data = new HoPlaySpeedSettingsData();
            try
            {
                if (EditorPrefs.HasKey(PrefsKey))
                {
                    string json = EditorPrefs.GetString(PrefsKey);
                    if (!string.IsNullOrEmpty(json))
                    {
                        JsonUtility.FromJsonOverwrite(json, data);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[播放速度] EditorPrefs 里的配置读不出来，用默认值：" + exception.Message);
                data = new HoPlaySpeedSettingsData();
            }

            Sanitize(data);
        }

        /// <summary>恢复全部默认（速度点、布局、警告、锁定全回去）。</summary>
        public static void ResetToDefault()
        {
            data = new HoPlaySpeedSettingsData();
            Sanitize(data);
            NotifyChanged();
        }

        /// <summary>把界面上填进来的值收进合法范围。</summary>
        private static void Sanitize(HoPlaySpeedSettingsData target)
        {
            target.speed = Mathf.Clamp(target.speed, 0f, MaxSpeed);
            target.warningBlinkPeriod = Mathf.Clamp(target.warningBlinkPeriod, 0.1f, 10f);
            target.warningBlinkDuration = Mathf.Clamp(target.warningBlinkDuration, 0.5f, 20f);
            target.warningColor.a = Mathf.Clamp01(target.warningColor.a);

            // 枚举也要夹：旧配置里存的是"跟随"那一档的数字（原来有 3 档），现在只剩 2 档，
            // 越界值会让 switch 落到 default —— 与其那样，不如直接当成"总是"
            int warning = Mathf.Clamp((int)target.warning, (int)HoPlaySpeedWarning.Never, (int)HoPlaySpeedWarning.Always);
            target.warning = (HoPlaySpeedWarning)warning;

            if (target.presets == null)
            {
                target.presets = HoPlaySpeedPresets.CreateDefault();
            }

            HoPlaySpeedPresets.Sanitize(target.presets);
        }
    }
}
