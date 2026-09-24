using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// **轨道处理**（旧面板「动画处理」那件事）：把一份 `.anim` 里**除了 Float 曲线以外的曲线块全删掉**。
    ///
    /// 【为什么是文本手术，不是 API】
    /// Unity 没有"删掉某个曲线块"的公开 API；`AnimationUtility` 只能读/写具体的绑定，
    /// 删不掉 `m_PositionCurves` / `m_RootMotionCurves` 这种**整块**。所以这里逐行扫 YAML：
    /// 见到要删的列表头就进入"删"状态（连头带内容一起跳过），见到 `m_FloatCurves` 或文件头就回到"留"。
    ///
    /// 【为什么状态机要盯"顶级属性"】
    /// 一个列表块到下一个**顶级**属性（行首没有空格 / `-` / `%`）为止。不看这一条，
    /// 上一个块的缩进行会被算进下一个块里 —— 那正是"删完之后文件结构坏了"的成因。
    ///
    /// ⚠️ **不可逆**：写完直接覆盖原文件（没有备份、没有 Undo），调用方必须先确认。
    /// 逻辑从旧的 `AnimationClipProcessorWindow` 原样搬出来，只把结果做成返回值（好让面板报出来）。
    /// </summary>
    public static class HoAnimationClipProcessor
    {
        /// <summary>要整块删掉的曲线列表（保留 `m_FloatCurves`）。</summary>
        private static readonly string[] CurveTypesToRemove =
        {
            "m_RotationCurves",
            "m_CompressedRotationCurves",
            "m_EulerCurves",
            "m_PositionCurves",
            "m_ScaleCurves",
            "m_PPtrCurves",      // 通常用于 Material 或 GameObject 引用
            "m_Bounds",          // 边界曲线
            "m_MassCenter",      // 质量中心
            "m_RootMotionCurves" // 根运动曲线
        };

        private static readonly Regex CurveTypeRegex = new Regex(
            $@"^\s*({string.Join("|", CurveTypesToRemove)}):", RegexOptions.Compiled);

        private static readonly Regex FloatCurveRegex = new Regex(@"^\s*m_FloatCurves:", RegexOptions.Compiled);

        public struct Result
        {
            /// <summary>删掉的曲线类型块数。</summary>
            public int BlocksRemoved;
            /// <summary>给面板看的一句话（失败时是原因）。</summary>
            public string Message;
            /// <summary>文件真的被改写过（`BlocksRemoved > 0`）。</summary>
            public bool Changed;
            public bool Ok;
        }

        /// <summary>
        /// 处理一份工程里的 `.anim`。**会改磁盘上的文件**，调用方负责先弹确认。
        /// 失败（不是 `Assets/` 下的 `.anim`、只读、读不出来）不抛异常，返回 <see cref="Result.Ok"/> = false。
        /// </summary>
        public static Result Process(AnimationClip clip)
        {
            if (clip == null) return Fail("没有选中动画剪辑。");

            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.ToLowerInvariant().EndsWith(".anim"))
                return Fail("请选择一个工程里的 `.anim` 动画剪辑文件。");

            // Unity 的工作目录就是工程根，所以相对路径直接拼得上。
            string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);

            if (!File.Exists(absolutePath))
                return Fail("文件不存在：" + assetPath + "（先确认它已经被 Unity 导入过。）");
            if (File.GetAttributes(absolutePath).HasFlag(FileAttributes.ReadOnly))
                return Fail("文件是只读的，改不了：" + assetPath);

            EditorUtility.DisplayProgressBar("轨道处理", "正在读取 " + assetPath, 0.1f);

            string content;
            try { content = File.ReadAllText(absolutePath); }
            catch (System.Exception e) { return Fail("读取失败：" + e.Message, true); }

            int blocksRemoved;
            string rewritten;
            try
            {
                EditorUtility.DisplayProgressBar("轨道处理", "正在逐行处理曲线…", 0.5f);
                rewritten = StripAllButFloatCurves(content, out blocksRemoved);
            }
            catch (System.Exception e) { return Fail("处理失败：" + e.Message, true); }

            if (blocksRemoved == 0)
            {
                EditorUtility.ClearProgressBar();
                return new Result
                {
                    Ok = true, Changed = false, BlocksRemoved = 0,
                    Message = "没有可删的曲线类型块，文件未修改。"
                };
            }

            try
            {
                EditorUtility.DisplayProgressBar("轨道处理", "正在写回并重新导入…", 0.95f);
                File.WriteAllText(absolutePath, rewritten);
                // 通知 Unity 文件已被外部改写，强制重新导入这一份资产。
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
            catch (System.Exception e) { return Fail("写回失败：" + e.Message, true); }
            finally { EditorUtility.ClearProgressBar(); }

            return new Result
            {
                Ok = true,
                Changed = true,
                BlocksRemoved = blocksRemoved,
                Message = "删掉了 " + blocksRemoved + " 个曲线类型块（只留 Float 曲线）：" + Path.GetFileName(assetPath)
            };
        }

        /// <summary>纯文本处理（可脱离 Unity 单测）：返回新内容与删掉的块数。</summary>
        public static string StripAllButFloatCurves(string content, out int blocksRemoved)
        {
            var builder = new StringBuilder(content.Length);
            int state = 0;   // 0 = 自由，1 = 正在删的块里，2 = 要保留的块里
            blocksRemoved = 0;

            using (var reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // 1. 遇到新的顶级属性（行首没有空格 / - / %）就退出上一个块。
                    if (!line.StartsWith(" ") && !line.StartsWith("-") && !line.StartsWith("%")) state = 0;

                    // 2. 进入删除块 / 保留块。
                    if (CurveTypeRegex.IsMatch(line))
                    {
                        state = 1;
                        blocksRemoved++;
                        continue;   // 列表头本身也不写
                    }

                    if (FloatCurveRegex.IsMatch(line)) state = 2;
                    else if (line.StartsWith("%") || line.StartsWith("--- !u!")
                        || line.Trim().StartsWith("AnimationClip:") || line.Trim().StartsWith("m_Events:"))
                        state = 2;   // 文件头与事件总是保留

                    // 3. 删除块里的行全部跳过，其余原样写回。
                    if (state == 1) continue;
                    builder.AppendLine(line);
                }
            }

            return builder.ToString();
        }

        private static Result Fail(string message, bool clearProgress = false)
        {
            if (clearProgress) EditorUtility.ClearProgressBar();
            Debug.LogError("[Ho 动画工具] " + message);
            return new Result { Ok = false, Message = message };
        }
    }
}
