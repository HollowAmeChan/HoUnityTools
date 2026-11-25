using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // A proof over the actual imported GPU resource. No texture read/write flag,
    // importer, mip content or source material is changed; failure is not proof.
    internal static class HoMigrationTextureAnalysis
    {
        internal static bool TryProveRedConstantZero(Texture source,out string evidence)
        {
            evidence="";
            if(source is not Texture2D texture){evidence="the bound input is not a Texture2D";return false;}
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            {evidence="GPU/float render target unavailable";return false;}
            // A missing streamed/limited mip can return zero without the source
            // resource being zero. Refuse such resources rather than certify it.
            if(texture.streamingMipmaps||QualitySettings.globalTextureMipmapLimit!=0)
            {evidence="streaming or a global mip limit prevents a complete imported-resource proof";return false;}
            if(texture.dimension!=TextureDimension.Tex2D||texture.width<=0||texture.height<=0||texture.mipmapCount<=0||
                texture.width>SystemInfo.maxTextureSize||texture.height>SystemInfo.maxTextureSize)
            {evidence="invalid or unsupported imported dimensions/mips";return false;}
            Shader shader=null;Material material=null;Texture2D readback=null;RenderTexture target=null;
            RenderTexture previous=RenderTexture.active;bool previousWrite=GL.sRGBWrite,previousAsync=ShaderUtil.allowAsyncCompilation;
            long texels=0;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                shader=ShaderUtil.CreateShaderAsset(ProofShader,false);shader.hideFlags=HideFlags.HideAndDontSave;
                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material.SetTexture("_AnalysisTex",texture);
                ShaderUtil.CompilePass(material,0,true);
                if(!shader.isSupported||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("GPU proof shader did not compile");
                GL.sRGBWrite=false;
                for(int mip=0;mip<texture.mipmapCount;mip++)
                {
                    int width=Mathf.Max(1,texture.width>>mip),height=Mathf.Max(1,texture.height>>mip);
                    material.SetInt("_AnalysisMip",mip);
                    target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    if(!target.IsCreated()&&!target.Create())throw new InvalidOperationException("GPU proof target could not be created");
                    RenderTexture.active=target;GL.Clear(false,true,new Color(float.NaN,float.NaN,float.NaN,float.NaN));
                    Graphics.Blit(texture,target,material,0);
                    RenderTexture.active=target;
                    readback=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
                    readback.ReadPixels(new Rect(0,0,width,height),0,0,false);readback.Apply(false,false);
                    var pixels=readback.GetRawTextureData<Color>();
                    if(pixels.Length!=(long)width*height)throw new InvalidOperationException("incomplete GPU mip readback");
                    for(int index=0;index<pixels.Length;index++)
                    {
                        Color value=pixels[index];
                        // G/B/A prove the pass actually ran against a resource of
                        // exactly the expected dimensions and mip count. NaN/Inf,
                        // tiny positives and negatives all fail exact-zero R.
                        if(value.g!=width||value.b!=height||value.a!=texture.mipmapCount)
                            throw new InvalidOperationException("GPU resource/mip dimensions or writer marker did not match");
                        if(value.r!=0f)
                        {evidence="R is not exact zero at mip "+mip+", texel "+index+": "+value.r.ToString("R",System.Globalization.CultureInfo.InvariantCulture);return false;}
                    }
                    texels+=pixels.Length;
                    Object.DestroyImmediate(readback);readback=null;
                    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);target=null;
                }
                evidence="GPU Texture2D.Load scanned "+texture.mipmapCount+" complete imported mips / "+texels+" texels; every decoded R is exactly 0 (no tolerance), dimensions verified; asset="+AssetDatabase.GetAssetPath(texture);
                return true;
            }
            catch(Exception error){evidence="GPU proof unavailable: "+error.GetType().Name+": "+error.Message;return false;}
            finally
            {
                RenderTexture.active=previous;GL.sRGBWrite=previousWrite;ShaderUtil.allowAsyncCompilation=previousAsync;
                if(target!=null)RenderTexture.ReleaseTemporary(target);
                if(readback!=null)Object.DestroyImmediate(readback);
                if(material!=null)Object.DestroyImmediate(material);
                if(shader!=null)Object.DestroyImmediate(shader);
            }
        }

        private const string ProofShader=@"Shader ""Hidden/HoMigration/ProveImportedRedZero"" {
Properties { _AnalysisTex(""Imported texture"",2D)=""white""{} }
SubShader { Pass { Cull Off ZWrite Off ZTest Always Blend Off
HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D<float4> _AnalysisTex; uint _AnalysisMip;
float4 frag(v2f_img input):SV_Target {
 uint width,height,mips; _AnalysisTex.GetDimensions(_AnalysisMip,width,height,mips);
 return float4(_AnalysisTex.Load(int3(int2(input.pos.xy),_AnalysisMip)).r,width,height,mips);
}
ENDHLSL
} } }";
    }
}
