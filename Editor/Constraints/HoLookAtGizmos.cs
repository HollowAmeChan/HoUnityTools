using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 注视约束的 Scene 视图可视化：把"纯数值"变成一眼能看懂的几何。
    /// 画在编辑器程序集里，所以可以用 Handles 画文字标签。
    ///
    /// 画的东西：
    ///   绿箭头 = 参考系前方（角色的面向，所有角度都相对它）
    ///   蓝箭头 = 参考系上方
    ///   白色线框 = 头部限位框（左右 ±yaw、上下 ±pitch，投影到 1 米处）
    ///   黄线   = 总角度方向（目标），黄球 = 目标点
    ///   青线   = 头部**估计**方向（我们让 Unity 看哪 × 它施加的比例 + 模型偏差 trim）
    ///   紫线   = 头估计 + 眼睛残余 = 实际目光；它和黄色目标线重合就说明"指哪看哪"
    ///   青虚线 = 鼠标射线（从相机原点穿过鼠标像素）：黄球必须落在这条线上
    /// </summary>
    internal static class HoLookAtGizmos
    {
        private static readonly Color FrameColor = new Color(0.35f, 0.95f, 0.45f, 1.0f);
        private static readonly Color UpColor = new Color(0.45f, 0.65f, 1.0f, 1.0f);
        private static readonly Color LimitColor = new Color(1.0f, 1.0f, 1.0f, 0.45f);
        private static readonly Color TargetColor = new Color(1.0f, 0.82f, 0.25f, 1.0f);
        private static readonly Color TargetOutsideColor = new Color(1.0f, 0.40f, 0.30f, 1.0f);
        private static readonly Color HeadColor = new Color(0.25f, 0.95f, 0.70f, 1.0f);
        private static readonly Color GazeColor = new Color(0.80f, 0.55f, 1.0f, 1.0f);
        private static readonly Color PointerColor = new Color(0.35f, 0.90f, 0.95f, 1.0f);

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void Draw(HoLookAtConstraint constraint, GizmoType gizmoType)
        {
            if (constraint == null || !constraint.DrawGizmosEnabled || !constraint.isActiveAndEnabled)
            {
                return;
            }

            Vector3 pivot = constraint.Pivot;
            Vector3 forward = constraint.ReferenceForward.normalized;
            Vector3 up = constraint.ReferenceUp.normalized;
            if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
            {
                return;
            }

            const float FrameLength = 0.35f;
            const float AimLength = 1.0f;

            Handles.color = FrameColor;
            Handles.DrawLine(pivot, pivot + forward * FrameLength);
            Handles.Label(pivot + forward * FrameLength, new GUIContent("前"), LabelStyle(FrameColor));

            Handles.color = UpColor;
            Handles.DrawLine(pivot, pivot + up * FrameLength * 0.7f);
            Handles.Label(pivot + up * FrameLength * 0.7f, new GUIContent("上"), LabelStyle(UpColor));

            DrawLimitFrame(pivot, forward, up, constraint.HeadLimitYaw, constraint.HeadLimitPitch, AimLength);

            HoLookAtDebug debug = constraint.GetDebug();
            bool inside = Mathf.Abs(debug.targetYaw) <= constraint.HeadLimitYaw + 0.01f
                          && Mathf.Abs(debug.targetPitch) <= constraint.HeadLimitPitch + 0.01f;

            Vector3 headDirection = HoLookAtSolver.DirectionFromAngles(forward, up, debug.headEstimateYaw, debug.headEstimatePitch);
            Vector3 gazeDirection = HoLookAtSolver.DirectionFromAngles(forward, up, debug.headEstimateYaw + debug.eyeYaw, debug.headEstimatePitch + debug.eyePitch);
            Vector3 targetDirection = HoLookAtSolver.DirectionFromAngles(forward, up, debug.targetYaw, debug.targetPitch);

            // 目标方向：超出限位就变红，"头转不过去"一眼可见
            Handles.color = inside ? TargetColor : TargetOutsideColor;
            Handles.DrawLine(pivot, pivot + targetDirection * AimLength);
            Handles.Label(
                pivot + targetDirection * AimLength,
                new GUIContent("总 " + debug.targetYaw.ToString("0") + "° / " + debug.targetPitch.ToString("0") + "°"),
                LabelStyle(Handles.color));

            // 头部实际方向（不是命令值）：Unity 的 IK 只做近似，这里画的是骨骼真实朝向
            Handles.color = HeadColor;
            Handles.DrawLine(pivot, pivot + headDirection * AimLength * 0.92f);
            Handles.Label(
                pivot + headDirection * AimLength * 0.92f + up * 0.04f,
                new GUIContent("头 " + debug.headEstimateYaw.ToString("0") + "° / " + debug.headEstimatePitch.ToString("0") + "°"),
                LabelStyle(HeadColor));

            // 目光 = 头部实际 + 眼睛残余。误差大时它和黄色目标线会明显分开
            Handles.color = GazeColor;
            Handles.DrawLine(pivot, pivot + gazeDirection * AimLength * 1.05f);
            Handles.Label(
                pivot + gazeDirection * AimLength * 1.05f - up * 0.05f,
                new GUIContent("目光 " + (debug.headEstimateYaw + debug.eyeYaw).ToString("0") + "° / " + (debug.headEstimatePitch + debug.eyePitch).ToString("0") + "°"),
                LabelStyle(GazeColor));

            // 目标点（跟随物体 / 场景点模式）
            if (debug.hasTargetPoint)
            {
                Handles.color = debug.hasTarget ? TargetColor : new Color(0.6f, 0.6f, 0.6f, 1.0f);
                Handles.SphereHandleCap(0, debug.targetPoint, Quaternion.identity, HandleUtility.GetHandleSize(debug.targetPoint) * 0.12f, EventType.Repaint);
                if (!inside)
                {
                    // 超出限位时画一条从目标点到限位方向的示意
                    Handles.color = TargetOutsideColor;
                    Handles.DrawDottedLine(debug.targetPoint, pivot + targetDirection * AimLength, 4.0f);
                }
            }

            // 鼠标射线：目标点必须落在这条线上，否则就是"屏幕→世界"的换算错了
            if (constraint.HasPointerRay)
            {
                Ray pointerRay = constraint.LastPointerRay;
                Vector3 rayEnd = debug.hasTargetPoint ? debug.targetPoint : pointerRay.origin + pointerRay.direction * 5.0f;
                Handles.color = PointerColor;
                Handles.DrawDottedLine(pointerRay.origin, rayEnd, 4.0f);
                Handles.SphereHandleCap(0, pointerRay.origin, Quaternion.identity, HandleUtility.GetHandleSize(pointerRay.origin) * 0.08f, EventType.Repaint);
                Handles.Label(pointerRay.origin, new GUIContent("鼠标射线"), LabelStyle(PointerColor));
            }

            Handles.color = new Color(1.0f, 1.0f, 1.0f, 0.2f);
            Handles.SphereHandleCap(0, pivot, Quaternion.identity, HandleUtility.GetHandleSize(pivot) * 0.05f, EventType.Repaint);
        }

        /// <summary>头部限位框：以参考系为基准，左右 ±yaw / 上下 ±pitch 的四角 + 四条棱。</summary>
        private static void DrawLimitFrame(Vector3 pivot, Vector3 forward, Vector3 up, float yawLimit, float pitchLimit, float length)
        {
            if (yawLimit <= 0.0f || pitchLimit <= 0.0f)
            {
                return;
            }

            const int Steps = 24;
            Handles.color = LimitColor;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= Steps; i++)
            {
                float angle = i / (float)Steps * Mathf.PI * 2.0f;
                float yaw = Mathf.Cos(angle) * yawLimit;
                float pitch = Mathf.Sin(angle) * pitchLimit;
                Vector3 point = pivot + HoLookAtSolver.DirectionFromAngles(forward, up, yaw, pitch) * length;
                if (i > 0)
                {
                    Handles.DrawLine(previous, point);
                }

                if (i % 6 == 0)
                {
                    Handles.DrawLine(pivot, point);
                }

                previous = point;
            }
        }

        private static GUIStyle LabelStyle(Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            style.fontStyle = FontStyle.Bold;
            return style;
        }
    }
}
