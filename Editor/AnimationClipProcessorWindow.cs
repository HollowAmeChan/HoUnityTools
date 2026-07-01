using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hollow.HoUnityTools.Editor
{
    /// <summary>
    /// 动画剪辑处理工具 - 用于优化动画文件，仅保留 Float 曲线
    /// </summary>
    public class AnimationClipProcessorWindow : EditorWindow
    {
        // 序列化字段用于保存UI状态
        [SerializeField]
        private AnimationClip targetClip;

        // ============== 【曲线类型定义】 ==============

        // 定义所有需要删除的曲线列表名称（保留 m_FloatCurves）
        private static readonly string[] CurveTypesToRemove = new string[]
        {
            "m_RotationCurves",
            "m_CompressedRotationCurves",
            "m_EulerCurves",
            "m_PositionCurves",
            "m_ScaleCurves",
            "m_PPtrCurves",     // 通常用于 Material 或 GameObject 引用
            "m_Bounds",         // 边界曲线
            "m_MassCenter",     // 质量中心
            "m_RootMotionCurves"// 根运动曲线
        };

        // 用于匹配这些曲线类型列表名称的正则表达式 (例如：^\s*m_RotationCurves:)
        private static readonly Regex CurveTypeRegex = new Regex($@"^\s*({string.Join("|", CurveTypesToRemove)}):", RegexOptions.Compiled);

        // 用于匹配我们想要保留的 m_FloatCurves 列表的正则表达式
        private static readonly Regex FloatCurveRegex = new Regex(@"^\s*m_FloatCurves:", RegexOptions.Compiled);

        // ========================================================

        [MenuItem("HoUnityTools/动画处理")]
        public static void ShowWindow()
        {
            GetWindow<AnimationClipProcessorWindow>("动画处理").minSize = new Vector2(400, 150);
        }

        void OnGUI()
        {
            Draw_ClipSelect();
            EditorGUILayout.Space(10);
            Draw_CleanClipExceptFloatCurve();
        }

        private void Draw_ClipSelect()
        {
            // 警告：由于移除了备份，强调操作的不可逆性
            EditorGUILayout.HelpBox(
                "⚠️ 警告：此工具直接读写文件内容，操作不可逆，请在执行前自行备份动画文件。",
                MessageType.Error
            );

            targetClip = (AnimationClip)EditorGUILayout.ObjectField(
                "目标动画剪辑 (.anim)",
                targetClip,
                typeof(AnimationClip),
                false
            );
        }

        private void Draw_CleanClipExceptFloatCurve()
        {
            EditorGUI.BeginDisabledGroup(targetClip == null);

            if (GUILayout.Button($"仅保留Float曲线", GUILayout.Height(30)))
            {
                if (targetClip != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(targetClip);
                    if (string.IsNullOrEmpty(assetPath) || !assetPath.ToLower().EndsWith(".anim"))
                    {
                        Debug.LogError("请选择一个有效的项目中的 .anim 动画剪辑文件。", targetClip);
                        return;
                    }

                    // 确认操作，因为没有撤销机制
                    if (!EditorUtility.DisplayDialog("确认操作", $"确定要从 {Path.GetFileName(assetPath)} 中永久删除所有 非Float 曲线吗？此操作不可撤销。", "是，删除", "否，取消"))
                    {
                        return;
                    }

                    // 执行文件操作
                    CleanClipExceptFloatCurve(assetPath);
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        private static void CleanClipExceptFloatCurve(string assetPath)
        // 删除 FloatCurve 以外的全部曲线
        {
            string originalAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);

            if (!File.Exists(originalAbsolutePath) || File.GetAttributes(originalAbsolutePath).HasFlag(FileAttributes.ReadOnly))
            {
                Debug.LogError($"文件不存在或处于只读状态，无法修改: {assetPath}");
                return;
            }

            EditorUtility.DisplayProgressBar("极速处理中", $"正在读取文件: {assetPath}", 0.1f);

            string originalContent;
            try
            {
                originalContent = File.ReadAllText(originalAbsolutePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"读取文件失败: {e.Message}");
                EditorUtility.ClearProgressBar();
                return;
            }

            // **【核心逻辑：状态机逐行处理 - 按曲线类型删除】**
            StringBuilder newContentBuilder = new StringBuilder();
            StringReader reader = new StringReader(originalContent);
            string line;

            // 状态变量
            // 0: 自由状态，查找下一个列表
            // 1: 位于要删除的曲线列表块内部 (如 m_PositionCurves)
            // 2: 位于要保留的曲线列表块内部 (如 m_FloatCurves 或文件头)
            int state = 0;
            int blocksRemoved = 0; // 记录删除了多少个曲线类型块

            EditorUtility.DisplayProgressBar("处理中", "正在逐行处理曲线...", 0.5f);
            int lineCount = originalContent.Split('\n').Length;
            int currentLine = 0;

            while ((line = reader.ReadLine()) != null)
            {
                currentLine++;
                if (currentLine % 500 == 0)
                {
                    EditorUtility.DisplayProgressBar("处理中", $"正在处理行 {currentLine}/{lineCount}...", 0.5f + 0.4f * ((float)currentLine / lineCount));
                }

                // 1. 检查是否遇到了新的顶级属性
                // 如果遇到一个更高的YAML层级属性（没有以空格、-、% 开头），表示退出了上一个块
                if (!line.StartsWith(" ") && !line.StartsWith("-") && !line.StartsWith("%"))
                {
                    // 遇到新的顶级属性，重置状态为自由状态
                    state = 0;
                }

                // 2. 检查是否进入/保持 删除 或 保留 状态

                // 匹配所有我们要删除的曲线列表
                if (CurveTypeRegex.IsMatch(line))
                {
                    state = 1; // 进入删除状态
                    blocksRemoved++;
                    // 不需要写入列表头，直接跳过到下一个循环
                    continue;
                }
                // 匹配我们要保留的 m_FloatCurves 列表
                else if (FloatCurveRegex.IsMatch(line))
                {
                    state = 2; // 进入保留状态
                }
                // 匹配文件头部 (总是保留)
                else if (line.StartsWith("%") || line.StartsWith("--- !u!") || line.Trim().StartsWith("AnimationClip:") || line.Trim().StartsWith("m_Events:"))
                {
                    state = 2; // 文件头和事件总是保留
                }

                // 3. 决定是否写入该行
                if (state == 1)
                {
                    // 处于删除状态 (state = 1)，跳过写入该行
                    continue;
                }
                else
                {
                    // 处于自由状态 (state = 0) 或保留状态 (state = 2)，写入该行
                    newContentBuilder.AppendLine(line);
                }
            }

            string newContent = newContentBuilder.ToString();

            if (blocksRemoved > 0)
            {
                EditorUtility.DisplayProgressBar("处理中", $"正在写入文件并刷新资产...", 0.95f);

                // 写入文件
                File.WriteAllText(originalAbsolutePath, newContent);

                // 通知 Unity 文件已更改，强制重新导入该资产
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                Debug.Log($"✅ 处理完成! 动画剪辑 **{assetPath}** 中删除了 **{blocksRemoved}** 个曲线类型块（FloatCurve被保留）。", AssetDatabase.LoadAssetAtPath<Object>(assetPath));
            }
            else
            {
                Debug.Log($"ℹ️ 动画剪辑 **{assetPath}** 中未发现可删除的曲线类型块。文件未被修改。", AssetDatabase.LoadAssetAtPath<Object>(assetPath));
            }

            EditorUtility.ClearProgressBar();
        }
    }
}
