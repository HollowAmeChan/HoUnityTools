using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Hollow.HoUnityTools.Constraints
{
    public sealed partial class HoBsCorrective
    {
        private struct CorrectivePosePass { }
        private static readonly HashSet<HoBsCorrective> Active = new HashSet<HoBsCorrective>();
        private static readonly List<HoBsCorrective> FrameComponents = new List<HoBsCorrective>();
        private static bool finalPoseInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFinalPoseClock()
        {
            finalPoseInstalled = false;
            Active.RemoveWhere(component => component == null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallFinalPosePass()
        {
            if (finalPoseInstalled || !Application.isPlaying) return;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertPass(ref loop))
            {
                Debug.LogWarning("HoBsCorrective: PlayerLoop 缺少 PostLateUpdate，无法执行最终修型阶段。");
                return;
            }
            PlayerLoop.SetPlayerLoop(loop);
            finalPoseInstalled = true;
        }

        private static bool InsertPass(ref PlayerLoopSystem loop)
        {
            if (loop.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
            {
                var systems = new List<PlayerLoopSystem>();
                if (loop.subSystemList != null)
                    foreach (PlayerLoopSystem system in loop.subSystemList)
                        if (system.type != typeof(CorrectivePosePass)) systems.Add(system);
                int insertion = 0;
                for (int i = 0; i < systems.Count; i++)
                    if (systems[i].type?.FullName == "Hollow.HoUnityTools.RigConstraints.HoAuxRig+FinalPosePass")
                        insertion = i + 1;
                // HoAux also inserts at PostLateUpdate's start: if installed after us,
                // it still naturally precedes this pass. Both run before skinned meshes.
                systems.Insert(insertion, new PlayerLoopSystem
                { type = typeof(CorrectivePosePass), updateDelegate = EvaluateComponents });
                loop.subSystemList = systems.ToArray();
                return true;
            }
            if (loop.subSystemList == null) return false;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = loop.subSystemList[i];
                if (!InsertPass(ref child)) continue;
                loop.subSystemList[i] = child;
                return true;
            }
            return false;
        }

        private static void EvaluateComponents()
        {
            if (!Application.isPlaying) return;
            FrameComponents.Clear();
            FrameComponents.AddRange(Active);
            foreach (HoBsCorrective component in FrameComponents)
                if (component != null && Application.IsPlaying(component.gameObject)) component.EvaluateNow();
        }
    }
}
