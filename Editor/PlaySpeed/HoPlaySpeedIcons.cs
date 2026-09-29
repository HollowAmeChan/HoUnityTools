// HoPlaySpeedIcons.cs -- 播放速度面板上那几张小图标（内嵌 base64，不引资源文件）
//
// 【这些图标是哪来的、能不能用】
// 图标字节来自 dotsquid 的 **ChronoHelper**（`Editor/Internal/Data/Base64Image.cs`）。那份工具是 **CC0 1.0**
// （公共领域奉献，见它包里的 LICENSE.md）：可以复制、改写、商用，不要求署名。这里照抄它的字节，
// 目的就是**跟它长得一模一样**（用户点名要 icon 跟他一样）；尺寸也照旧（最大 13×12）。
//
// 【为什么不放 .png】
// 十来张、最大 13×12 的图，做成资源要带一堆 .meta 与导入设置，还会被打进包里；
// 内嵌 base64 在编辑器里现解成 Texture2D（`HideFlags.HideAndDontSave`，不落盘、不进包）最省事。
//
// 【皮肤怎么选】
// `*_light` = 深色皮肤用的白线图标，`*_dark` = 浅色皮肤用的黑线图标（名字说的是"配哪种皮肤"，
// 不是"图标什么颜色"）。有一个例外照抄原实现：`AutoResetOn` 只有 light 一版。
// （原实现里还有 Locked / Unlocked 两张，本面板"永远跟随"、没有锁定档，所以没搬过来。）
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>
    /// 面板图标的持有者：<see cref="Load"/> 一次解出全部贴图，<see cref="Clear"/> 一次性销毁。
    /// 跟着窗口的 OnEnable / OnDisable 走 —— 这些 Texture2D 是 HideAndDontSave，没人替你回收。
    /// </summary>
    internal sealed class HoPlaySpeedIcons
    {
        // ── 图标字节（CC0，来自 ChronoHelper 的 Base64Image.cs，名字沿用它的；Logo 那张 112x128 没用上）──
        /// <summary>8x8</summary>
        private const string Back =
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAM0lEQVQY033MsQ0AMAgDwYf9d3YqFJDAbs/6kCT+gjmlQ4B0WIcTe2FFINJhL6xYhxMBHi+qDAxapajWAAAAAElFTkSuQmCC";

        /// <summary>12x12</summary>
        private const string Settings_dark =
            "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAdElEQVQoz5VSwRHAIAgLPafKLHY6O0vWoi/vlMZHfXGBEAhGZuLPawBAsuKzS6ygJFylyMlt+HUocPFOkPSc5l5zkZkgma6AZC94WMKybFoCgOE6mpHuqYCDQ5tqtfWzoDOimUVvAH3GkrAeNw5fY8yZa+IFBq5HP2JJ8iYAAAAASUVORK5CYII=";

        /// <summary>12x12</summary>
        private const string Settings_light =
            "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAkUlEQVQoz5VSwQ3EIAxzqi7AFmQEdmmn681CRoAtGMH3uHICLn1cJKTIsZM4Qkjin9gBwMwmMIRAAGityYinlLCNpE5cxSO+eQQv/xGo6utp77EmJFFrpUcopRwjHmMUV9DNrit9BSJyeR3XiSRPIQkzcw2uU6ezega9Q+xrMed8Ajh6rqq48w/x4Wtc95uCJN7fkVGVyp93+QAAAABJRU5ErkJggg==";

        /// <summary>13x12</summary>
        private const string Reset_dark =
            "iVBORw0KGgoAAAANSUhEUgAAAA0AAAAMCAYAAAC5tzfZAAAAXklEQVQoz52RwQ3AMAgDz1GGCfsPk3HcVyTUqmmoXzw4jGzZZikiDGjOyU7tBhypVQEAjTFKAKBGXe6AgOymHZCDUOk/20REvvQa+QqsAaSlI8e+hq9CH1C1pz+R6wKUNx2CpAeEkwAAAABJRU5ErkJggg==";

        /// <summary>13x12</summary>
        private const string Reset_light =
            "iVBORw0KGgoAAAANSUhEUgAAAA0AAAAMCAYAAAC5tzfZAAAAXklEQVQoz52RwQ3AMAgDz1G2yf6bhHncVyTUqmmoXzw4jGzZZikiDGiMwU7tBhypVQEAzTlLAKBGXe6AgOymHZCDUOk/20REvvQa+QqsAaSlI8e+hq9CH1C1pz+R6wLdSyOOjGmt7wAAAABJRU5ErkJggg==";

        /// <summary>7x12</summary>
        private const string Pause_dark =
            "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAMCAYAAACulacQAAAAKUlEQVQY02PU0ND4zwABjNevX2dgYGBg0NTU/M/AwMDAxIAHjEoSkgQANSEFj9cbB0UAAAAASUVORK5CYII=";

        /// <summary>7x12</summary>
        private const string Pause_light =
            "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAMCAYAAACulacQAAAAKUlEQVQY02O8cuXKfwYIYNTW1mZgYGBguHr16n8GBgYGJgY8YFSSkCQA3ocHkwSoErMAAAAASUVORK5CYII=";




        /// <summary>12x12</summary>
        private const string AutoResetOn_light =
            "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAg0lEQVQoz5WQMQ7DMAwDqaJ6iCc9LnlO/ThNfggHZkgcxGkLxDcJMCnKNEmY4YVJ3gDQWgMAkJS7G0m4+yAspYwJJPU44SruM8lT5O42GCJizcxPn+9bM1MAdtOlpeV4wC+DJEjaEw5qREzXWh99+nbrFxGx9tNNEszOEpY/i+tgmGEDIxNHz8eqd30AAAAASUVORK5CYII=";

        /// <summary>12x12</summary>
        private const string AutoResetOff_dark =
            "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAdElEQVQoz5WQ0Q2DMAwFXxBL2SuFcehKXuv4AWRM2oKlSHZyL4muAXpTk17WJeDuw+fy/vQEjojlmBswhCOiJfhzHgACuplhZgD9mPdeey/gDChByvCvgOrNfwMjuAbmasPdV0lrEXC3lGz0gd27pfqNb2sD55vTX0C1gsEAAAAASUVORK5CYII=";

        /// <summary>12x12</summary>
        private const string AutoResetOff_light =
            "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAfElEQVQoz5VRwQ3EIAwzpy7j2WCcdjazje9DEOWiq5pXsOw4McU23tQHL+sm6L2ndjc8VpJkST+CgVXbsI1iO51MskgyyQbgmreOpi4ONd6jhySHA5aUgoSV/E+AffKjICPvgmMFSTZJJ4BzC6DF4JlSpJE5pCk9/XCs9AXp0pyoA0mhyQAAAABJRU5ErkJggg==";

        /// <summary>背景斜纹贴图边长（UV 平铺要用它换算）。</summary>
        public const float BackSize = 8f;

        public Texture2D back;
        public Texture2D settings;
        public Texture2D reset;
        public Texture2D pause;
        public Texture2D autoResetOn;
        public Texture2D autoResetOff;

        private readonly List<Texture2D> all = new List<Texture2D>();

        public void Load()
        {
            if (all.Count > 0)
            {
                return;
            }

            bool pro = EditorGUIUtility.isProSkin;
            Add(out back, Back, "HOPS_Back");
            Add(out settings, pro ? Settings_light : Settings_dark, "HOPS_Icon_Settings");
            Add(out reset, pro ? Reset_light : Reset_dark, "HOPS_Icon_Reset");
            Add(out pause, pro ? Pause_light : Pause_dark, "HOPS_Icon_Pause");
            Add(out autoResetOn, AutoResetOn_light, "HOPS_Icon_AutoResetOn");
            Add(out autoResetOff, pro ? AutoResetOff_light : AutoResetOff_dark, "HOPS_Icon_AutoResetOff");
        }

        public void Clear()
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(all[i]);
                }
            }

            all.Clear();
            back = null;
            settings = null;
            reset = null;
            pause = null;
            autoResetOn = null;
            autoResetOff = null;
        }

        private void Add(out Texture2D texture, string base64, string name)
        {
            texture = Create(base64, name);
            all.Add(texture);
        }

        /// <summary>base64 → Texture2D。和原实现一样用 <c>LoadImage</c>（ARGB32、可读写、双线性）。</summary>
        private static Texture2D Create(string base64, string name)
        {
            byte[] data = Convert.FromBase64String(base64);
            Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = name,
                filterMode = FilterMode.Bilinear,
            };
            texture.LoadImage(data);
            return texture;
        }
    }
}
