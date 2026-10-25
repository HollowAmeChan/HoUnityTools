using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

[Serializable] public class HoAuxMergeCase
{
    public string name;
    public HoAuxRig rig;
    public Transform root,chest,point,copy,aim,fan,mch;
    public int referenceIndex;
    public Vector3 rootPosition,rootScale;
    public Quaternion rootRotation;
    public bool animateChest=true;
}
[Serializable] public class HoAuxMergeResult
{
    public List<string> failures=new List<string>();
    public int checks;
}
public sealed class HoAuxMergeProbe:MonoBehaviour
{
    public HoAuxMergeCase[] cases;
    public string output;
    private void Start()
    {
        var result=new HoAuxMergeResult();
        try
        {
            for(int pose=0;pose<5;pose++)
            {
                foreach(var c in cases)
                {
                    Quaternion motion=Quaternion.Euler(0,pose*13,0);float scale=1+.1f*pose;
                    c.root.localPosition=new Vector3(.2f*pose,.04f*pose,-.1f*pose)+motion*c.rootPosition*scale;
                    c.root.localRotation=motion*c.rootRotation;c.root.localScale=c.rootScale*scale;
                    if(c.animateChest)c.chest.localRotation=Quaternion.Euler(7*pose,5*pose,-3*pose);
                }
                for(int repeat=0;repeat<4;repeat++)
                {
                    foreach(var c in cases)c.rig.EvaluateNow();
                    foreach(var c in cases)
                    {
                        if(!string.IsNullOrEmpty(c.rig.LastDirectError))result.failures.Add(c.name+" "+c.rig.LastDirectError);
                        foreach(string bone in new[]{"point","copy","aim","fan","mch"})
                        {
                            var field=typeof(HoAuxMergeCase).GetField(bone);var expected=(Transform)field.GetValue(cases[c.referenceIndex]);var actual=(Transform)field.GetValue(c);
                            float p=Vector3.Distance(expected.position,actual.position),a=Quaternion.Angle(expected.rotation,actual.rotation);
                            result.checks++;if(p>.0001f||a>.05f)result.failures.Add(c.name+" pose="+pose+" repeat="+repeat+" "+bone+" position="+p+" angle="+a);
                        }
                    }
                }
            }
        }
        catch(Exception e){result.failures.Add(e.ToString());}
        File.WriteAllText(output,JsonUtility.ToJson(result,true));
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(result.failures.Count==0?0:1);
#endif
    }
}
