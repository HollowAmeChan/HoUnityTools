using System;
using System.Collections.Generic;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;

[Serializable] public class ConstraintReport
{
    public List<string> passed=new List<string>();
    public List<string> failures=new List<string>();
}

public static class ConstraintChecks
{
    public static ConstraintReport Report=new ConstraintReport();
    private static Transform root;
    public static void Check(bool condition,string name)
    {if(condition)Report.passed.Add(name);else throw new Exception(name);}
    public static void Near(Vector3 actual,Vector3 expected,string name,float tolerance=0.0001f)
    {Check(Vector3.Distance(actual,expected)<tolerance,name+" actual="+actual.ToString("F5")+" expected="+expected.ToString("F5"));}
    public static void Near(Quaternion actual,Quaternion expected,string name)
    {Check(Quaternion.Angle(actual,expected)<.05f,name+" angle="+Quaternion.Angle(actual,expected));}
    public static void Case(string name,Action test)
    {
        root=new GameObject(name).transform;
        try{test();}catch(Exception error){Report.failures.Add(name+": "+error);}
        finally{UnityEngine.Object.DestroyImmediate(root.gameObject);}
    }
    public static Transform Obj(string name,Vector3 position=default(Vector3),Transform parent=null)
    {var t=new GameObject(name).transform;t.SetParent(parent==null?root:parent,false);t.localPosition=position;return t;}
    public static T Constraint<T>(Transform target,params Transform[] sources) where T:HoTransformConstraint
    {
        var c=Obj("External Host").gameObject.AddComponent<T>();c.TargetTransform=target;
        foreach(var source in sources)c.Sources.Add(new HoTransformConstraintSource {transform=source});
        return c;
    }
    public static void Activate(HoTransformConstraint c,bool keep=false)
    {Check(c.Activate(keep,out string error),"Activate "+c.Kind+": "+error);}
    public static void Eval(params HoTransformConstraint[] constraints)
    {
        HoTransformConstraintScheduler.Evaluate(constraints);
        foreach(var c in constraints)Check(c.LastError==null,"Evaluate "+c.Kind+": "+c.LastError);
    }
    public static void Run()
    {
        Case("Explicit target and external host",()=>{
            var c=Obj("Host",new Vector3(90,80,70)).gameObject.AddComponent<HoPositionConstraint>();
            c.Sources.Add(new HoTransformConstraintSource {transform=Obj("source",Vector3.one)});
            Check(!c.Activate(false,out _),"Missing explicit target is rejected");
            var target=Obj("target");c.TargetTransform=target;Activate(c);Eval(c);
            Near(target.position,Vector3.one,"Explicit target follows");Near(c.transform.position,new Vector3(90,80,70),"Host is untouched");
        });
        Case("Normalized position and channel preservation",()=>{
            var target=Obj("target");target.rotation=Quaternion.Euler(17,24,39);target.localScale=new Vector3(2,3,4);
            var rotation=target.rotation;var a=Obj("a",new Vector3(2,0,0));var b=Obj("b",new Vector3(10,4,0));
            var c=Constraint<HoPositionConstraint>(target,a,b);c.Sources[0].weight=2;c.Sources[1].weight=6;Activate(c);Eval(c);
            Near(target.position,new Vector3(8,3,0),"Position weights normalize");Near(target.rotation,rotation,"Position preserves rotation");Near(target.localScale,new Vector3(2,3,4),"Position preserves scale");
            c.enabled=false;target.position=Vector3.zero;HoTransformConstraintScheduler.Evaluate(new[]{c});Near(target.position,Vector3.zero,"Disabled component does not write");
        });
        Case("Captured source-local position offsets",()=>{
            var target=Obj("target",new Vector3(4,2,1));var source=Obj("source",new Vector3(1,-1,2));
            source.rotation=Quaternion.Euler(10,30,5);source.localScale=new Vector3(2,3,4);
            var c=Constraint<HoPositionConstraint>(target,source);var initial=target.position;Activate(c,true);Eval(c);Near(target.position,initial,"Keep offset does not snap");
            var offset=c.Sources[0].positionOffset;source.rotation=Quaternion.Euler(-5,70,25);source.localScale=new Vector3(3,4,5);source.position+=Vector3.one;
            Eval(c);Near(target.position,source.TransformPoint(offset),"Offset follows source rotation and scale");
        });
        Case("Two-source rotation and Parent channels",()=>{
            var a=Obj("a",new Vector3(2,1,0));var b=Obj("b",new Vector3(6,3,0));
            a.rotation=Quaternion.Euler(20,-40,10);b.rotation=Quaternion.Euler(-30,100,35);
            var target=Obj("target");target.localScale=Vector3.one*3;
            var c=Constraint<HoParentConstraint>(target,a,b);c.Sources[0].weight=1;c.Sources[1].weight=3;Activate(c);Eval(c);
            Near(target.position,new Vector3(5,2.5f,0),"Parent weighted position");Near(target.rotation,Quaternion.Slerp(a.rotation,b.rotation,.75f),"Parent two-source Slerp");Near(target.localScale,Vector3.one*3,"Parent does not inherit scale");
            var rotationOnly=Constraint<HoRotationConstraint>(Obj("rotation only"),a,b);Activate(rotationOnly);Eval(rotationOnly);
            Near(rotationOnly.TargetTransform.rotation,Quaternion.Slerp(a.rotation,b.rotation,.5f),"Rotation component");
        });
        Case("Stable rest baseline and root motion",()=>{
            var parent=Obj("parent");var target=Obj("target",new Vector3(2,0,0),parent);var source=Obj("source",new Vector3(10,0,0));
            var c=Constraint<HoPositionConstraint>(target,source);Activate(c);c.Weight=.25f;
            for(int i=0;i<20;i++)Eval(c);Near(target.position,new Vector3(4,0,0),"Rest baseline does not accumulate");
            parent.position=new Vector3(0,4,0);parent.rotation=Quaternion.Euler(0,0,90);Eval(c);
            Near(target.position,Vector3.Lerp(parent.TransformPoint(new Vector3(2,0,0)),source.position,.25f),"Rest baseline follows root");
            c.Weight=0;target.position=Vector3.one*8;Eval(c);Near(target.position,Vector3.one*8,"Zero overall weight does not write");
        });
        Case("Local inputs and position axes",()=>{
            var p1=Obj("p1",Vector3.one*100);var p2=Obj("p2",Vector3.one*200);
            var source=Obj("source",new Vector3(7,8,9),p1);var target=Obj("target",new Vector3(1,2,3),p2);
            var c=Constraint<HoPositionConstraint>(target,source);c.Space=HoTransformConstraintSpace.Local;c.PositionAxes=HoTransformAxes.X;Activate(c);Eval(c);
            Near(target.localPosition,new Vector3(7,2,3),"Local source values and X mask");
        });
        Case("World scale under rotated nonuniform parent",()=>{
            var parent=Obj("parent");parent.localScale=new Vector3(2,3,4);parent.rotation=Quaternion.Euler(20,30,15);
            var target=Obj("target",default(Vector3),parent);target.localRotation=Quaternion.Euler(13,37,-16);
            var source=Obj("source");source.localScale=new Vector3(5,6,7);
            var c=Constraint<HoScaleConstraint>(target,source);Activate(c);Eval(c);
            Near(target.lossyScale,source.lossyScale,"World scale reproduces axis magnitudes",.001f);
            for(int i=1;i<=12;i++)
            {
                parent.localScale=new Vector3(1+i*.1f,3+i*.2f,2+i*.15f);parent.rotation=Quaternion.Euler(i*3,20+i*2,-i);
                target.localRotation=Quaternion.Euler(i*2,-i*4,17+i);source.localScale=new Vector3(5+i*.1f,6-i*.1f,7+i*.2f);
                Eval(c);Near(target.lossyScale,source.lossyScale,"Changing sheared parent world scale "+i,.001f);
            }
            Activate(c,true);var captured=target.localScale;Eval(c);Near(target.localScale,captured,"World scale offset capture keeps pose",.001f);
            var before=target.localScale;parent.localScale=Vector3.zero;HoTransformConstraintScheduler.Evaluate(new[]{c});
            Check(c.LastError!=null,"Degenerate parent scale rejected");Near(target.localScale,before,"Failed scale does not write");
        });
        Case("Atomic failed offset recapture",()=>{
            var target=Obj("target",Vector3.one);var source=Obj("source");var c=Constraint<HoScaleConstraint>(target,source);Activate(c,true);
            var old=c.Sources[0].scaleMultiplier;var bound=c.RestPose;var replacement=Obj("zero");replacement.localScale=Vector3.zero;c.Sources[0].transform=replacement;
            Check(!c.Activate(true,out _),"Zero scale capture rejected");Near(c.Sources[0].scaleMultiplier,old,"Failed capture preserves offset");Near(c.RestPose.scale,bound.scale,"Failed capture preserves binding");
            Check(!c.ValidateConfiguration(out _),"Replaced bound source requires recapture");
        });
        Case("Aim axes, up object and degeneracy",()=>{
            var target=Obj("target");var source=Obj("point",new Vector3(4,1,2));var up=Obj("up",new Vector3(0,3,0));
            var c=Constraint<HoAimConstraint>(target,source);c.AimAxis=HoSignedAxis.PositiveX;c.UpAxis=HoSignedAxis.NegativeZ;c.UpMode=HoAimUpMode.ObjectPosition;c.UpObject=up;Activate(c);Eval(c);
            Near(target.right,(source.position-target.position).normalized,"Aim signed local axis");
            var projected=Vector3.ProjectOnPlane(up.position-target.position,target.right).normalized;Near(-target.forward,projected,"Aim object Up orientation");
            var before=target.rotation;source.position=target.position;HoTransformConstraintScheduler.Evaluate(new[]{c});Check(c.LastError!=null,"Coincident aim rejected");Near(target.rotation,before,"Degenerate aim preserves rotation");
            source.position=Vector3.up;up.position=Vector3.up*2;HoTransformConstraintScheduler.Evaluate(new[]{c});Check(c.LastError!=null,"Collinear Up rejected");
        });
        Case("Aim maintained offset",()=>{
            var target=Obj("target");target.rotation=Quaternion.Euler(10,25,15);var source=Obj("point",new Vector3(3,1,4));var c=Constraint<HoAimConstraint>(target,source);
            var before=target.rotation;Activate(c,true);Eval(c);Near(target.rotation,before,"Aim capture keeps initial orientation");
            source.position=new Vector3(-2,1,5);Eval(c);Check(Quaternion.Angle(target.rotation,before)>10,"Aim responds after capture");
        });
        Case("Aim weighted target, signed axes and Up rotation",()=>{
            var target=Obj("target");var source=Obj("a",new Vector3(4,1,2));var second=Obj("b",new Vector3(-1,3,8));var up=Obj("up");up.rotation=Quaternion.Euler(25,10,13);
            var c=Constraint<HoAimConstraint>(target,source,second);c.Sources[1].weight=3;c.UpMode=HoAimUpMode.ObjectRotation;c.UpObject=up;c.WorldUp=Vector3.up;
            Vector3[] axes={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            for(int a=0;a<6;a++)for(int b=0;b<6;b++)
            {
                if(a/2==b/2)continue;c.AimAxis=(HoSignedAxis)a;c.UpAxis=(HoSignedAxis)b;Activate(c);Eval(c);
                var direction=(Vector3.Lerp(source.position,second.position,.75f)-target.position).normalized;
                Near(target.rotation*axes[a],direction,"Signed Aim axis pair "+a+","+b);
                Near(target.rotation*axes[b],Vector3.ProjectOnPlane(up.up,direction).normalized,"Signed Up axis pair "+a+","+b);
            }
        });
        Case("External dependency chain and ancestor inputs",()=>{
            var source=Obj("source",Vector3.one*4);var mid=Obj("mid");var target=Obj("target");var child=Obj("child",Vector3.right,mid);
            var last=Constraint<HoPositionConstraint>(target,child);var first=Constraint<HoPositionConstraint>(mid,source);Activate(last);Activate(first);Eval(last,first);
            Near(target.position,source.position+Vector3.right,"Reverse registered chain resolves ancestor dependency");
        });
        Case("Parent and UpObject dependencies",()=>{
            var source=Obj("source",Vector3.one*3);var parent=Obj("parent");var target=Obj("target",Vector3.zero,parent);var point=Obj("point",new Vector3(8,4,7));var up=Obj("up");var upSource=Obj("up source",new Vector3(3,8,3));
            var p=Constraint<HoPositionConstraint>(parent,source);var a=Constraint<HoAimConstraint>(target,point);a.UpMode=HoAimUpMode.ObjectPosition;a.UpObject=up;var u=Constraint<HoPositionConstraint>(up,upSource);Activate(p);Activate(a);Activate(u);Eval(a,p,u);
            Near(target.forward,(point.position-target.position).normalized,"Aim reads updated parent position");
            Near(target.up,Vector3.ProjectOnPlane(up.position-target.position,target.forward).normalized,"Aim reads updated UpObject");
        });
        Case("Same Target Position then Aim",()=>{
            var target=Obj("target");var origin=Obj("origin",new Vector3(2,3,4));var point=Obj("point",new Vector3(9,4,5));
            var aim=Constraint<HoAimConstraint>(target,point);var position=Constraint<HoPositionConstraint>(target,origin);Activate(aim);Activate(position);Eval(aim,position);
            Near(target.position,origin.position,"Same-target Position is applied");Near(target.forward,(point.position-origin.position).normalized,"Same-target Aim reads updated origin");
        });
        Case("Writer order and ambiguity",()=>{
            var target=Obj("target");var a=Obj("a",Vector3.right*4);var b=Obj("b",Vector3.right*10);var first=Constraint<HoPositionConstraint>(target,a);var last=Constraint<HoPositionConstraint>(target,b);
            Activate(first);Activate(last);first.Order=1;last.Order=2;last.Weight=.5f;Eval(last,first);Near(target.position,Vector3.right*7,"Ordered writer blends earlier result");
            last.Order=first.Order;target.position=Vector3.zero;HoTransformConstraintScheduler.Evaluate(new HoTransformConstraint[]{last,first});Check(first.LastError!=null&&last.LastError!=null,"Same-order overlapping writers rejected");Near(target.position,Vector3.zero,"Ambiguous writers do not write");
        });
        Case("Cycles and invalid upstream",()=>{
            var a=Obj("a",Vector3.right);var b=Obj("b",Vector3.up);var ca=Constraint<HoPositionConstraint>(a,b);var cb=Constraint<HoPositionConstraint>(b,a);Activate(ca);Activate(cb);
            var independent=Constraint<HoPositionConstraint>(Obj("independent"),Obj("independent source",Vector3.one));Activate(independent);
            HoTransformConstraintScheduler.Evaluate(new HoTransformConstraint[]{ca,cb,independent});Check(ca.LastError!=null&&cb.LastError!=null,"Cycle rejected");Near(a.position,Vector3.right,"Cycle target a untouched");Near(b.position,Vector3.up,"Cycle target b untouched");Near(independent.TargetTransform.position,Vector3.one,"Independent graph still runs");
            ca.Sources[0].transform=null;HoTransformConstraintScheduler.Evaluate(new HoTransformConstraint[]{cb,ca});Check(cb.LastError!=null,"Invalid upstream propagates");Near(b.position,Vector3.up,"Downstream does not read stale invalid result");
        });
    }
}
