// HoPlaySpeedPreset.cs -- 播放速度面板的「速度点」与按钮文字格式化
//
// 速度点 = 面板上一颗常驻按钮代表的一个速度值（Time.timeScale 的倍数）。
// 一条硬规矩：**0 与 1 必须存在、且标成"固定"** —— 暂停与常速是面板的两个确定落点；
// 少了它们，想停住或想回到正常就只能靠手拖滑杆，而滑杆是拖不准的。
//
// 按钮文字三档（`HoPlaySpeedFormat`）：
//   AsIs    原样     ×0.125
//   Short   短       ×0.125（去掉多余的 0）
//   Compact 紧凑     ×⅛      —— 分数用 Unicode 分数符号（Number Forms 区），比 ×0.125 短一半
// 数字一律用 InvariantCulture 格式化：跟着系统区域走的话，某些区域会输出 `0,125` 甚至阿拉伯数字。
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>速度点按钮上那串文字怎么排。</summary>
    public enum HoPlaySpeedFormat
    {
        AsIs,
        Short,
        Compact,
    }

    /// <summary>
    /// 一个速度点。<see cref="pinned"/> 的点删不掉（默认就是 0 与 1）。
    /// 这个类型会被 <c>JsonUtility</c> 直接序列化进 EditorPrefs，所以字段是公开的、别改名。
    /// </summary>
    [Serializable]
    public sealed class HoPlaySpeedPreset
    {
        /// <summary>速度值（Time.timeScale 的倍数，0 ~ <see cref="HoPlaySpeedSettings.MaxSpeed"/>）。</summary>
        public float value = 1f;

        /// <summary>自定义按钮文字；空 = 按面板的「按钮格式」自动生成。</summary>
        public string label = "";

        /// <summary>固定点：界面上删不掉（防止把 0× 和 1× 弄丢）。</summary>
        public bool pinned;

        public HoPlaySpeedPreset()
        {
        }

        public HoPlaySpeedPreset(float value, bool pinned = false, string label = "")
        {
            this.value = value;
            this.pinned = pinned;
            this.label = label ?? "";
        }

        /// <summary>按钮上显示什么（自定义优先）。</summary>
        public string Display(HoPlaySpeedFormat format)
        {
            return string.IsNullOrEmpty(label) ? HoPlaySpeedFormatting.Format(value, format) : label;
        }

        public HoPlaySpeedPreset Clone()
        {
            return new HoPlaySpeedPreset(value, pinned, label);
        }
    }

    /// <summary>把速度值排成按钮文字。</summary>
    internal static class HoPlaySpeedFormatting
    {
        private const string Prefix = "×";

        /// <summary>Decimal 与分数符号的容差。表里最小的间隔是 1/9−1/10≈0.0111，取它的一半以下。</summary>
        private const float FractionTolerance = 0.005f;

        private static readonly float[] FractionValues =
        {
            1f / 10f, 1f / 9f, 1f / 8f, 1f / 7f, 1f / 6f, 1f / 5f, 1f / 4f, 1f / 3f, 3f / 8f,
            2f / 5f, 1f / 2f, 3f / 5f, 5f / 8f, 2f / 3f, 3f / 4f, 4f / 5f, 5f / 6f, 7f / 8f,
        };

        /// <summary>与 <see cref="FractionValues"/> 一一对应的 Unicode 分数符号。</summary>
        private static readonly string[] FractionGlyphs =
        {
            "⅒", "⅑", "⅛", "⅐", "⅙", "⅕", "¼", "⅓", "⅜",
            "⅖", "½", "⅗", "⅝", "⅔", "¾", "⅘", "⅚", "⅞",
        };

        public static string Format(float value, HoPlaySpeedFormat format)
        {
            switch (format)
            {
                case HoPlaySpeedFormat.Short:
                    return Prefix + Trim(value);
                case HoPlaySpeedFormat.Compact:
                    return Prefix + Compact(value);
                default:
                    return Prefix + value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>去掉多余的 0：0.125 / 1.5 / 2。</summary>
        private static string Trim(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>带分数：1.5 ⇒ <c>1½</c>；凑不到分数符号就退回小数（别硬凑）。</summary>
        private static string Compact(float value)
        {
            if (value <= 0f)
            {
                return "0";
            }

            float integral = Mathf.Floor(value);
            string glyph = FindFractionGlyph(value - integral);
            if (glyph == null)
            {
                return Trim(value);
            }

            return integral >= 1f
                ? ((int)integral).ToString(CultureInfo.InvariantCulture) + glyph
                : glyph;
        }

        private static string FindFractionGlyph(float fraction)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < FractionValues.Length; i++)
            {
                float distance = Mathf.Abs(fraction - FractionValues[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 && bestDistance <= FractionTolerance ? FractionGlyphs[best] : null;
        }
    }

    /// <summary>速度点列表的小工具（默认表、清洗、排序）。</summary>
    internal static class HoPlaySpeedPresets
    {
        /// <summary>默认那七颗（与参考实现一致：暂停 / ⅛ / ¼ / ½ / 常速 / 1.5 / 2）。</summary>
        public static List<HoPlaySpeedPreset> CreateDefault()
        {
            return new List<HoPlaySpeedPreset>
            {
                new HoPlaySpeedPreset(0f, true),
                new HoPlaySpeedPreset(0.125f),
                new HoPlaySpeedPreset(0.25f),
                new HoPlaySpeedPreset(0.5f),
                new HoPlaySpeedPreset(1f, true),
                new HoPlaySpeedPreset(1.5f),
                new HoPlaySpeedPreset(2f),
            };
        }

        /// <summary>按值排序（同值固定点在前，免得"固定"的按钮被挤来挤去）。</summary>
        public static void Sort(List<HoPlaySpeedPreset> presets)
        {
            presets.Sort((a, b) =>
            {
                int byValue = a.value.CompareTo(b.value);
                return byValue != 0 ? byValue : b.pinned.CompareTo(a.pinned);
            });
        }

        /// <summary>
        /// 清洗：丢空项、夹范围、去掉重复值、补齐 0 与 1、排序。
        /// 每次读 EditorPrefs（或用户在设置窗口里改）之后都要过一遍 —— 手写过的 JSON 什么都有。
        /// </summary>
        public static void Sanitize(List<HoPlaySpeedPreset> presets)
        {
            if (presets == null)
            {
                return;
            }

            for (int i = presets.Count - 1; i >= 0; i--)
            {
                HoPlaySpeedPreset preset = presets[i];
                if (preset == null)
                {
                    presets.RemoveAt(i);
                    continue;
                }

                preset.value = Mathf.Clamp(preset.value, 0f, HoPlaySpeedSettings.MaxSpeed);
                preset.label = preset.label ?? "";
            }

            // 重复值只留一颗（固定点优先）
            for (int i = presets.Count - 1; i >= 0; i--)
            {
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (Mathf.Abs(presets[i].value - presets[j].value) <= 0.0001f)
                    {
                        duplicate = true;
                        if (presets[i].pinned)
                        {
                            presets[j].pinned = true;
                        }

                        break;
                    }
                }

                if (duplicate)
                {
                    presets.RemoveAt(i);
                }
            }

            EnsureFixedPoint(presets, 0f);
            EnsureFixedPoint(presets, 1f);

            Sort(presets);
        }

        /// <summary>0× 与 1× 必须在，而且必须是固定点。</summary>
        private static void EnsureFixedPoint(List<HoPlaySpeedPreset> presets, float value)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                if (Mathf.Abs(presets[i].value - value) <= 0.0001f)
                {
                    presets[i].pinned = true;
                    return;
                }
            }

            presets.Add(new HoPlaySpeedPreset(value, true));
        }
    }
}
