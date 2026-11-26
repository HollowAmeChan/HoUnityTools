using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hollow.HoUnityTools.Editor.ShadowResponse.LwguiIntegration;
using UnityEditor;
using UnityEngine;

// Copy into a disposable host beside HoShadowRampRecovery.cs. No production project writes.
public static class ShadowRecoveryTransactionChecks
{
    private static int checks;
    private static void Need(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
    private static void Private()=>Need(Root.StartsWith(Path.GetFullPath("D:/Temp/"),StringComparison.OrdinalIgnoreCase),"Private host only");
    private static string Env(string key)=>Environment.GetEnvironmentVariable(key)??throw new InvalidOperationException(key);
    private static string Full(string path)=>Path.Combine(Root,path);
    private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    private static T Read<T>(string path)=>JsonUtility.FromJson<T>(File.ReadAllText(path));

    // Reset only a known successful private corpus after a shader revision, then add a genuine Unity Material Variant.
    public static void PrepareFinalCorpus()
    {
        Private();string backup=Env("HO_SHADOW_RECOVERY_BACKUP");
        var journal=Read<HoShadowRampRecovery.Journal>(Path.Combine(backup,"journal.json"));
        Need(journal.projectRoot==Root&&journal.state=="Verified","Known private successful transaction");
        foreach(var item in journal.rows)
        {
            Need(Hash(Full(item.row.targetPath))==item.afterSha&&Hash(Full(item.row.targetPath+".meta"))==item.afterMetaSha,"Preserve edited fixture material");
            Need(Hash(Path.Combine(backup,item.row.targetGuid+".mat"))==item.row.targetSha256&&Hash(Path.Combine(backup,item.row.targetGuid+".meta"))==item.row.targetMetaSha256,"Exact fixture backup");
        }
        foreach(var item in journal.rows)
        {
            File.Copy(Path.Combine(backup,item.row.targetGuid+".mat"),Full(item.row.targetPath),true);
            File.Copy(Path.Combine(backup,item.row.targetGuid+".meta"),Full(item.row.targetPath+".meta"),true);
            AssetDatabase.ImportAsset(item.row.targetPath,ImportAssetOptions.ForceSynchronousImport);
        }
        var plan=Read<HoShadowRampRecovery.Plan>(Env("HO_SHADOW_RECOVERY_PLAN"));
        foreach(var ramp in plan.ramps)
        {
            string old=ramp.assetPath;
            ramp.assetPath=old.Substring(0,old.Length-6)+"-final.asset";
            foreach(var row in plan.rows.Where(r=>r.rampAssetPath==old))row.rampAssetPath=ramp.assetPath;
            ramp.targetGuids=plan.rows.Where(r=>r.rampAssetPath==ramp.assetPath).Select(r=>r.targetGuid).ToArray();
        }
        var template=plan.rows.First(r=>r.targetFamily=="Cloth"&&r.activeShadow);
        var parent=AssetDatabase.LoadAssetAtPath<Material>(template.targetPath);
        string folder="Assets/HoRecoveryVariant-"+System.Guid.NewGuid().ToString("N").Substring(0,8);
        AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        var variant=new Material(parent){parent=parent};
        variant.SetFloat("_SpecularStrength",.123f);
        string path=folder+"/Variant.mat";AssetDatabase.CreateAsset(variant,path);AssetDatabase.SaveAssetIfDirty(variant);
        var rowCopy=JsonUtility.FromJson<HoShadowRampRecovery.Row>(JsonUtility.ToJson(template));
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(variant,out rowCopy.targetGuid,out long _);
        rowCopy.targetPath=path;rowCopy.targetSha256=Hash(Full(path));rowCopy.targetMetaSha256=Hash(Full(path+".meta"));rowCopy.isFork=true;
        plan.rows=plan.rows.Concat(new[]{rowCopy}).ToArray();
        var shared=plan.ramps.Single(r=>r.assetPath==rowCopy.rampAssetPath);shared.targetGuids=shared.targetGuids.Concat(new[]{rowCopy.targetGuid}).ToArray();
        string output=Env("HO_SHADOW_RECOVERY_FIXTURE_PLAN");Need(!File.Exists(output),"Fresh final fixture plan");
        File.WriteAllText(output,JsonUtility.ToJson(plan,true));
        Debug.Log("HO_SHADOW_RECOVERY_FINAL_CORPUS_PREPARED targets="+plan.rows.Length+" variant="+path);
    }

    public static void ApplyFinalCorpus()
    {
        Private();HoShadowRampRecovery.RunPreview();HoShadowRampRecovery.RunApply();
    }
    public static void VerifyAndRestore()
    {
        Private();string backup=Env("HO_SHADOW_RECOVERY_BACKUP");
        var journal=Read<HoShadowRampRecovery.Journal>(Path.Combine(backup,"journal.json"));
        HoShadowRampRecovery.RunVerify();
        var variant=journal.rows.Select(r=>AssetDatabase.LoadAssetAtPath<Material>(r.row.targetPath)).Single(m=>m.isVariant);
        Need(variant.parent&&Mathf.Abs(variant.GetFloat("_SpecularStrength")-.123f)<.000001f,"Variant parent and authored override survive");
        // A corrupt meta backup must be rejected before the first .mat or .meta write.
        string meta=Path.Combine(backup,journal.rows.Last().row.targetGuid+".meta");byte[] original=File.ReadAllBytes(meta);
        try
        {
            File.WriteAllBytes(meta,original.Concat(new byte[]{10}).ToArray());
            bool rejected=false;try{HoShadowRampRecovery.RunRestore();}catch(InvalidOperationException){rejected=true;}
            Need(rejected,"Corrupt backup meta rejected");
            foreach(var item in journal.rows)Need(Hash(Full(item.row.targetPath))==item.afterSha&&Hash(Full(item.row.targetPath+".meta"))==item.afterMetaSha,"Rejected restore changes no asset bytes");
        }
        finally{File.WriteAllBytes(meta,original);}
        HoShadowRampRecovery.RunRestore();
        foreach(var item in journal.rows)Need(Hash(Full(item.row.targetPath))==item.row.targetSha256&&Hash(Full(item.row.targetPath+".meta"))==item.row.targetMetaSha256,"Cross-process exact byte restore");
        foreach(string path in journal.createdAssets)Need(File.Exists(Full(path)),"Restore retains reusable authored resources");
        string output=Env("HO_SHADOW_RECOVERY_FIXTURE_RESULT");Need(!File.Exists(output),"Fresh evidence");
        File.WriteAllText(output,"{\"checks\":"+checks+",\"targets\":"+journal.rows.Length+",\"variant\":true,\"corruptMetaRejectedBeforeWrites\":true,\"crossProcessRestore\":true}");
        Debug.Log("HO_SHADOW_RECOVERY_TRANSACTION_CHECKS_OK checks="+checks);
    }
}
