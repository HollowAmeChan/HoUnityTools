using System;
using System.IO;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;

// Executes before the production 30000-order constraints; validates after them.
[DefaultExecutionOrder(31000)]
public class ConstraintPlayProbe:MonoBehaviour
{
    public string reportPath;
    private HoPositionConstraint rest,animated,manual,fixedConstraint,updateConstraint;
    private Transform source,target,animatedTarget,manualTarget,fixedTarget,updateTarget;
    private int frames;
    private void Start()
    {
        ConstraintChecks.Report=JsonUtility.FromJson<ConstraintReport>(File.ReadAllText(reportPath));
        source=new GameObject("Play source").transform;target=new GameObject("Play target").transform;
        animatedTarget=new GameObject("Animated target").transform;manualTarget=new GameObject("Manual target").transform;
        fixedTarget=new GameObject("Fixed target").transform;updateTarget=new GameObject("Update target").transform;
        rest=Make(target,HoTransformConstraintPhase.LateUpdate);rest.Weight=.5f;
        animated=Make(animatedTarget,HoTransformConstraintPhase.LateUpdate);animated.Input=HoTransformConstraintInput.Animated;animated.Weight=.5f;
        manual=Make(manualTarget,HoTransformConstraintPhase.Manual);
        fixedConstraint=Make(fixedTarget,HoTransformConstraintPhase.FixedUpdate);
        updateConstraint=Make(updateTarget,HoTransformConstraintPhase.Update);
        var driver=new GameObject("Early source driver").AddComponent<ConstraintInputDriver>();driver.source=source;driver.animated=animatedTarget;
    }
    private HoPositionConstraint Make(Transform driven,HoTransformConstraintPhase phase)
    {
        var c=new GameObject("External play host "+phase).AddComponent<HoPositionConstraint>();c.TargetTransform=driven;c.Phase=phase;
        c.Sources.Add(new HoTransformConstraintSource{transform=source});ConstraintChecks.Activate(c);return c;
    }
    private void LateUpdate()
    {
        if(Time.time<.12f)return;
        if(++frames<3)return;
        try
        {
            ConstraintChecks.Near(target.position,source.position*.5f,"Automatic LateUpdate with stable baseline frame "+frames);
            ConstraintChecks.Near(animatedTarget.position,Vector3.Lerp(new Vector3(2,4,6),source.position,.5f),"Animated frame input frame "+frames);
            ConstraintChecks.Near(updateTarget.position,source.position,"Automatic Update frame "+frames);
            ConstraintChecks.Near(fixedTarget.position,new Vector3(13,14,15),"Automatic FixedUpdate frame "+frames);
            ConstraintChecks.Near(manualTarget.position,Vector3.zero,"Manual phase is not automatic frame "+frames);
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
