using System;
using System.Linq;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

[Serializable] public class V3HoMergeRoute
{
    public HoAuxRig rig;
    public Transform root;
    public Vector3 rootPosition,rootScale;
    public Quaternion rootRotation;
    public Transform[] inputs,owners;
    public Quaternion[] inputRotations;
}
public sealed class V3HoMergeProbe:MonoBehaviour
{
    public V3HoMergeRoute[] routes;
    public bool Completed {get;private set;}
    public string Failure {get;private set;}
    public int Checks {get;private set;}
    private void Start()
    {
        try
        {
            if(GetComponentsInChildren<Hollow.HoUnityTools.Armatures.HoCombineArmature>(true).Length!=0)
                throw new Exception("Automatic play-mode merge did not run");
            foreach(var r in routes){r.rig.Mode=HoAuxRig.UpdateMode.Manual;if(r.rig.OperationCount!=110)throw new Exception("Real WTD merge lost operations");}
            for(int pose=0;pose<5;pose++)
            {
                foreach(var r in routes)
                {
                    var motion=Quaternion.Euler(3*pose,13*pose,-2*pose);float scale=1+pose*.03f;
                    r.root.position=new Vector3(.13f*pose,-.03f*pose,.07f*pose)+motion*r.rootPosition*scale;
                    r.root.rotation=motion*r.rootRotation;r.root.localScale=r.rootScale*scale;
                    for(int i=0;i<r.inputs.Length;i++)r.inputs[i].localRotation=r.inputRotations[i]*Quaternion.Euler(2*pose,pose*(i%2==0?3:-3),-pose);
                }
                for(int repeat=0;repeat<4;repeat++)
                {
                    foreach(var r in routes){r.rig.EvaluateNow();if(!string.IsNullOrEmpty(r.rig.LastDirectError))throw new Exception(r.rig.LastDirectError);}
                    for(int i=0;i<routes[0].owners.Length;i++)
                    {
                        var a=routes[0].owners[i];var b=routes[1].owners[i];
                        float p=Vector3.Distance(a.position,b.position),angle=Quaternion.Angle(a.rotation,b.rotation),s=Vector3.Distance(a.lossyScale,b.lossyScale);
                        Checks++;if(p>.0001f||angle>.1f||s>.0001f)throw new Exception("WTD merge parity "+a.name+" pose="+pose+" repeat="+repeat+" position="+p+" angle="+angle+" scale="+s);
                    }
                }
            }
            Debug.Log("HOAUX_REAL_MERGE_PARITY_OK "+name+": "+Checks+" comparisons, 110 operations, automatic scene processing");
        }
        catch(Exception e){Failure=e.ToString();Debug.LogException(e);}
        Completed=true;
    }
}
