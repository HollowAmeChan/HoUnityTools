using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;

namespace Hollow.HoUnityTools.Editor.TransformConstraints
{
    /// <summary>Preview values are temporary; save/play/reload restores authoring data.</summary>
    [InitializeOnLoad]
    public static class HoTransformConstraintPreview
    {
        private sealed class Saved
        {public HoTransformLocalPose pose;public HoTransformChannels channels;}
        private static readonly Dictionary<Transform,Saved> saved=new Dictionary<Transform,Saved>();
        static HoTransformConstraintPreview()
        {
            EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload+=RestoreAll;
            EditorApplication.quitting+=RestoreAll;
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingEditMode)RestoreAll();};
            EditorSceneManager.sceneSaving+=(scene,path)=>RestoreAll();
            PrefabStage.prefabSaving+=root=>RestoreAll();
        }
        public static void Tick()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            var constraints=HoTransformConstraintScheduler.Registered.Where(c=>c.isActiveAndEnabled&&c.ConstraintActive&&c.PreviewInEditMode&&
                c.TargetTransform!=null&&!EditorUtility.IsPersistent(c)&&!EditorUtility.IsPersistent(c.TargetTransform)&&
                c.gameObject.scene==c.TargetTransform.gameObject.scene).ToList();
            var desired=new Dictionary<Transform,HoTransformChannels>();
            foreach(var c in constraints)
            {desired.TryGetValue(c.TargetTransform,out HoTransformChannels mask);desired[c.TargetTransform]=mask|c.WrittenChannels;}
            foreach(var pair in saved.ToArray())
            {
                if(pair.Key==null){saved.Remove(pair.Key);continue;}
                desired.TryGetValue(pair.Key,out HoTransformChannels mask);
                pair.Value.pose.Apply(pair.Key,pair.Value.channels&~mask);
                if(mask==0)saved.Remove(pair.Key);
            }
            foreach(var pair in desired)
            {
                if(!saved.TryGetValue(pair.Key,out Saved state))
                {state=new Saved {pose=HoTransformLocalPose.Read(pair.Key)};saved.Add(pair.Key,state);}
                state.pose.CopyFrom(HoTransformLocalPose.Read(pair.Key),pair.Value&~state.channels);
                state.channels=pair.Value;
                // Animated-input preview also starts from a stable editing snapshot.
                state.pose.Apply(pair.Key,state.channels);
            }
            HoTransformConstraintScheduler.Evaluate(constraints);
            if(saved.Count>0)SceneView.RepaintAll();
        }
        public static void RestoreAll()
        {
            foreach(var pair in saved)if(pair.Key!=null)pair.Value.pose.Apply(pair.Key,pair.Value.channels);
            saved.Clear();
        }
    }
}
