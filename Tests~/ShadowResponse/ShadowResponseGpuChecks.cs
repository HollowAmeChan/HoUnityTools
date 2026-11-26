using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Hollow.HoUnityTools.Editor.ShadowResponse;
using Object=UnityEngine.Object;

public static class ShadowResponseGpuChecks
{
    const string InputFile="D:/Temp/ho-shadow-response-source17-inputs.json";
    const string Output="D:/Temp/ho-shadow-response-gpu-validation.json";
    const string Root="Assets/__ShadowResponseGpuReadback";
    const string ShaderPath="Assets/Editor/HoShadowResponseValidation/ShadowResponseGpuProbe.shader";
    [Serializable] sealed class Inputs {public InputRow[] rows;}
    [Serializable] sealed class InputRow {public string path,guid;public HoEffectShadowLutMigration.Input input;}
    [Serializable] sealed class Case {public string source;public float maxRawError,maxBlendError,maxSourceAAError;public int comparisons;}
    [Serializable] sealed class Report
    {
        public string unity,graphicsApi,colorSpace,inputSha256,shaderSha256,scope;
        public int sourceProfiles,comparisons,samplesPerCapture=4096,assertions;
        public float maxRawError,maxBlendError,maxSourceAAError,independentRGBAError,endpointError,centerUvImprovement,stressAADifference;
        public Case[] cases;
    }
    public static void Run()
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new Exception("Real GPU required");
        if(AssetDatabase.IsValidFolder(Root)||File.Exists(Output))throw new Exception("Use fresh GPU fixture/report.");
        AssetDatabase.CreateFolder("Assets","__ShadowResponseGpuReadback");
        var inputs=JsonUtility.FromJson<Inputs>(File.ReadAllText(InputFile));Require(inputs.rows.Length==17,"Actual source17 input set required");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);Require(shader!=null,"GPU shader missing");
        var material=new Material(shader);ShaderUtil.CompilePass(material,0,true);
        Require(!ShaderUtil.ShaderHasError(shader),string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(x=>x.message)));
        var curveMethod=typeof(HoEffectShadowLutMigration).GetMethod("Curve",BindingFlags.Static|BindingFlags.NonPublic);
        var shadeMethod=typeof(HoEffectShadowLutMigration).GetMethod("Shade",BindingFlags.Static|BindingFlags.NonPublic);
        var bandMethod=typeof(HoEffectShadowLutMigration).GetMethod("Band",BindingFlags.Static|BindingFlags.NonPublic);
        var curve=(Func<Func<float,float>,AnimationCurve>)Delegate.CreateDelegate(typeof(Func<Func<float,float>,AnimationCurve>),curveMethod);
        var shade=(Func<HoEffectShadowLutMigration.Input,float,Color>)Delegate.CreateDelegate(typeof(Func<HoEffectShadowLutMigration.Input,float,Color>),shadeMethod);
        var band=(Func<float,float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float,float>),bandMethod);
        var report=new Report {unity=Application.unityVersion,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),
            inputSha256=Hash(InputFile),shaderSha256=Hash(Path.Combine(Path.GetDirectoryName(Application.dataPath),ShaderPath)),
            scope="Actual migration-generated RGBAHalf LUT GPU sampling vs frozen old fixed-blur Effect shader math. Uniform receive/cast, AO=1, outer Multiply/SoftLight. Source fwidth and a sharp-edge stress witness measured separately; not complete Forward scene parity."};
        var cases=new List<Case>();
        try
        {
            foreach(var row in inputs.rows)
            {
                var p=ScriptableObject.CreateInstance<HoShadowResponseAsset>();var input=row.input;
                p.SetCurves(curve(t=>shade(input,t).r),curve(t=>shade(input,t).g),curve(t=>shade(input,t).b),curve(t=>band(t,input.border1,input.blur1)));
                AssetDatabase.CreateAsset(p,Root+"/"+row.guid+".asset");HoShadowResponseBaker.Bake(p,false);var lut=p.LookupTexture;
                Require(lut.width==1024&&lut.height==1&&lut.format==TextureFormat.RGBAHalf&&!lut.isDataSRGB&&lut.filterMode==FilterMode.Bilinear&&lut.wrapMode==TextureWrapMode.Clamp,"Actual baked LUT format/sampler contract");
                material.SetTexture("_Lookup",lut);material.SetColor("_Bright",input.bright);material.SetColor("_First",input.first);material.SetColor("_Second",input.second);material.SetColor("_Third",input.third);
                material.SetVector("_Borders",new Vector4(input.border1,input.border2,input.border3,0));material.SetVector("_Blurs",new Vector4(input.blur1,input.blur2,input.blur3,0));material.SetFloat("_Strength",Mathf.Clamp01(input.use*input.strength));
                var result=new Case {source=row.path};
                foreach(float receive in new[]{0f,1f})foreach(float cast in new[]{0f,.5f,1f})
                {
                    material.SetFloat("_Receive",receive);material.SetFloat("_Cast",cast);material.SetFloat("_Output",0);material.SetFloat("_Shift",0);
                    Color[] actual=Render(material,0),expected=Render(material,1),aa=Render(material,2);
                    result.maxRawError=Mathf.Max(result.maxRawError,Error(actual,expected));result.maxSourceAAError=Mathf.Max(result.maxSourceAAError,Error(aa,expected));
                    result.comparisons+=2;report.assertions+=4096*4*2;
                    foreach(Color baseColor in new[]{new Color(.25f,.25f,.25f,1),new Color(.5f,.5f,.5f,1),new Color(.75f,.75f,.75f,1),new Color(2,1,.5f,1)})
                        foreach(float blend in new[]{2f,10f})
                        {
                            material.SetColor("_Base",baseColor);material.SetFloat("_Blend",blend);material.SetFloat("_Output",1);
                            result.maxBlendError=Mathf.Max(result.maxBlendError,Error(Render(material,0),Render(material,1)));result.comparisons++;report.assertions+=4096*4;
                        }
                }
                Require(result.maxRawError<.003f,"RGBA response error "+row.path+" "+result.maxRawError);Require(result.maxBlendError<.004f,"Outer blend error "+row.path+" "+result.maxBlendError);
                report.maxRawError=Mathf.Max(report.maxRawError,result.maxRawError);report.maxBlendError=Mathf.Max(report.maxBlendError,result.maxBlendError);report.maxSourceAAError=Mathf.Max(report.maxSourceAAError,result.maxSourceAAError);report.comparisons+=result.comparisons;cases.Add(result);
            }
            // Four independent channels exercise endpoints, half storage, linear sampling, and center UV correction.
            var independent=ScriptableObject.CreateInstance<HoShadowResponseAsset>();independent.SetCurves(AnimationCurve.Linear(0,.13f,1,.93f),AnimationCurve.Linear(0,.77f,1,.21f),AnimationCurve.Linear(0,.32f,1,.68f),AnimationCurve.Linear(0,.06f,1,.86f));
            AssetDatabase.CreateAsset(independent,Root+"/Independent.asset");HoShadowResponseBaker.Bake(independent,false);material.SetTexture("_Lookup",independent.LookupTexture);material.SetFloat("_Output",0);material.SetFloat("_Receive",1);material.SetFloat("_Cast",1);material.SetFloat("_Shift",0);
            Color[] sampled=Render(material,0),uncentered=Render(material,3);float wrong=0;
            for(int x=0;x<4096;x++)
            {
                float t=x/4095f;Color expected=new Color(Mathf.Lerp(.13f,.93f,t),Mathf.Lerp(.77f,.21f,t),Mathf.Lerp(.32f,.68f,t),Mathf.Lerp(.06f,.86f,t));
                report.independentRGBAError=Mathf.Max(report.independentRGBAError,Distance(sampled[x],expected));wrong=Mathf.Max(wrong,Distance(uncentered[x],expected));report.assertions+=4;
                if(x==0||x==4095)report.endpointError=Mathf.Max(report.endpointError,Distance(sampled[x],expected));
            }
            report.centerUvImprovement=wrong-report.independentRGBAError;Require(report.independentRGBAError<.0006f&&report.endpointError<.0006f,"Independent linear RGBA/endpoint/sRGB proof");
            foreach(float shift in new[]{-.25f,.25f})
            {
                material.SetFloat("_Shift",shift);Color[] clamp=Render(material,0);
                Require(Distance(clamp[shift<0?0:4095],sampled[shift<0?0:4095])<.00001f,"LinearClamp out-of-range endpoint");report.assertions+=4;
            }
            material.SetFloat("_Shift",0);material.SetVector("_Borders",new Vector4(2047.25f/4095f,.1f,.05f,0));material.SetVector("_Blurs",Vector4.zero);
            material.SetColor("_Bright",Color.white);material.SetColor("_First",new Color(.2f,.3f,.4f,1));material.SetColor("_Second",new Color(0,0,0,0));material.SetColor("_Third",new Color(0,0,0,0));
            report.stressAADifference=Error(Render(material,1),Render(material,2));Require(report.stressAADifference>.005f,"fwidth stress witness must differ from fixed blur");
            report.sourceProfiles=cases.Count;report.cases=cases.ToArray();File.WriteAllText(Output,JsonUtility.ToJson(report,true));
            Debug.Log("HO_SHADOW_RESPONSE_GPU_OK profiles="+report.sourceProfiles+" comparisons="+report.comparisons+" assertions="+report.assertions+" raw="+report.maxRawError+" blend="+report.maxBlendError+" AAstress="+report.stressAADifference);
        }
        finally {Object.DestroyImmediate(material);}
    }
    static Color[] Render(Material material,float mode)
    {
        material.SetFloat("_Mode",mode);var target=RenderTexture.GetTemporary(4096,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        try {Graphics.Blit(null,target,material);var request=AsyncGPUReadback.Request(target,0,TextureFormat.RGBAFloat);request.WaitForCompletion();Require(!request.hasError,"GPU readback failed");return request.GetData<Color>().ToArray();}
        finally {RenderTexture.ReleaseTemporary(target);}
    }
    static float Error(Color[] a,Color[] b){float error=0;for(int i=0;i<a.Length;i++)error=Mathf.Max(error,Distance(a[i],b[i]));return error;}
    static float Distance(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
