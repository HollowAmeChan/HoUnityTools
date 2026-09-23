// 一次性验证探针：把 HoAnimationClipPreviewer 的姿势与 Unity 自己的预览路径（内存 AnimatorController +
// Animator.Play/Update）逐帧对比，证明 PlayableGraph 对 Humanoid 的求值与 Animator 一致。
//
// 放进一次性验证工程的 Assets/Editor，调用 HoAnimationPreviewValidation.RunBatch。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.Animations;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HoAnimationPreviewValidation
{
    const string HumanoidPath = "Assets/ProbeHumanoid.fbx";
    static readonly List<string> Lines = new List<string>();
    static int passed;
    static int failed;

    static void Check(bool condition, string name)
    {
        if (condition)
        {
            passed++;
            Log("PASS  " + name);
        }
        else
        {
            failed++;
            Log("FAIL  " + name);
        }
    }

    static void Log(string message)
    {
        Lines.Add(message);
        Debug.Log("[HOPREV] " + message);
    }

    public static void RunBatch()
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            failed++;
            Log("FATAL " + e);
        }
        finally
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "preview_result.txt");
            Lines.Insert(0, failed == 0 ? "HO_PREVIEW_ALL_PASSED (" + passed + ")" : "HO_PREVIEW_FAILED (" + failed + "/" + (passed + failed) + ")");
            File.WriteAllText(path, string.Join("\n", Lines.ToArray()), new UTF8Encoding(false));
            Debug.Log("HO_PREVIEW_DONE " + path);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }

    static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 找一个 Humanoid 来源 ---------------------------------------------------
        Avatar avatar = null;
        AnimationClip clip = null;
        AnimationClip otherClip = null;
        GameObject model = null;
        if (File.Exists(HumanoidPath))
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(HumanoidPath))
            {
                if (o is Avatar a && a.isHuman && avatar == null) avatar = a;
                if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                {
                    if (clip == null) clip = c;
                    else if (otherClip == null && c != clip) otherClip = c;
                }
                if (o is GameObject g && model == null) model = g;
            }
        }

        Log("source avatar=" + (avatar == null ? "null" : avatar.name + " human=" + avatar.isHuman)
            + " clip=" + (clip == null ? "null" : clip.name + " len=" + clip.length.ToString("F3")
                + " fps=" + clip.frameRate + " humanMotion=" + clip.humanMotion)
            + " otherClip=" + (otherClip == null ? "null" : otherClip.name + " len=" + otherClip.length.ToString("F3"))
            + " model=" + (model == null ? "null" : model.name));

        if (avatar == null || clip == null || model == null)
        {
            Log("SKIP: 没有可用的 Humanoid 来源，无法做人类骨骼对比。");
            return;
        }

        // ---- 搭台 -------------------------------------------------------------------
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (root == null) root = UnityEngine.Object.Instantiate(model);
        root.name = "PreviewRig";
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;

        Animator animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // 记录"原始状态"，最后检查是否被原样还原。
        RuntimeAnimatorController originalController = animator.runtimeAnimatorController;

        var previewer = root.AddComponent<HoAnimationClipPreviewer>();

        // 采集点：整副骨架的本地旋转 + 世界位置。
        List<Transform> bones = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) bones.Add(t);
        Log("boneCount=" + bones.Count);

        int frames = 6;

        // ---- A：组件（PlayableGraph 路径） ------------------------------------------
        // 编辑器里组件不会自动接管，所以显式发起预览（这也正是面板「预览」按钮做的事）。
        previewer.SetClip(clip, true);
        previewer.SetTime(0f, false);
        Check(previewer.IsPreviewing, "A 进入预览（图已建立）");
        Check(!previewer.IsPlaying, "A SetTime 之后处于暂停");

        List<string> posesA = new List<string>();
        for (int f = 0; f < frames; f++)
        {
            float t = clip.length * f / (frames - 1);
            previewer.SetTime(t, false);
            posesA.Add(Capture(bones));
        }

        Check(Distinct(posesA) > 1, "A 逐帧姿势在变化（distinct=" + Distinct(posesA) + "）");

        // 暂停稳定性：同一时间点反复求值 + 空跑若干次 Update 之后姿势不变。
        previewer.SetTime(clip.length * 0.37f, false);
        string pausedBefore = Capture(bones);
        for (int i = 0; i < 20; i++) previewer.AdvanceBy(0.05f);
        string pausedAfter = Capture(bones);
        Check(pausedBefore == pausedAfter, "A 暂停时骨架不动（20 次推进无变化）");

        bool playing = previewer.IsPlaying;
        previewer.StepOneFrame(true);
        Check(previewer.CurrentFrame == Mathf.RoundToInt(clip.length * 0.37f * clip.frameRate) + 1,
            "A StepOneFrame 前进一帧（frame=" + previewer.CurrentFrame + "）");
        previewer.StepOneFrame(false);
        Check(previewer.CurrentFrame == Mathf.RoundToInt(clip.length * 0.37f * clip.frameRate),
            "A StepOneFrame 后退一帧（frame=" + previewer.CurrentFrame + "）");

        // 倍速：同样的墙钟时间，2 倍速应走两倍播放头。
        previewer.SetTime(0f, true);
        previewer.PlaybackSpeed = 1f;
        previewer.AdvanceBy(0.1f);
        float at1x = previewer.CurrentTime;
        previewer.SetTime(0f, true);
        previewer.PlaybackSpeed = 2f;
        previewer.AdvanceBy(0.1f);
        float at2x = previewer.CurrentTime;
        Check(Mathf.Abs(at1x - 0.1f) < 0.001f, "A 1 倍速 0.1s 走 0.1s（" + at1x.ToString("F4") + "）");
        Check(Mathf.Abs(at2x - 0.2f) < 0.001f, "A 2 倍速 0.1s 走 0.2s（" + at2x.ToString("F4") + "）");
        previewer.PlaybackSpeed = 1f;

        // 帧/时间换算。
        Check(previewer.FrameCount == Mathf.RoundToInt(clip.length * clip.frameRate) + 1,
            "A FrameCount = round(len*fps)+1（" + previewer.FrameCount + "）");
        previewer.SetFrame(3, false);
        Check(Mathf.Abs(previewer.CurrentTime - 3f / clip.frameRate) < 0.0005f,
            "A SetFrame(3) 的秒数 = 3/fps（" + previewer.CurrentTime.ToString("F4") + "）");

        // 循环折返：非循环模式到片尾应停住。
        previewer.Loop = false;
        previewer.SetTime(clip.length - 0.01f, true);
        previewer.AdvanceBy(0.5f);
        Check(!previewer.IsPlaying && Mathf.Abs(previewer.CurrentTime - clip.length) < 0.001f,
            "A 非循环到片尾停住（t=" + previewer.CurrentTime.ToString("F3") + " playing=" + previewer.IsPlaying + "）");

        previewer.Loop = true;
        previewer.SetTime(clip.length - 0.01f, true);
        previewer.AdvanceBy(0.05f);
        Check(previewer.IsPlaying && previewer.CurrentTime < 0.05f,
            "A 循环折返到片头（t=" + previewer.CurrentTime.ToString("F4") + "）");
        previewer.Pause();

        // ---- A2：换 clip 的残留 -----------------------------------------------------
        // PlayableGraph 只写"这条 clip 里有的通道"。用两条不同的 clip 交替切换，
        // 检查上一条留下的姿势/形态键有没有被清掉 —— 这正是"切换动画有残余"的复现路径。
        if (otherClip != null)
        {
            // 先记下"未预览"的形态键基准，再进预览 —— 用它检验切 clip 时有没有残留。
            List<SkinnedMeshRenderer> blendRenderers;
            List<float[]> blendBaseline;
            CaptureBlendShapes(root, out blendRenderers, out blendBaseline);

            previewer.SetClip(clip, true);
            previewer.Pause();
            previewer.SetTime(clip.length * 0.6f, false);
            string afterFirst = Capture(bones);

            previewer.SetClip(otherClip, true);
            float blendAtSwitch = MaxBlendShapeDeviation(blendRenderers, blendBaseline);

            previewer.SetClip(clip, true);
            previewer.Pause();
            previewer.SetTime(clip.length * 0.6f, false);
            string afterRoundTrip = Capture(bones);

            float pos;
            float angle;
            ComparePoses(afterRoundTrip, afterFirst, out pos, out angle);
            Log("A2 切走再切回：dPos=" + pos.ToString("F6") + " dAngle=" + angle.ToString("F5")
                + (afterRoundTrip == afterFirst ? "  [完全一致]" : ""));
            Check(pos < 0.0005f && angle < 0.05f,
                "A2 换到另一条 clip 再切回来，姿势回到同一点（dPos=" + pos.ToString("F6") + "）");

            Log("A2 切走瞬间形态键对基准的最大偏差=" + blendAtSwitch.ToString("F5"));
            Check(blendAtSwitch < 0.001f,
                "A2 切换后形态键无残留（最大偏差 " + blendAtSwitch.ToString("F6") + "）");

            // 额外：开关一次组件，形态键也该回到基准。
            previewer.enabled = false;
            float blendAfterDisable = MaxBlendShapeDeviation(blendRenderers, blendBaseline);
            Check(blendAfterDisable < 0.001f,
                "A2 禁用组件后形态键回到基准（最大偏差 " + blendAfterDisable.ToString("F6") + "）");
            previewer.enabled = true;
            // 编辑器里启用不会自动接管，下一段（参照路径）要自己管 Animator，
            // 这里只需确认引用已还原、没有野控制器。
            Check(!previewer.IsPreviewing, "A2 重新启用后不会自动接管 Animator（编辑器语义）");
        }
        else
        {
            Log("A2 SKIP：这个 FBX 里只有一条 clip，无法做换 clip 的残留对比。");
        }

        // ---- B：参照路径（Unity 自己的做法：内存控制器 + Animator.Play/Update） ------
        var controller = new AnimatorController();
        controller.hideFlags = HideFlags.HideAndDontSave;
        controller.AddLayer("preview");
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        stateMachine.hideFlags = HideFlags.HideAndDontSave;
        AnimatorState state = stateMachine.AddState("preview");
        state.motion = clip;
        state.hideFlags = HideFlags.HideAndDontSave;

        // A 阶段结束了：先让组件交出 Animator，再用参照路径接管。
        previewer.enabled = false;

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play(0, 0, 0f);
        animator.Update(0f);

        List<string> posesB = new List<string>();
        for (int f = 0; f < frames; f++)
        {
            float normalized = (float)f / (frames - 1);
            animator.Play(0, 0, normalized);
            animator.Update(0f);
            posesB.Add(Capture(bones));
        }

        Check(Distinct(posesB) > 1, "B 参照路径逐帧姿势在变化（distinct=" + Distinct(posesB) + "）");

        // ---- C：两条路径的姿势差异 --------------------------------------------------
        int worstFrame = -1;
        float worstPosition = 0f;
        float worstAngle = 0f;
        for (int f = 0; f < frames; f++)
        {
            float position;
            float angle;
            ComparePoses(posesA[f], posesB[f], out position, out angle);
            if (position > worstPosition || angle > worstAngle) worstFrame = f;
            if (position > worstPosition) worstPosition = position;
            if (angle > worstAngle) worstAngle = angle;
            Log("C frame " + f + " dPos=" + position.ToString("F5") + " dAngle=" + angle.ToString("F4")
                + (posesA[f] == posesB[f] ? "  [完全一致]" : ""));
        }

        Log("C worstFrame=" + worstFrame + " dPos=" + worstPosition.ToString("F5")
            + "m dAngle=" + worstAngle.ToString("F4") + "deg");
        Check(worstPosition < 0.001f, "C 两条路径位置一致（最大 " + worstPosition.ToString("F6") + "m）");
        Check(worstAngle < 0.1f, "C 两条路径旋转一致（最大 " + worstAngle.ToString("F4") + "deg）");

        // ---- D：还原 -----------------------------------------------------------------
        UnityEngine.Object.DestroyImmediate(controller);

        previewer.enabled = false;
        previewer.enabled = true;
        previewer.SetTime(0f, false);
        Check(previewer.IsPreviewing, "D 重新启用后能再次进入预览");
        previewer.enabled = false;
        Check(!previewer.IsPreviewing, "D 禁用后退出预览");
        Check(animator.avatar == avatar, "D 禁用后 Avatar 被还原");
        Check(animator.runtimeAnimatorController == controller || animator.runtimeAnimatorController == null,
            "D 禁用后未留下野控制器引用（当前=" + (animator.runtimeAnimatorController == null ? "null" : animator.runtimeAnimatorController.name) + "）");

        // 对照：本来有控制器的情形必须还原成原值。
        animator.runtimeAnimatorController = originalController;
        RuntimeAnimatorController before = animator.runtimeAnimatorController;
        previewer.enabled = true;
        previewer.SetTime(0f, false);
        Check(previewer.IsPreviewing, "D 有原控制器时也能进入预览");
        previewer.enabled = false;
        Check(animator.runtimeAnimatorController == before,
            "D 禁用后原控制器被还原（" + (before == null ? "null" : before.name) + "）");

        // ---- E：m_IsActive 残留（不依赖测试 FBX 有没有开关物体的 clip，就地造一条） --------
        {
            var toggleTarget = new GameObject("HoActiveToggleTarget");
            toggleTarget.transform.SetParent(root.transform, false);
            bool activeBefore = toggleTarget.activeSelf;

            AnimationClip toggleClip = BuildActiveToggleClip(toggleTarget);
            Check(toggleClip != null, "E 造出 m_IsActive 曲线（" + (toggleClip == null ? "失败" : toggleClip.name) + "）");

            if (toggleClip != null)
            {
                previewer.SetClip(toggleClip, true);
                previewer.Pause();
                previewer.SetTime(toggleClip.length * 0.99f, false);
                bool actuallyToggled = toggleTarget.activeSelf != activeBefore;
                Log("E 目标物体 activeSelf=" + toggleTarget.activeSelf + "（预期 " + (!activeBefore) + "）");

                if (!actuallyToggled)
                {
                    // 图的输出不一定会应用 GameObject 的 m_IsActive —— 那样这条就没有判别力，
                    // 不能冒充"通过"。如实报出来。
                    Log("E SKIP：PlayableGraph 输出没有应用 m_IsActive，本项无判别力，不计入结论。");
                }
                else
                {
                    Check(actuallyToggled, "E 该 clip 确实关掉了目标物体");

                    // 切回普通 clip：物体必须回到预览前的激活状态，而不是留着上一条的残留。
                    previewer.SetClip(clip, true);
                    Check(toggleTarget.activeSelf == activeBefore,
                        "E 换回普通 clip 后激活状态被还原（activeSelf=" + toggleTarget.activeSelf + "）");

                    // 再来一次，然后退出预览，也要还原。
                    previewer.SetClip(toggleClip, true);
                    previewer.Pause();
                    previewer.SetTime(toggleClip.length * 0.99f, false);
                    previewer.enabled = false;
                    Check(toggleTarget.activeSelf == activeBefore,
                        "E 退出预览后激活状态被还原（activeSelf=" + toggleTarget.activeSelf + "）");
                    previewer.enabled = true;
                }

                UnityEngine.Object.DestroyImmediate(toggleClip);
            }

            UnityEngine.Object.DestroyImmediate(toggleTarget);
        }

        Log("summary passed=" + passed + " failed=" + failed);
    }

    /// 造一条只写 <c>m_IsActive</c> 的 clip：0 秒开、后半段关。
    static AnimationClip BuildActiveToggleClip(GameObject target)
    {
        var clip = new AnimationClip { name = "HoActiveToggleProbe", frameRate = 30f };
        var binding = new EditorCurveBinding
        {
            // 相对预览根物体的路径 —— 组件就是挂在那个根上的。
            path = target.name,
            type = typeof(GameObject),
            propertyName = "m_IsActive"
        };

        var curve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.5001f, 0f),
            new Keyframe(1f, 0f));
        AnimationUtility.SetEditorCurve(clip, binding, curve);
        return clip;
    }

    // ---- 工具 ---------------------------------------------------------------------

    /// 记下所有蒙皮网格的形态键权重，作为"未预览"的基准。
    static void CaptureBlendShapes(GameObject root, out List<SkinnedMeshRenderer> renderers, out List<float[]> weights)
    {
        renderers = new List<SkinnedMeshRenderer>();
        weights = new List<float[]>();
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || renderer.sharedMesh == null) continue;
            int count = renderer.sharedMesh.blendShapeCount;
            if (count <= 0) continue;

            var snapshot = new float[count];
            for (int i = 0; i < count; i++) snapshot[i] = renderer.GetBlendShapeWeight(i);
            renderers.Add(renderer);
            weights.Add(snapshot);
        }
        Log("blendShapeRendererCount=" + renderers.Count
            + " totalShapes=" + TotalShapes(weights));
    }

    static int TotalShapes(List<float[]> weights)
    {
        int total = 0;
        foreach (float[] w in weights) total += w.Length;
        return total;
    }

    /// 当前形态键与基准的最大偏差。
    static float MaxBlendShapeDeviation(List<SkinnedMeshRenderer> renderers, List<float[]> baseline)
    {
        float worst = 0f;
        for (int r = 0; r < renderers.Count; r++)
        {
            SkinnedMeshRenderer renderer = renderers[r];
            if (renderer == null) continue;
            float[] expected = baseline[r];
            for (int i = 0; i < expected.Length; i++)
            {
                float delta = Mathf.Abs(renderer.GetBlendShapeWeight(i) - expected[i]);
                if (delta > worst) worst = delta;
            }
        }
        return worst;
    }

    static int Distinct(List<string> items)
    {
        var set = new HashSet<string>();
        foreach (string s in items) set.Add(s);
        return set.Count;
    }

    /// 把整副骨架编码成一行，便于"完全一致"这一步做字符串比较。
    static string Capture(List<Transform> bones)
    {
        var sb = new StringBuilder(bones.Count * 40);
        for (int i = 0; i < bones.Count; i++)
        {
            Transform b = bones[i];
            Vector3 p = b.position;
            Quaternion q = b.localRotation;
            sb.Append(p.x.ToString("F5")).Append(',')
              .Append(p.y.ToString("F5")).Append(',')
              .Append(p.z.ToString("F5")).Append('|')
              .Append(q.x.ToString("F5")).Append(',')
              .Append(q.y.ToString("F5")).Append(',')
              .Append(q.z.ToString("F5")).Append(',')
              .Append(q.w.ToString("F5")).Append(';');
        }
        return sb.ToString();
    }

    /// 逐骨骼比较两行快照，返回最大世界位置误差与最大本地旋转夹角。
    static void ComparePoses(string a, string b, out float maxPosition, out float maxAngle)
    {
        maxPosition = 0f;
        maxAngle = 0f;
        string[] left = a.Split(';');
        string[] right = b.Split(';');
        int count = Mathf.Min(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            if (left[i].Length == 0 || right[i].Length == 0) continue;
            string[] lp = left[i].Split('|');
            string[] rp = right[i].Split('|');
            if (lp.Length != 2 || rp.Length != 2) continue;

            float[] l = Parse(lp[0]);
            float[] r = Parse(rp[0]);
            float position = Vector3.Distance(new Vector3(l[0], l[1], l[2]), new Vector3(r[0], r[1], r[2]));
            if (position > maxPosition) maxPosition = position;

            float[] lq = Parse(lp[1]);
            float[] rq = Parse(rp[1]);
            float angle = Quaternion.Angle(new Quaternion(lq[0], lq[1], lq[2], lq[3]), new Quaternion(rq[0], rq[1], rq[2], rq[3]));
            if (angle > maxAngle) maxAngle = angle;
        }
    }

    static float[] Parse(string csv)
    {
        string[] parts = csv.Split(',');
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            float v;
            values[i] = float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
        return values;
    }
}
