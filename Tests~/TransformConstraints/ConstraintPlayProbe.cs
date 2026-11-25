using System;
using System.IO;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;

// Validate after the production 30000-order constraints.
[DefaultExecutionOrder(31000)]
public class ConstraintPlayProbe:MonoBehaviour
{
    public string reportPath;
    public GameObject prefab;
    public HoPositionConstraint sceneConstraint;
    private HoPositionConstraint rest,animated,manual,fixedConstraint,updateConstraint;
    private Transform source,target,animatedTarget,manualTarget,fixedTarget,updateTarget;
    private Transform parentTarget, aimTarget;
    private int frames;
    private void Start()
    {
        ConstraintChecks.Report=JsonUtility.FromJson<ConstraintReport>(File.ReadAllText(reportPath));
        ConstraintChecks.Run();
        ConstraintChecks.Case("Prefab in Play Mode", () =>
        {
            var instance = Instantiate(prefab);
            try
            {
                var constraint = instance.GetComponentInChildren<HoPositionConstraint>();
                ConstraintChecks.Check(constraint.TargetTransform.IsChildOf(instance.transform) &&
                    constraint.Sources[0].transform.IsChildOf(instance.transform), "Prefab references remap");
                ConstraintChecks.Eval(constraint);
                ConstraintChecks.Near(constraint.TargetTransform.localPosition, new Vector3(3, 4, 5), "Serialized offset works in Play Mode");
                constraint.RestoreRestPose();
                ConstraintChecks.Near(constraint.TargetTransform.localPosition, new Vector3(2, 3, 4), "Runtime restore works");

                var prefabConstraint = prefab.GetComponentInChildren<HoPositionConstraint>();
                constraint.TargetTransform = prefabConstraint.TargetTransform;
                ConstraintChecks.Activate(constraint);
                ConstraintChecks.Check(!constraint.EvaluateNow(), "Runtime component cannot write to a non-playing target");
                ConstraintChecks.Near(prefabConstraint.TargetTransform.localPosition, new Vector3(2, 3, 4), "Prefab asset stays untouched");
            }
            finally
            {
                DestroyImmediate(instance);
            }
        });
        source=new GameObject("Play source").transform;target=new GameObject("Play target").transform;
        animatedTarget=new GameObject("Animated target").transform;manualTarget=new GameObject("Manual target").transform;
        fixedTarget=new GameObject("Fixed target").transform;updateTarget=new GameObject("Update target").transform;
        rest=Make<HoPositionConstraint>(target,HoTransformConstraintPhase.LateUpdate,target.gameObject);rest.Weight=.5f;
        Make<HoRotationConstraint>(target,HoTransformConstraintPhase.LateUpdate,target.gameObject);
        Make<HoScaleConstraint>(target,HoTransformConstraintPhase.LateUpdate,target.gameObject);
        source.rotation=Quaternion.Euler(17f,33f,49f);
        source.localScale=new Vector3(2f,3f,4f);
        animated=Make<HoPositionConstraint>(animatedTarget,HoTransformConstraintPhase.LateUpdate);animated.Input=HoTransformConstraintInput.Animated;animated.Weight=.5f;
        manual=Make<HoPositionConstraint>(manualTarget,HoTransformConstraintPhase.Manual);
        fixedConstraint=Make<HoPositionConstraint>(fixedTarget,HoTransformConstraintPhase.FixedUpdate);
        updateConstraint=Make<HoPositionConstraint>(updateTarget,HoTransformConstraintPhase.Update);
        parentTarget = new GameObject("Parent target").transform;
        aimTarget = new GameObject("Aim target").transform;
        Make<HoParentConstraint>(parentTarget, HoTransformConstraintPhase.LateUpdate);
        Make<HoAimConstraint>(aimTarget, HoTransformConstraintPhase.LateUpdate);
        var driver=new GameObject("Early source driver").AddComponent<ConstraintInputDriver>();driver.source=source;driver.animated=animatedTarget;
    }
    private T Make<T>(Transform driven,HoTransformConstraintPhase phase,GameObject host=null) where T:HoTransformConstraint
    {
        var c=(host!=null?host:new GameObject("External play host "+phase)).AddComponent<T>();c.TargetTransform=driven;c.Phase=phase;
        c.Sources.Add(new HoTransformConstraintSource{transform=source});ConstraintChecks.Activate(c);return c;
    }
    private void LateUpdate()
    {
        if(Time.time<.12f)return;
        if(++frames<3)return;
        try
        {
            ConstraintChecks.Near(target.position,source.position*.5f,"Automatic LateUpdate with stable baseline frame "+frames);
            ConstraintChecks.Near(target.rotation,source.rotation,"Same-object rotation writer frame "+frames);
            ConstraintChecks.Near(target.localScale,source.localScale,"Same-object scale writer frame "+frames);
            ConstraintChecks.Near(animatedTarget.position,Vector3.Lerp(new Vector3(2,4,6),source.position,.5f),"Animated frame input frame "+frames);
            ConstraintChecks.Near(updateTarget.position,source.position,"Automatic Update frame "+frames);
            ConstraintChecks.Near(fixedTarget.position,new Vector3(13,14,15),"Automatic FixedUpdate frame "+frames);
            ConstraintChecks.Near(manualTarget.position,Vector3.zero,"Manual phase is not automatic frame "+frames);
            ConstraintChecks.Near(parentTarget.position, source.position, "Automatic Parent position frame " + frames);
            ConstraintChecks.Near(parentTarget.rotation, source.rotation, "Automatic Parent rotation frame " + frames);
            ConstraintChecks.Near(aimTarget.forward, (source.position - aimTarget.position).normalized, "Automatic Aim frame " + frames);
            ConstraintChecks.Near(sceneConstraint.TargetTransform.localPosition, new Vector3(3, 4, 5),
                "Preconfigured scene constraint starts on entering Play Mode");
            if(frames<9)return;
            ConstraintChecks.Eval(manual);ConstraintChecks.Near(manualTarget.position,source.position,"Explicit manual evaluation");
            Finish();
        }
        catch(Exception error){ConstraintChecks.Report.failures.Add("PlayMode: "+error);Finish();}
    }
    private void Finish()
    {
        File.WriteAllText(reportPath,JsonUtility.ToJson(ConstraintChecks.Report,true));
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(ConstraintChecks.Report.failures.Count==0?0:1);
#endif
    }
}

[DefaultExecutionOrder(-100)]
public class ConstraintInputDriver:MonoBehaviour
{
    public Transform source,animated;
    private void Update(){source.position=new Vector3(Time.frameCount,2,3);animated.position=new Vector3(2,4,6);}
    private void FixedUpdate(){source.position=new Vector3(13,14,15);}
}
