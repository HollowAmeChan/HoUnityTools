using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Hollow.HoUnityTools.RigConstraints
{
    public sealed partial class HoAuxRig
    {
        private struct FinalPosePass { }
        private static readonly HashSet<HoAuxRig> FinalPoseRigs = new HashSet<HoAuxRig>();
        private static bool finalPoseInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFinalPoseClock()
        {
            finalPoseInstalled = false;
            // Keep live registrations when Enter Play Mode skips scene reload.
            FinalPoseRigs.RemoveWhere(rig => rig == null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallFinalPosePass()
        {
            if (finalPoseInstalled || !Application.isPlaying) return;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertFinalPosePass(ref loop))
            {
                Debug.LogWarning("HoAuxRig: PlayerLoop 中缺少 PostLateUpdate，无法执行最终姿态阶段。");
                return;
            }
            PlayerLoop.SetPlayerLoop(loop);
            finalPoseInstalled = true;
        }

        private static bool InsertFinalPosePass(ref PlayerLoopSystem loop)
        {
            if (loop.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
            {
                var systems = new List<PlayerLoopSystem>();
                systems.Add(new PlayerLoopSystem
                {
                    type = typeof(FinalPosePass),
                    updateDelegate = EvaluateFinalPoseRigs
                });
                if (loop.subSystemList != null)
                    foreach (PlayerLoopSystem system in loop.subSystemList)
                        if (system.type != typeof(FinalPosePass)) systems.Add(system);
                // A host stage after ordinary LateUpdate / PreLateUpdate
                // simulations, before the engine updates skinned meshes.
                loop.subSystemList = systems.ToArray();
                return true;
            }
            if (loop.subSystemList == null) return false;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = loop.subSystemList[i];
                if (!InsertFinalPosePass(ref child)) continue;
                loop.subSystemList[i] = child;
                return true;
            }
            return false;
        }

        private static void EvaluateFinalPoseRigs()
        {
            if (!Application.isPlaying) return;
            // Registration can change if an owner is disabled by another rig.
            var rigs = new List<HoAuxRig>(FinalPoseRigs);
            foreach (HoAuxRig rig in rigs)
                if (rig != null && rig.isActiveAndEnabled && Application.IsPlaying(rig.gameObject) &&
                    rig.updateMode == UpdateMode.LateUpdate && rig.directExecutionPhase == DirectExecutionPhase.FinalPose &&
                    rig.directOperations.Count > 0)
                    rig.EvaluateDirectStack();
        }
    }
}
