using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // Material-only preview. The caller owns roles/forks, external animation and
    // MPB/script preflight, transactional application and source retirement.
    internal static class HoLegacyMaterialConverter
    {
        private static readonly string[] StencilSuffixes={"Ref","ReadMask","WriteMask","Comp","Pass","Fail","ZFail"};
        private static readonly HashSet<string> LegacyShaders=new(StringComparer.Ordinal)
        {
            "lilToon","Hidden/lilToonOutline","Hidden/lilToonCutout","Hidden/lilToonCutoutOutline",
            "Hidden/lilToonTransparent","Hidden/lilToonTransparentOutline","Hidden/lilToonOnePassTransparent",
            "Hidden/lilToonOnePassTransparentOutline","Hidden/lilToonTwoPassTransparent","Hidden/lilToonTwoPassTransparentOutline"
        };
        internal static Material Convert(Material source,HoMigrationFamily family,List<string> differences,List<string> blockers,string derivedAssetFolder,string[] modelDecisions=null)
        {
            if(differences==null||blockers==null)throw new ArgumentNullException(differences==null?nameof(differences):nameof(blockers));
            if(source==null||source.shader==null||!LegacyShaders.Contains(source.shader.name))
            {Block(blockers,"SOURCE_SHADER: conversion requires a current supported lilToon shader.");return null;}
            Shader shader=Shader.Find("HoShader/"+family+" URP");
            if(shader==null){Block(blockers,"TARGET_SHADER: missing HoShader/"+family+" URP");return null;}
            var target=new Material(shader){name=source.name+" ["+family+"]",enableInstancing=source.enableInstancing,doubleSidedGI=source.doubleSidedGI,globalIlluminationFlags=source.globalIlluminationFlags};
            try
            {
                int mode=SurfaceMode(source);
                HashSet<string> decisions=ValidateModelDecisions(source,family,modelDecisions,differences,blockers);
                if(decisions.Contains("PreserveHiddenCoverage"))
                {
                    // The old vertex stage returned before every raster output.
                    // Keep a real zero-coverage cutout so native RT alpha-any-hit
                    // also hides it; pass-disable flags alone do not affect RT.
                    CopyColor(source,"_Color",target,"_BaseColor",Color.white,blockers);
                    CopyTexture(source,"_MainTex",target,"_BaseMap",false);SetAlpha(target,"_BaseColor",0);
                    Set(target,"_SurfaceMode",1);Set(target,"_AlphaMaskMode",0);Set(target,"_Cutoff",.5f);
                    Set(target,"_OutlineEnabled",0);Set(target,"_OutlineWidth",0);Set(target,"_UseEmission",0);
                    Set(target,"_QueueControl",1);Sync(target,family,blockers);
                    if(source.renderQueue>=0)target.renderQueue=source.renderQueue;
                    Set(target,"_QueueOffset",target.renderQueue-2450);Set(target,"_HoOITMaterialQueue",target.renderQueue);
                    MapMetadata(source,target,family);return target;
                }
                CheckUnsupportedInputs(source,family,mode,differences,blockers,decisions);
                CopyColor(source,"_Color",target,"_BaseColor",Color.white,blockers);
                CopyTexture(source,"_MainTex",target,"_BaseMap",false);
                MapSurface(source,target,family,mode,differences,blockers,derivedAssetFolder,decisions);
                MapNormal(source,target,family,differences,blockers);
                MapPbr(source,target,family,differences,blockers,derivedAssetFolder,decisions);
                MapLighting(source,target,family,differences,blockers);
                MapAo(source,target,family,differences,blockers,derivedAssetFolder);
                MapEmission(source,target,family,differences,blockers,derivedAssetFolder,decisions);
                MapTension(source,target,family,differences,blockers);
                MapStencil(source,target,"_Stencil",blockers);
                if(decisions.Contains("RetireZeroWidthOutline")||decisions.Contains("RetireEyeOutline"))
                {Set(target,"_OutlineEnabled",0);Set(target,"_OutlineWidth",0);}
                else MapOutline(source,target,family,mode,differences,blockers,derivedAssetFolder,decisions);
                MapSss(source,target,family,differences,blockers,derivedAssetFolder,decisions);
                MapSemantics(source,target,family,differences,blockers);
                Sync(target,family,blockers);
                // Preserve authored sorting after each family synchronizes its defaults.
                if(source.renderQueue>=0)target.renderQueue=source.renderQueue;
                Set(target,"_HoOITMaterialQueue",target.renderQueue);
                Set(target,"_QueueOffset",target.renderQueue-(mode==2?3000:mode==1?2450:2000));
                MapMetadata(source,target,family);
                return target;
            }
            catch(Exception error)
            {Block(blockers,"CONVERSION_ERROR: "+error.GetType().Name+": "+error.Message);return target;}
        }
        private static void MapSurface(Material s,Material t,HoMigrationFamily family,int mode,List<string> d,List<string> b,string folder,HashSet<string> decisions)
        {
            Set(t,"_SurfaceMode",mode);Set(t,"_AlphaClip",mode==1?1:0);Set(t,"_Surface",mode==2?1:0);
            Set(t,"_Cutoff",F(s,"_Cutoff",.5f));Set(t,"_SubpassCutoff",F(s,"_SubpassCutoff",.5f));
            Set(t,"_Cull",F(s,"_Cull",2));Set(t,"_QueueControl",1);
            Set(t,"_TransparentZWrite",mode==2?F(s,"_ZWrite",0):0);Set(t,"_ZWrite",F(s,"_ZWrite",mode==2?0:1));
            if(decisions.Contains("ReplaceTwoPassTransparency"))Set(t,"_Cull",0);
            if(decisions.Contains("UseNativeTransparentDepth")){Set(t,"_TransparentZWrite",0);Set(t,"_ZWrite",0);}
            bool oit=mode==2&&On(s,"_lilOITEnabled");
            Set(t,"_UseHoOIT",oit?1:0);
            if(oit&&!t.HasProperty("_UseHoOIT"))Block(b,"OIT_UNSUPPORTED: active legacy _lilOITEnabled requires a native OIT pass; "+family+" has none.");
            bool front=mode==2&&s.FindPass("HO_GEOMETRY_BUFFER")>=0&&s.GetShaderPassEnabled("HO_GEOMETRY_BUFFER")&&s.GetShaderPassEnabled("HoGeometryBuffer");
            Set(t,"_TransparentHoCoverage",front?1:0);
            if(front)
            {
                Diff(d,"TRANSPARENT_HO_MODEL: enabled legacy front geometry becomes the target explicit Ho-front-coverage policy; numeric surface/object/capture availability follows the chosen family, not the old generic transparent passes.");
                if(family==HoMigrationFamily.Cloth&&Mathf.Abs(F(s,"_Cutoff",.5f)-.001f)>.00001f)
                    Block(b,"CLOTH_TRANSPARENT_HO_THRESHOLD: legacy front coverage uses _Cutoff="+F(s,"_Cutoff",.5f)+" while current Cloth geometry uses fixed .001; explicit pipeline/coverage review required.");
            }
            Set(t,"_HoCharacterCaptureOpacity",F(s,"_HoCharacterCaptureOpacity",1));
            if(family==HoMigrationFamily.Skin&&mode==2)Block(b,"SURFACE_TRANSPARENT_SKIN: Skin has no transparent mode; explicitly choose a Face-overlay/Eye or Cloth role instead of silently making it opaque.");
            if(mode==2)
            {
                Set(t,"_Blend",F(s,"_SrcBlend",1)==1?1:0);
                Diff(d,"TRANSPARENCY_MODEL: single-pass "+family+" blending/depth/Ho coverage replaces the legacy transparent lighting/compositing model; source queue is preserved.");
                if((family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Hair)&&F(s,"_ZWrite",0)>.5f&&!decisions.Contains("UseNativeTransparentDepth"))
                    Block(b,"TRANSPARENT_DEPTH_WRITE: source Forward writes depth; "+family+" fixes transparent Forward depth write off.");
                if(F(s,"_DstBlend",10)!=10||(F(s,"_SrcBlend",1)!=1&&F(s,"_SrcBlend",1)!=5))
                    Block(b,"TRANSPARENT_BLEND: custom legacy RGB blend factors are not a standard alpha/premultiplied target mode.");
            }
            if(F(s,"_BlendOp")!=0||F(s,"_BlendOpAlpha")!=0)Block(b,"BLEND_OPERATION: non-Add legacy blend operations have no corresponding target setting.");
            if(mode!=2&&F(s,"_ZWrite",1)<.5f)Block(b,"SOLID_DEPTH_WRITE: source solid Forward has ZWrite Off but target solid family writes depth.");
            int mask=Mathf.RoundToInt(F(s,"_AlphaMaskMode"));
            bool hsvg=V(s,"_MainTexHSVG",new Vector4(0,1,1,1))!=new Vector4(0,1,1,1)||F(s,"_MainGradationStrength")!=0;
            bool alpha=mode!=0;
            if(hsvg)
            {
                Diff(d,"HSVG_NUMERIC_CONSUMERS: legacy visible Forward applies main HSVG/gradation but old HoSurfaceBuffer/legacy RT publishes unadjusted albedo. New family SB/RT uses the baked visible-Forward albedo; numeric GI/reflection consumers intentionally change.");
                if(On(s,"_UseEmission")&&F(s,"_EmissionMainStrength")!=0)
                    Diff(d,"HSVG_EMISSION_ALBEDO: live emission MainStrength now follows the corrected visible albedo. Old numeric SB emission used unadjusted albedo, so numeric radiance changes while matching the visible Forward emission component.");
            }
            if(family==HoMigrationFamily.Hair||(family==HoMigrationFamily.Environment&&alpha&&mask!=0))
            {
                if(hsvg||alpha)
                {
                    Texture2D map=HoMigrationTextureBaker.HairBase(s,folder,b);
                    if(map!=null){t.SetTexture("_BaseMap",map);SetAlpha(t,"_BaseColor",1);Set(t,"_Opacity",1);}
                    StaticBake(d,"BASE_RGBA",s);
                }
            }
            else
            {
                if(hsvg)
                {
                    Texture2D map=HoMigrationTextureBaker.HsvgBase(s,folder,b);
                    if(map!=null)t.SetTexture("_BaseMap",map);
                    StaticBake(d,"LEGACY_HSVG",s);
                }
                if(alpha&&(family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye||family==HoMigrationFamily.Cloth&&mask!=0))
                {
                    Texture2D coverage=HoMigrationTextureBaker.Coverage(s,folder,b);
                    if(coverage!=null)
                    {
                        string input=family==HoMigrationFamily.Cloth?"_AlphaMask":"_CoverageMap";
                        t.SetTexture(input,coverage);SetAlpha(t,"_BaseColor",1);
                        if(family==HoMigrationFamily.Cloth)
                        {Set(t,"_AlphaMaskMode",1);Set(t,"_AlphaMaskScale",1);Set(t,"_AlphaMaskValue",0);SetST(t,input,Scale(s,"_MainTex"),Offset(s,"_MainTex"));}
                    }
                    StaticBake(d,"COVERAGE",s);
                }
            }
            if(mode==1&&F(s,"_AlphaToMask")>.5f)
            {
                if(family==HoMigrationFamily.Eye)Set(t,"_AlphaToCoverage",1);
                else if(family==HoMigrationFamily.Environment)Set(t,"_AlphaToMask",1);
                else Diff(d,"CUTOUT_MSAA: target "+family+" uses binary clipping; legacy alpha-to-mask edge smoothing is not reproduced.");
            }
        }
        private static void MapNormal(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b)
        {
            bool shared=family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye;
            CopyTexture(s,"_BumpMap",t,"_BumpMap",!shared);Set(t,"_BumpScale",F(s,"_BumpScale",1));
            if(shared&&!IdentityST(s,"_BumpMap"))Block(b,"NORMAL_UV_SHARED: Skin/Eye normal shares BaseMap ST; the source extra BumpMap ST needs a dedicated normal resample.");
            Set(t,"_FlipNormal",F(s,"_FlipNormal"));
            if((shared||family==HoMigrationFamily.Hair)&&F(s,"_FlipNormal")<.5f&&F(s,"_Cull",2)==0)
                Diff(d,"BACKFACE_NORMAL: this family flips visible backface normals unconditionally; legacy FlipNormal=0 is not reproduced.");
            SamplerDifference(s,"_BumpMap",d);
        }
        private static void MapPbr(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b,string folder,HashSet<string> decisions)
        {
            bool packed=F(s,"_PBRInputMode")>.5f;
            float metallic=Mathf.Clamp01(F(s,"_Metallic")),smoothness=Mathf.Clamp01(F(s,"_Smoothness",1)),f0=Mathf.Clamp01(F(s,"_Reflectance",.04f));
            if(family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Environment)
            {
                Set(t,"_PBRInputMode",packed?1:0);Set(t,"_Metallic",metallic);Set(t,"_Reflectance",f0);Set(t,"_Smoothness",smoothness);
                foreach(string map in new[]{"_PBRMap","_MetallicGlossMap","_ReflectanceTex","_SmoothnessTex"})CopyTexture(s,map,t,map,true);
                Set(t,"_MetallicMapScale",metallic);Set(t,"_SmoothnessMapScale",smoothness);Set(t,"_GSAAStrength",F(s,"_GSAAStrength"));
                if(family==HoMigrationFamily.Environment)
                    foreach(string map in packed?new[]{"_PBRMap"}:new[]{"_MetallicGlossMap","_ReflectanceTex","_SmoothnessTex"})SamplerDifference(s,map,d);
                if(family==HoMigrationFamily.Cloth&&packed&&!Assigned(s,"_PBRMap"))
                    Diff(d,"PBR_UNBOUND_PACKED: legacy white packed fallback equals copied scalar values; target assigned-map flags retain scalar fallback.");
            }
            else if(family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye)
            {
                Set(t,"_Smoothness",smoothness);bool smoothMap=Assigned(s,packed?"_PBRMap":"_SmoothnessTex");
                if(smoothMap&&t.HasProperty("_SmoothnessMap"))
                {
                    Texture map=packed?HoMigrationTextureBaker.Channel(s,"_PBRMap",3,folder,b):Texture(s,"_SmoothnessTex");
                    if(map!=null){t.SetTexture("_SmoothnessMap",map);ComposeST(s,packed?"_PBRMap":"_SmoothnessTex",t,"_SmoothnessMap");}
                    Set(t,"_SmoothnessMapScale",smoothness);
                    if(!packed)SamplerDifference(s,"_SmoothnessTex",d);
                }
                else if(smoothMap)Block(b,"EYE_SMOOTHNESS_MAP: Eye has scalar smoothness only; source smoothness varies spatially.");
                if(metallic>0)
                {
                    string metalInput=packed?"_PBRMap":"_MetallicGlossMap";
                    string evidence="no texture actually bound to "+metalInput;
                    bool proven=Assigned(s,metalInput)&&HoMigrationTextureAnalysis.TryProveRedConstantZero(Texture(s,metalInput),out evidence);
                    if(proven)
                    {
                        // Keep the proof itself in the reviewable result. Do not
                        // reinterpret the source scalar alone as its surface.
                        Diff(d,"CONSTANT_ZERO_METALLIC: source scalar "+metallic+" * "+metalInput+".r is exactly zero: "+evidence+". "+family+" fixed metallic=0 preserves this static surface; external texture overrides remain caller preflight.");
                    }
                    else Block(b,"NONMETAL_FAMILY: source metallic="+metallic+" (including its map) cannot be represented by "+family+" fixed metallic=0; constant-zero proof unavailable: "+evidence+".");
                }
                if(Assigned(s,packed?"_PBRMap":"_ReflectanceTex"))Block(b,"DIELECTRIC_F0_MAP: "+family+" has no spatial F0 input; source "+(packed?"packed G":"ReflectanceTex R")+" requires explicit model review.");
                if(family==HoMigrationFamily.Skin)
                {
                    float targetF0=decisions.Contains("UseNativeSkinF0")?.04f:f0;
                    Set(t,"_SpecularStrength",targetF0/.04f);
                    if(f0>.08f&&!decisions.Contains("UseNativeSkinF0"))Block(b,"SKIN_F0_RANGE: source absolute F0="+f0+" exceeds Skin maximum .08; preview shader clamps, not an equivalent conversion.");
                }
                else if(Mathf.Abs(f0-.04f)>.0001f)Block(b,"EYE_F0_FIXED: source F0="+f0+" differs from Eye fixed .04.");
                if(F(s,"_GSAAStrength")!=0)Diff(d,"GSAA_MODEL: "+family+" does not expose legacy normal-variance roughness filtering.");
            }
            else Diff(d,"HAIR_HIGHLIGHT_MODEL: strand primary/secondary lobes and diffuse HoSurface proxy replace legacy GGX metallic/F0/smoothness. New lobe defaults are retained; no retired hair-enum or width/F0 guessing.");
            bool reflection=On(s,"_UseReflection");
            if(family==HoMigrationFamily.Cloth){Set(t,"_UseSpecular",reflection?1:0);Set(t,"_SpecularStrength",F(s,"_SpecularStrength",1));Set(t,"_ApplySpecular",F(s,"_ApplySpecular",1));}
            if(family==HoMigrationFamily.Environment)Set(t,"_SpecularHighlights",reflection&&On(s,"_ApplySpecular",true)?1:0);
            if(family==HoMigrationFamily.Skin)Set(t,"_DirectSpecularStrength",reflection&&On(s,"_ApplySpecular",true)?F(s,"_SpecularStrength",1):0);
            if(family==HoMigrationFamily.Eye)Set(t,"_SpecularStrength",reflection&&On(s,"_ApplySpecular",true)?F(s,"_SpecularStrength",1):0);
            if(On(s,"_UseSilk"))
            {
                if(family==HoMigrationFamily.Cloth)foreach(string name in new[]{"_UseSilk","_SilkStrength","_SilkAnisotropy","_SilkViewShift"})CopyFloat(s,name,t,name);
                else Diff(d,"SILK_MODEL: active legacy silk lobe is replaced by the selected "+family+" response.");
            }
        }
        private static void MapLighting(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b)
        {
            bool environment=On(s,"_UseEnvironmentLighting",true),probe=On(s,"_ApplyReflection");
            float diffuse=environment?Mathf.Max(0,F(s,"_EnvironmentDiffuseStrength",1)):0;
            float reflection=environment?Mathf.Clamp01(F(s,"_EnvironmentReflectionStrength",1)):0;
            Set(t,"_UseEnvironmentLighting",environment?1:0);Set(t,"_EnvironmentDiffuseStrength",diffuse);
            Set(t,"_EnvironmentDiffuse",diffuse);Set(t,"_EnvironmentStrength",diffuse);
            Set(t,"_EnvironmentReflections",reflection>0?1:0);Set(t,"_ProbeReflections",probe?1:0);
            Set(t,"_EnvironmentReflectionStrength",reflection*(t.HasProperty("_ProbeReflections")?1:probe?1:0));
            Set(t,"_EnvironmentReflection",reflection*(probe?1:0));Set(t,"_IndirectStrength",1);
            foreach(var pair in new[]{("_HoAdditionalLightIntensity","_AdditionalLightIntensity"),("_HoAdditionalLightIntensity","_AdditionalLightStrength"),
                ("_HoAdditionalLightShadowStrength","_AdditionalLightShadowStrength"),("_ShadowReceive","_ReceiveShadowStrength"),("_ShadowReceive","_MainLightShadowStrength")})CopyFloat(s,pair.Item1,t,pair.Item2);
            Set(t,"_ReceiveShadows",family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye?F(s,"_ShadowReceive"):1);Set(t,"_CastShadows",Casts(s)?1:0);
            Set(t,"_UseHoShadowCast",F(s,"_ShadowReceive")>0?1:0);Set(t,"_UseHoCastShadows",F(s,"_ShadowReceive")>0?1:0);
            if(!t.HasProperty("_AdditionalLightIntensity")&&!t.HasProperty("_AdditionalLightStrength")&&F(s,"_HoAdditionalLightIntensity",1)!=1)
                Diff(d,"ADDITIONAL_LIGHT_GAIN: target family has no separate material additional-light multiplier.");
            if(On(s,"_UsePlanarReflection"))
            {
                if(t.HasProperty("_UsePlanarReflection"))
                {
                    Set(t,"_UsePlanarReflection",1);Set(t,"_PlanarReflectionStrength",F(s,"_PlanarReflectionStrength",1));
                    foreach(string name in new[]{"_PlanarReflectionMinSmoothness","_PlanarReflectionEdgeFade","_PlanarReflectionFadeStart","_PlanarReflectionFadeEnd","_PlanarReflectionFlipY"})CopyFloat(s,name,t,name);
                    CopyColor(s,"_PlanarReflectionTint",t,"_PlanarReflectionTint",Color.white,b);
                    Diff(d,"PLANAR_MODEL: native Forward planar reflection replaces the old shared fullscreen/Forward route; probe gain stays separate. Renderer-feature/camera binding belongs to caller migration.");
                }
                else Block(b,"PLANAR_UNSUPPORTED: active planar reflection has no input on "+family+".");
            }
            if(Assigned(s,"_ReflectionCubeTex")&&(On(s,"_ReflectionCubeOverride")||RgbPositive(LinearColor(s,"_ReflectionCubeColor",Color.clear))))
                Block(b,"CUSTOM_CUBEMAP: active material cubemap override/addition needs explicit scene/probe migration; no target cube slot.");
            Diff(d,"LIGHTING_MODEL: "+family+" uses dedicated diffuse/ramp/light-limit/shadow response. Old toon bands, lighting floor/ceiling, monochrome and backface tint/shadow are not literal equivalents.");
            if(On(s,"_UseShadow"))
            {
                CopyColor(s,"_ShadowColor",t,"_ShadowTint",Color.gray,b);
                Diff(d,"SHADOW_RESPONSE: old color bands/thresholds/masks are approximated by native ramp/tint; no SDF/ramp texture is inferred from old bands.");
            }
            MapDecorative(s,t,family,d,b);
        }
        private static void MapAo(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b,string folder)
        {
            int source=Mathf.RoundToInt(F(s,"_AOSource"));
            bool runs=On(s,"_UseAODarkening")&&F(s,"_AODarkStrength",1)>0||On(s,"_UseShadow")&&F(s,"_AOStrength",1)>0;
            if(!runs)source=0;
            bool baked=source==1||source==3,realtime=source==2||source==3;
            Set(t,"_AOSource",source);Set(t,"_AOInput",source);Set(t,"_AOStrength",baked?1:0);
            Set(t,"_RealtimeAOStrength",realtime?(source==3?F(s,"_AORealtimeWeight",1):1):0);Set(t,"_UseRealtimeAO",realtime?1:0);
            Set(t,"_UseAODarkening",On(s,"_UseAODarkening")?1:0);CopyFloat(s,"_AODarkStrength",t,"_AODarkStrength");CopyColor(s,"_AOColor",t,"_AODarkColor",Color.black,b);
            CopyFloat(s,"_AOContrast",t,"_RealtimeAOContrast");CopyFloat(s,"_AOLevel",t,"_RealtimeAOLevel");
            bool packed=F(s,"_PBRInputMode")>.5f;
            if(baked)
            {
                if(packed&&Assigned(s,"_PBRMap")&&(family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Environment))
                {
                    // Native packed B already supplies RGB gray visibility. A
                    // derived RFloat channel must never be assigned as RGB AO.
                }
                else if(packed&&Assigned(s,"_PBRMap"))
                {
                    Texture2D map=HoMigrationTextureBaker.Channel(s,"_PBRMap",2,folder,b);
                    string input=t.HasProperty("_OcclusionMap")?"_OcclusionMap":"_AOMap";
                    if(map!=null&&t.HasProperty(input)){t.SetTexture(input,map);ComposeST(s,"_PBRMap",t,input);}
                    if((family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye)&&!IdentityST(s,"_PBRMap"))
                        Block(b,"PACKED_AO_SHARED_UV: Skin/Eye AO shares BaseMap UV; the source extra packed map ST requires explicit resampling.");
                }
                else
                {
                    string input=t.HasProperty("_OcclusionMap")?"_OcclusionMap":"_AOMap";
                    CopyTexture(s,"_AOMap",t,input,false);SetST(t,input,Scale(s,"_MainTex"),Offset(s,"_MainTex"));
                }
            }
            Set(t,"_OcclusionSource",0);Set(t,"_OcclusionStrength",baked?1:0);
            if(source!=0&&Assigned(s,"_AOMask"))
            {
                bool spatialBaked=baked&&Assigned(s,packed?"_PBRMap":"_AOMap");
                if(spatialBaked)Block(b,"AO_MASK_BAKED: a live baked AO map and material mask require a separate static masked-AO bake; the realtime mask does not alter numeric baked AO.");
                if(realtime)
                {
                    if(t.HasProperty("_RealtimeAOMask"))
                    {
                        CopyTexture(s,"_AOMask",t,"_RealtimeAOMask",false);
                        Diff(d,"AO_REALTIME_MASK: native Ho realtime visibility uses the mask R at BaseMap UV, independent of baked AO/SSAO. Legacy toon shadow and accumulated-color darkening are replaced by the family's declared lighting model.");
                        SamplerDifference(s,"_AOMask",d);
                    }
                    else Block(b,"AO_MASK: active realtime material mask has no native Ho realtime mask input.");
                }
                else if(!spatialBaked)Diff(d,"AO_MASK_INERT_BAKED_WHITE: the unbound baked AO input is constant white; its assigned mask has no baked visibility effect.");
            }
            if(source==3&&F(s,"_AORealtimeWeight",1)!=1&&!t.HasProperty("_RealtimeAOStrength"))
                Diff(d,"REALTIME_AO_WEIGHT: target realtime AO does not expose the old mixed-source weight.");
            if(realtime&&(F(s,"_AOContrast",1)!=1||F(s,"_AOLevel")!=0)&&(!t.HasProperty("_RealtimeAOContrast")||!t.HasProperty("_RealtimeAOLevel")))
                Diff(d,"AO_REALTIME_REMAP_MODEL: this family consumes native Ho realtime visibility without the old contrast/level remap; source contrast="+F(s,"_AOContrast",1)+", level="+F(s,"_AOLevel")+". No absent parameter is claimed as copied.");
            if(F(s,"_AOMapLOD")!=0)Diff(d,"AO_FILTER: legacy gradient softening footprint differs from target sampling.");
            if(source!=0)Diff(d,"AO_CONSUMPTION: old RGB visibility drives toon shadow/optional accumulated-color darkening. Skin/Eye use indirect-only R visibility and Hair uses R-only color darkening; these are explicit new family-model choices.");
        }
        private static void MapEmission(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b,string folder,HashSet<string> decisions)
        {
            if(!On(s,"_UseEmission")){Set(t,"_UseEmission",0);return;}
            if(!t.HasProperty("_EmissionMap")){Block(b,"EMISSION_UNSUPPORTED: active emission has no input on "+family+".");return;}
            bool rgba=family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye;
            Texture2D map=HoMigrationTextureBaker.EmissionRgba(s,folder,!rgba,b,decisions.Contains("UseNativeUnboundEmissionSampler"));
            if(map!=null){t.SetTexture("_EmissionMap",map);SetST(t,"_EmissionMap",Vector2.one,Vector2.zero);}
            Color color=LinearColor(s,"_EmissionColor",Color.white);
            if(!rgba){color.r*=color.a;color.g*=color.a;color.b*=color.a;color.a=1;}
            SetLinearColor(t,"_EmissionColor",color,b);Set(t,"_UseEmission",1);Set(t,"_EmissionMainStrength",F(s,"_EmissionMainStrength"));
            StaticBake(d,"EMISSION_RGBA_MASK",s);
            if(F(s,"_EmissionFluorescence")!=0)Diff(d,"EMISSION_FLUORESCENCE: target radiance is independent of scene-light inversion; old Forward fluorescence is not reproduced.");
        }
        private static void MapTension(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b)
        {
            if(!On(s,"_HoTesionEnabled")||!s.IsKeywordEnabled("_HO_GD_TENSION")){Set(t,"_HoTesionEnabled",0);return;}
            if(family!=HoMigrationFamily.Cloth&&family!=HoMigrationFamily.Skin){Block(b,"TENSION_UNSUPPORTED: active GeometryData Tension requires Cloth or Skin.");return;}
            Set(t,"_HoTesionEnabled",1);CopyFloat(s,"_HoTesionStrength",t,"_HoTesionStrength");
            foreach(string prefix in new[]{"_HoTesionStretch","_HoTesionCompression"})
            {
                foreach(string suffix in new[]{"Strength","NormalScale"})CopyFloat(s,prefix+suffix,t,prefix+suffix);
                if(s.HasProperty(prefix+"Range"))t.SetVector(prefix+"Range",s.GetVector(prefix+"Range"));
                CopyColor(s,prefix+"Color",t,prefix+"Color",Color.white,b);CopyTexture(s,prefix+"BaseColor",t,prefix+"BaseColor",false);CopyTexture(s,prefix+"Normal",t,prefix+"Normal",false);
            }
            Diff(d,"TENSION_BINDING: current GeometryData material ABI retained; renderer high16 slot binding is migrated separately.");
        }
        private static void MapStencil(Material s,Material t,string prefix,List<string> b)
        {
            bool effective=F(s,prefix+"Comp",8)!=8||F(s,prefix+"Pass")!=0||F(s,prefix+"Fail")!=0||F(s,prefix+"ZFail")!=0;
            if(!t.HasProperty(prefix+"Ref")){if(effective)Block(b,"STENCIL_UNSUPPORTED: active "+prefix+" state is not represented by "+t.shader.name+".");return;}
            foreach(string suffix in StencilSuffixes)CopyFloat(s,prefix+suffix,t,prefix+suffix);
        }
        private static void MapOutline(Material s,Material t,HoMigrationFamily family,int mode,List<string> d,List<string> b,string folder,HashSet<string> decisions)
        {
            bool variant=s.shader.name.Contains("Outline");
            bool forward=variant&&s.GetShaderPassEnabled("FORWARD_OUTLINE")&&s.GetShaderPassEnabled("UniversalForward")&&s.GetShaderPassEnabled("UniversalForwardOnly");
            bool infrastructure=variant&&s.GetShaderPassEnabled("HO_OUTLINE_NORMAL_DEPTH")&&s.GetShaderPassEnabled("HoGeometryBufferOutlineNormalDepth");
            float width=F(s,"_OutlineWidth"),bias=F(s,"_OutlineZBias");
            bool deleted=width==0&&On(s,"_OutlineDeleteMesh");
            bool enabled=variant&&!deleted&&(forward||infrastructure);
            Set(t,"_OutlineEnabled",enabled?1:0);if(!enabled){Set(t,"_OutlineWidth",0);return;}
            if(width==0)
            {
                // The old inverted shell is not discarded when DeleteMesh is off.
                // In particular, two real Environment sources have zero expansion
                // but a .5 object-view displacement. A material alone cannot prove
                // even an undisplaced shell has no depth/stencil/Ho coverage.
                Block(b,"OUTLINE_ZERO_WIDTH_SHELL: old DeleteMesh=false retains a zero-expansion shell"+(bias!=0?" with object-view bias "+bias:"")+"; target zero-width suppression needs an explicit mesh/coverage retirement decision.");
            }
            if(family==HoMigrationFamily.Eye){Block(b,"OUTLINE_EYE: active source outline has no Eye pass; requires an explicit role/shell decision.");return;}
            if(!StandardOutlineBlend(s,mode,out string blendState))Block(b,"OUTLINE_CUSTOM_BLEND: native shell mapping requires the declared standard source blend; "+blendState);
            CopyColor(s,"_OutlineColor",t,"_OutlineColor",Color.black,b);MapStencil(s,t,"_OutlineStencil",b);
            bool objectGeometry=(family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Hair||family==HoMigrationFamily.Skin)&&t.HasProperty("_OutlineFixWidth");
            if(objectGeometry)
            {
                Set(t,"_OutlineWidthMode",family==HoMigrationFamily.Skin?2:1);
                foreach(string name in new[]{"_OutlineWidth","_OutlineFixWidth","_OutlineVertexR2Width","_OutlineDeleteMesh","_OutlineZBias"})CopyFloat(s,name,t,name);
                foreach(string name in new[]{"_OutlineWidthMask","_OutlineZBiasMask"})CopyTexture(s,name,t,name,false);
                if(mode==2)
                {
                    if(!decisions.Contains("UseNativeTransparentOutline"))Block(b,"TRANSPARENT_OUTLINE: select the explicit native transparent shell model; legacy alpha-squared blending/depth is not silently retained.");
                    else if(!t.HasProperty("_OutlineCoverageMap")||!t.HasProperty("_OutlineCutoff")||!t.HasProperty("_UseOutlineCoverageMap"))
                        Block(b,"OUTLINE_NATIVE_CAPABILITY: selected target version has no native independent transparent shell interface.");
                }
                Diff(d,"OUTLINE_OBJECT_GEOMETRY: width remains source hundredths of object units, including near-distance compensation, main-UV masks, vertex RGBA/GD and object-view bias. No guessed pixel conversion.");
            }
            else
            {
                Set(t,"_OutlineWidthMode",1);Set(t,"_OutlineWidthMeters",F(s,"_OutlineWidth")*.01f);
                if(family==HoMigrationFamily.Skin)
                {
                    CopyTexture(s,"_OutlineWidthMask",t,"_OutlineMask",false);SetST(t,"_OutlineMask",Scale(s,"_MainTex"),Offset(s,"_MainTex"));Set(t,"_OutlineMaskStrength",Assigned(s,"_OutlineWidthMask")?1:0);
                    int correction=Mathf.RoundToInt(F(s,"_OutlineVertexR2Width"));Set(t,"_OutlineNormalSource",correction==3?2:0);
                    if(correction==1||correction==2)Block(b,"OUTLINE_SKIN_VERTEX: old vertex red/RGBA width-direction input requires explicit mesh/role authoring for Skin.");
                }
                Block(b,"OUTLINE_SPACE: object-space width/fix-distance/bias cannot become "+family+" meters/pixels without renderer transform/mesh context. Preview .01*oldWidth is provisional, not equivalent.");
            }
            if(width!=0&&Assigned(s,"_OutlineVectorTex")&&F(s,"_OutlineVectorScale",1)!=0&&Mathf.RoundToInt(F(s,"_OutlineVertexR2Width"))!=2)
                Block(b,"OUTLINE_DIRECTION_TEXTURE: active old vector texture/UV mode is not consumed by the target; fixed VertexRGBA mode alone overrides that texture (GD can fall back).");
            bool rgb=OutlineTextureRgbEffective(s);
            bool alpha=mode!=0&&Mathf.RoundToInt(F(s,"_AlphaMaskMode"))!=1&&V(s,"_OutlineColor",new Vector4(0,0,0,1)).w!=0;
            bool assigned=Assigned(s,"_OutlineTex");
            Vector4 hsvg=V(s,"_OutlineTexHSVG",new Vector4(0,1,1,1));
            bool hsvgEffective=rgb&&hsvg!=new Vector4(0,1,1,1)&&(assigned||hsvg.z<1);
            if(assigned&&rgb)
            {
                if(t.HasProperty("_OutlineColorMap"))CopyTexture(s,"_OutlineTex",t,"_OutlineColorMap",false);
                else Block(b,"OUTLINE_TEXTURE: live old RGB color texture has no target outline-color slot.");
            }
            if(hsvgEffective)
            {
                if(t.HasProperty("_OutlineColorMap")&&!string.IsNullOrEmpty(folder))
                {
                    var map=HoMigrationTextureBaker.OutlineColor(s,folder,b);
                    if(map!=null){t.SetTexture("_OutlineColorMap",map);SetST(t,"_OutlineColorMap",Scale(s,"_OutlineTex"),Offset(s,"_OutlineTex"));}
                    StaticBake(d,"OUTLINE_HSVG",s);
                }
                else Block(b,"OUTLINE_HSVG: live outline tone correction requires a destination color-map slot and a dedicated bake folder.");
            }
            if(mode!=0)
            {
                if(t.HasProperty("_OutlineCoverageMap")&&!string.IsNullOrEmpty(folder))
                {
                    var coverage=HoMigrationTextureBaker.OutlineCoverage(s,folder,b);
                    if(coverage!=null)
                    {
                        t.SetTexture("_OutlineCoverageMap",coverage);SetST(t,"_OutlineCoverageMap",Vector2.one,Vector2.zero);
                        SetAlpha(t,"_OutlineColor",1);Set(t,"_OutlineCutoff",Mathf.Max(F(s,"_Cutoff",.5f),.001f));
                    }
                    StaticBake(d,"OUTLINE_COVERAGE_RAW_UV",s);
                    if(F(s,"_Cutoff",.5f)<.001f)Diff(d,"OUTLINE_NATIVE_FLOOR: old cutoff="+F(s,"_Cutoff",.5f)+" becomes native .001; zero/tiny shell depth/stencil/Ho coverage is intentionally retired.");
                }
                else if(assigned&&alpha)Block(b,"OUTLINE_ALPHA_TEXTURE: old outline alpha uses its own texture/ST and alpha-mask chain; the target RGB map/body coverage cannot preserve it implicitly.");
            }
            if(assigned&&!rgb&&!alpha)Diff(d,"OUTLINE_DEAD_TEXTURE: old outline RGB coefficients are zero and opaque/replace-alpha discards its alpha; the assigned color map has no effective output and is omitted. The black shell itself remains enabled.");
            if(Assigned(s,"_OutlineVectorTex")&&Mathf.RoundToInt(F(s,"_OutlineVertexR2Width"))==2)
                Diff(d,"OUTLINE_DEAD_VECTOR_TEXTURE: fixed VertexRGBA correction overrides the old vector texture before expansion; that texture is omitted.");
            Diff(d,"OUTLINE_SHADING: native unlit OutlineColor*OutlineColorMap replaces the legacy shell lighting/lit-color/shadow model; RGB maps retain their own raw-UV ST and are not composed with MainTex ST.");
            if(F(s,"_OutlineZTest",4)!=4||F(s,"_OutlineZWrite",1)!=1||On(s,"_OutlineAlphaToMask"))
                Diff(d,"OUTLINE_RASTER_POLICY: native shell uses the specialized LEqual/depth-write/alpha-to-coverage policy instead of saved old OutlineZTest="+F(s,"_OutlineZTest",4)+", OutlineZWrite="+F(s,"_OutlineZWrite",1)+", OutlineAlphaToMask="+F(s,"_OutlineAlphaToMask")+"; this is an explicit new raster policy, not an equivalent state copy.");
        }
        private static bool OutlineTextureRgbEffective(Material source)
        {
            Vector4 tint=V(source,"_OutlineColor",new Vector4(0,0,0,1));
            Vector4 lit=V(source,"_OutlineLitColor",Vector4.zero);
            float start=Mathf.Clamp01(F(source,"_OutlineLitOffset"))*lit.w;
            float end=Mathf.Clamp01(F(source,"_OutlineLitOffset")+F(source,"_OutlineLitScale",1))*lit.w;
            bool apply=F(source,"_OutlineLitApplyTex")!=0;
            // The texture coefficient is affine in the light factor. Both
            // endpoints must be zero in all channels to prove it is dead.
            foreach(float factor in F(source,"_OutlineLitShadowReceive")!=0?new[]{start,end,0f}:new[]{start,end})
                for(int channel=0;channel<3;channel++)
                    if(tint[channel]*(1-factor)+(apply?lit[channel]*factor:0)!=0)return true;
            return false;
        }
        private static bool StandardOutlineBlend(Material source,int mode,out string description)
        {
            float src=F(source,"_OutlineSrcBlend",mode==2?5:1),dst=F(source,"_OutlineDstBlend",mode==2?10:0);
            float srcA=F(source,"_OutlineSrcBlendAlpha",1),dstA=F(source,"_OutlineDstBlendAlpha",mode==2?10:0);
            float op=F(source,"_OutlineBlendOp"),opA=F(source,"_OutlineBlendOpAlpha");
            description="RGB="+src+"/"+dst+" op="+op+", alpha="+srcA+"/"+dstA+" op="+opA;
            // Legacy solid shells also serialize OneMinusSrcAlpha on the alpha
            // channel; with their opaque output it is neutral. Accept Zero too.
            return src==(mode==2?5:1)&&dst==(mode==2?10:0)&&srcA==1&&(dstA==10||mode!=2&&dstA==0)&&op==0&&opA==0;
        }
        private static void MapSss(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b,string folder,HashSet<string> decisions)
        {
            bool enabled=On(s,"_UseSSS");float thickness=Mathf.Clamp01(F(s,"_HoSurfaceThickness"));
            float curve=Mathf.Clamp01(Mathf.Abs(F(s,"_HoSurfaceCurvature"))),trans=Mathf.Clamp01(F(s,"_HoSurfaceTransmittanceHint"));
            if(enabled)
            {
                curve=Mathf.Max(curve,Mathf.Clamp01(F(s,"_HoSSSTransmissionStrength",1)*(.5f+.5f*F(s,"_SSSBorder",.35f)+.25f*F(s,"_SSSViewStrength",.5f))));
                trans=Mathf.Max(trans,Mathf.Clamp01(F(s,"_HoSSSTransmissionRadius",1)*.5f));
            }
            if(family!=HoMigrationFamily.Skin)
            {
                if(enabled||thickness>0||curve>0||trans>0)
                {
                    if(decisions.Contains("UseNativeNonSssSurface"))
                    {Set(t,"_HoSSSProfileId",0);Set(t,"_HoSurfaceThickness",0);Set(t,"_HoSurfaceCurvature",0);Set(t,"_HoSurfaceTransmittanceHint",0);}
                    else Block(b,"SSS_SURFACE_UNSUPPORTED: active classic/Ho thinness-profile-curvature-transmittance cannot be represented by "+family+" numeric surface.");
                }
                return;
            }
            Set(t,"_HoSSSProfileId",enabled?Mathf.Clamp(Mathf.RoundToInt(F(s,"_HoSSSProfileId")),0,255):0);
            Set(t,"_HoSurfaceCurvature",curve);Set(t,"_HoSurfaceTransmittanceHint",trans);Set(t,"_HoSSSThicknessScale",1);
            if(enabled&&Assigned(s,"_SSSThicknessMap"))
            {
                Texture2D map=HoMigrationTextureBaker.SssThinness(s,folder,b);
                if(map!=null){t.SetTexture("_SSSThicknessMap",map);Set(t,"_HoSurfaceThickness",1);}
                StaticBake(d,"HO_SSS_THINNESS",s);
            }
            else
            {
                float value=enabled?Mathf.Clamp01((On(s,"_SSSThicknessInvert")?0:1)*F(s,"_SSSStrength",.35f)*Mathf.Max(0,F(s,"_HoSSSThicknessScale",1))):0;
                Set(t,"_HoSurfaceThickness",Mathf.Max(thickness,value));
            }
            if(enabled)Diff(d,"SSS_FORWARD_MODEL: derived Ho profile/thinness hints retained; classic Forward SSS color/border/view/blur lobe is replaced by Skin plus Ho SSS, requiring visual review.");
        }
        private static void MapSemantics(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b)
        {
            Set(t,"_HoSurfaceMaterialClassId",F(s,"_HoSurfaceMaterialClassId"));Set(t,"_HoSemanticWeight",F(s,"_HoSemanticWeight",1));
            if(s.IsKeywordEnabled("_HO_SEMANTIC_MASK")&&Assigned(s,"_HoSemanticWeightTex"))
            {
                if(!t.HasProperty("_HoSemanticMask"))Block(b,"SEMANTIC_MASK: active source mask has no target input.");
                else
                {
                    CopyTexture(s,"_HoSemanticWeightTex",t,"_HoSemanticMask",false);
                    if(family==HoMigrationFamily.Cloth)SetST(t,"_HoSemanticMask",Scale(s,"_MainTex"),Offset(s,"_MainTex"));
                    else if(!IdentityST(s,"_MainTex"))Block(b,"SEMANTIC_UV: fixed target mask UV differs from source main UV.");
                }
            }
            if(family==HoMigrationFamily.Eye&&F(s,"_HoSemanticWeight",1)!=1)Diff(d,"EYE_SEMANTICS: Eye has capture/owner but not the source independent semantic-weight pass.");
        }
        private static void MapDecorative(Material s,Material t,HoMigrationFamily family,List<string> d,List<string> b)
        {
            if(On(s,"_UseMatCap"))
            {
                if(t.HasProperty("_MatCapMap"))
                {
                    CopyTexture(s,"_MatCapTex",t,"_MatCapMap",false);CopyColor(s,"_MatCapColor",t,"_MatCapColor",Color.white,b);
                    Set(t,"_UseMatCap",1);Set(t,"_MatCapStrength",F(s,"_MatCapBlend",1));
                    int blend=Mathf.RoundToInt(F(s,"_MatCapBlendMode",1));Set(t,"_MatCapBlend",blend==3?1:0);
                    Diff(d,"MATCAP_MODEL: old blend enum "+blend+" explicitly maps to "+(blend==3?"Multiply":"Add")+" approximation; UV/main-color/light/shadow/direction responses use the new model.");
                    if(Assigned(s,"_MatCapBlendMask"))Diff(d,"MATCAP_MASK: source independent spatial blend mask is absent from target MatCap model.");
                }
                else Diff(d,"MATCAP_REMOVED: active legacy MatCap is not part of selected "+family+" model.");
            }
            if(On(s,"_UseBacklight"))
            {
                Set(t,"_UseBacklight",1);CopyColor(s,"_BacklightColor",t,"_BacklightColor",Color.white,b);
                foreach(string name in new[]{"_BacklightMainStrength","_BacklightNormalStrength","_BacklightBorder","_BacklightBlur","_BacklightDirectivity","_BacklightViewStrength","_BacklightReceiveShadow","_BacklightBackfaceMask"})CopyFloat(s,name,t,name);
                CopyTexture(s,"_BacklightColorTex",t,"_BacklightColorTex",true);
                Diff(d,"BACKLIGHT_MODEL: old backlight is approximated by family thin-cloth/environment response, or absorbed into the new model when no independent controls exist.");
            }
            if(On(s,"_UseRim")||On(s,"_UseLightRim")||On(s,"_UseRimShade"))
            {
                Set(t,"_UseRim",On(s,"_UseRim")?1:0);Set(t,"_RimStrength",On(s,"_UseRim")?F(s,"_RimStrength",1):0);CopyColor(s,"_RimColor",t,"_RimColor",Color.white,b);
                foreach(var pair in new[]{("_RimWidth","_RimWidth"),("_RimSoftness","_RimSoftness"),("_RimDirStrength","_RimDirectionStrength"),("_RimDirStrength","_RimDirStrength"),
                    ("_RimMainStrength","_RimMainStrength"),("_RimNormalStrength","_RimNormalStrength"),("_RimEnableLighting","_RimEnableLighting"),("_RimShadowMask","_RimShadowMask"),("_RimBackfaceMask","_RimBackfaceMask")})CopyFloat(s,pair.Item1,t,pair.Item2);
                CopyTexture(s,"_RimColorTex",t,"_RimColorTex",true);Set(t,"_UseLightRim",On(s,"_UseLightRim")?1:0);
                Set(t,"_LightRimStrength",F(s,"_LightRimStrength",1));Set(t,"_HoRimStrength",On(s,"_UseLightRim")?F(s,"_LightRimStrength",1):0);
                Diff(d,"RIM_MODEL: AutoRim/LightRim/RimShade use target family response; spatial lighting/mask/geometry choices and separate RimShade are not claimed equivalent.");
            }
        }
        private static void CheckUnsupportedInputs(Material s,HoMigrationFamily family,int mode,List<string> d,List<string> b,HashSet<string> decisions)
        {
            SourceVariant(s,"_HoSemanticMaskOn","_HO_SEMANTIC_MASK",b);
            SourceVariant(s,"_HoTesionEnabled","_HO_GD_TENSION",b);
            if(s.shader.name.Contains("TwoPass")&&!decisions.Contains("ReplaceTwoPassTransparency"))Block(b,"TWO_PASS_TRANSPARENCY: source front/back ordering has two passes; one target pass needs explicit ordering/role review.");
            if(On(s,"_Invisible"))Block(b,"INVISIBLE_SOURCE: source discards rendering but target has no material invisible toggle.");
            if(On(s,"_UseParallax"))Block(b,"PARALLAX: active parallax/POM has no target equivalent.");
            if(On(s,"_UseDither"))Block(b,"DITHER: active old dither coverage has no target equivalent.");
            if(F(s,"_ZTest",4)!=4)Block(b,"DEPTH_TEST: custom source Forward depth comparison is not represented by the target.");
            if(F(s,"_ZClip",1)<.5f)Block(b,"DEPTH_CLIP: source disables depth clipping but target uses fixed clipping.");
            if(F(s,"_ColorMask",15)!=15)Block(b,"COLOR_MASK: custom source color-write mask has no target control.");
            if(F(s,"_OffsetFactor")!=0||F(s,"_OffsetUnits")!=0)Block(b,"RASTER_OFFSET: source polygon depth offset has no target control.");
            if(V(s,"_DissolveParams",Vector4.zero).x!=0)Block(b,"DISSOLVE: active old dissolve/animated coverage needs a separate implementation.");
            if(V(s,"_DistanceFade",Vector4.zero).z!=0)Block(b,"DISTANCE_FADE: distance-dependent shading cannot be baked as a static material texture.");
            Diff(d,"ROLE_SCOPE: exactly one chosen family is converted. Shared skin/eye/hair/cloth slots, renderer bindings, animation, scripts and MPBs require caller native reference/fork preflight.");
        }
        private static HashSet<string> ValidateModelDecisions(Material source,HoMigrationFamily family,string[] requested,List<string> differences,List<string> blockers)
        {
            var accepted=new HashSet<string>(StringComparer.Ordinal);
            if(requested==null)return accepted;
            bool outline=source.shader.name.Contains("Outline")&&
                ((source.GetShaderPassEnabled("FORWARD_OUTLINE")&&source.GetShaderPassEnabled("UniversalForward")&&source.GetShaderPassEnabled("UniversalForwardOnly"))||
                 (source.GetShaderPassEnabled("HO_OUTLINE_NORMAL_DEPTH")&&source.GetShaderPassEnabled("HoGeometryBufferOutlineNormalDepth")))&&
                !(F(source,"_OutlineWidth")==0&&On(source,"_OutlineDeleteMesh"));
            foreach(string code in requested)
            {
                string evidence;
                bool valid;
                switch(code)
                {
                    case "RetireZeroWidthOutline":
                        valid=outline&&F(source,"_OutlineWidth")==0&&F(source,"_OutlineDeleteMesh")==0;
                        evidence="requires an enabled old outline variant with exactly zero width and DeleteMesh=false; source width="+F(source,"_OutlineWidth")+", DeleteMesh="+F(source,"_OutlineDeleteMesh")+", object-view bias="+F(source,"_OutlineZBias")+". Explicitly retire its visible/depth/stencil/Ho shell output; no expanded outline is retired.";
                        break;
                    case "RetireEyeOutline":
                        valid=family==HoMigrationFamily.Eye&&outline;
                        evidence="requires Eye and an enabled old outline; target family="+family+", source="+source.shader.name+". Explicitly retire the Eye-role shell instead of inventing an Eye outline pass; other family roles retain their own outline policy.";
                        break;
                    case "UseNativeTransparentDepth":
                        valid=(family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Hair)&&SurfaceMode(source)==2&&F(source,"_ZWrite",0)>.5f;
                        evidence="requires transparent Cloth/Hair and source Forward ZWrite On; family="+family+", mode="+SurfaceMode(source)+", source ZWrite="+F(source,"_ZWrite",0)+". Adopt native transparent Forward ZWrite Off; Ho coverage/depth policy is validated separately.";
                        break;
                    case "ReplaceTwoPassTransparency":
                        valid=family!=HoMigrationFamily.Skin&&source.shader.name.Contains("TwoPass")&&SurfaceMode(source)==2;
                        evidence="requires an actual old TwoPass transparent source and a transparent-capable family; family="+family+", source="+source.shader.name+". Adopt one native transparent pass with Cull Off, replacing the old ordered back/front passes; alpha/blend/depth/Ho limitations remain independent.";
                        break;
                    case "PreserveHiddenCoverage":
                        valid=family==HoMigrationFamily.Cloth&&F(source,"_Invisible")!=0;
                        evidence="requires Cloth and a current nonzero _Invisible; family="+family+", source _Invisible="+F(source,"_Invisible")+". Preserve zero raster/Shadow/Ho/Meta coverage with native Cutout, BaseColor.a=0, AlphaMaskMode=0, Cutoff=.5, Outline/Emission Off. Old RT ignored _Invisible and could retain ghost occlusion/radiance; native alpha-any-hit now hides this submesh too. Main RGB/texture/ST, authored queue and metadata remain; invisible legacy decoration is not baked.";
                        break;
                    case "UseNativeNonSssSurface":
                        valid=family!=HoMigrationFamily.Skin&&(On(source,"_UseSSS")||Mathf.Clamp01(F(source,"_HoSurfaceThickness"))>0||Mathf.Clamp01(Mathf.Abs(F(source,"_HoSurfaceCurvature")))>0||Mathf.Clamp01(F(source,"_HoSurfaceTransmittanceHint"))>0);
                        evidence="requires a non-Skin family with effective old SSS/thinness/curvature/transmittance. Adopt the dedicated "+family+" non-SSS numeric surface (profile/thinness/curvature/transmittance zero); the Skin role retains its own SSS. This changes old shared-surface scattering rather than copying those inputs into an unrelated family.";
                        break;
                    case "UseNativeSkinF0":
                        float oldF0=Mathf.Clamp01(F(source,"_Reflectance",.04f));
                        valid=family==HoMigrationFamily.Skin&&oldF0>.08f&&!Assigned(source,"_ReflectanceTex")&&F(source,"_PBRInputMode")<.5f;
                        evidence="requires Skin with scalar F0 above .08 and no spatial/packed F0. Replace old absolute F0="+oldF0+" by native dielectric F0=.04 (weight=1); direct diffuse remaining changes from "+(1-oldF0)+" to .96. Base tint is retained without hiding this brightness/RT-reflectance redesign; direct/probe enable states remain independently mapped.";
                        break;
                    case "UseNativeTransparentOutline":
                        valid=(family==HoMigrationFamily.Cloth||family==HoMigrationFamily.Hair)&&SurfaceMode(source)==2&&outline&&StandardOutlineBlend(source,2,out _);
                        StandardOutlineBlend(source,2,out string sourceBlend);
                        evidence="requires an enabled transparent Cloth/Hair shell with standard SrcAlpha/Add RGB and One/Add alpha; source "+sourceBlend+". Use independent static outline coverage, a native .001 minimum floor, premultiplied One/OneMinusSrcAlpha RGB*alpha once, and transparent visible depth Off. Legacy alpha-squared blending and zero-alpha depth/stencil output are intentionally replaced; Ho front coverage remains opt-in.";
                        break;
                    case "UseNativeUnboundEmissionSampler":
                        valid=family==HoMigrationFamily.Cloth&&On(source,"_UseEmission")&&!Assigned(source,"_MainTex")&&Assigned(source,"_EmissionBlendMask");
                        evidence="requires Cloth emission with an unbound Main texture and assigned emission mask. Adopt the mask's explicit native importer sampler instead of guessing the engine's unbound Main sampler; only provably neutral white albedo influence may be folded. UV0/animation/range and other emission blockers remain enforced.";
                        break;
                    default:
                        Block(blockers,"MODEL_DECISION: unknown code '"+(code??"<null>")+"'; no generic blocker bypass is supported.");
                        continue;
                }
                if(!valid){Block(blockers,"MODEL_DECISION: "+code+" is outside its permitted condition: "+evidence);continue;}
                if(accepted.Add(code))Diff(differences,"MODEL_DECISION_"+code+": "+evidence);
            }
            return accepted;
        }
        private static void SourceVariant(Material source,string property,string keyword,List<string> blockers)
        {
            if(source.HasProperty(property)&&On(source,property)!=source.IsKeywordEnabled(keyword))
                Block(blockers,"SOURCE_VARIANT_STATE: "+property+"="+F(source,property)+" and actual "+keyword+"="+source.IsKeywordEnabled(keyword)+" disagree. Source is not repaired; resolve its effective variant before Apply.");
        }
        private static void MapMetadata(Material source,Material target,HoMigrationFamily family)
        {
            target.enableInstancing=source.enableInstancing;target.doubleSidedGI=source.doubleSidedGI;target.globalIlluminationFlags=source.globalIlluminationFlags;
            foreach(string tag in new[]{"HO_MaterialSource","HO_PMX_MaterialIndex"})
            {string value=source.GetTag(tag,false,string.Empty);if(!string.IsNullOrEmpty(value))target.SetOverrideTag(tag,value);}
            if(source.GetTag("HO_MaterialSource",false)=="PMX")target.SetOverrideTag("HO_PMX_MaterialFamily",family.ToString());
            foreach(var mapping in new[]{("SHADOW_CASTER","ShadowCaster","ShadowCaster"),("MOTIONVECTORS","MotionVectors","MotionVectors"),
                ("DEPTHONLY","DepthOnly","DepthOnly"),("DEPTHNORMALS","DepthNormals","DepthNormalsOnly"),
                ("HO_OBJECT_BUFFER","HO_OBJECT_BUFFER","HoObjectBuffer"),("HO_SURFACE_BUFFER","HO_SURFACE_BUFFER","HoSurfaceBuffer"),
                ("HO_GEOMETRY_BUFFER","HO_GEOMETRY_BUFFER","HoGeometryBuffer"),("HO_CHARACTER_CAPTURE","HO_CHARACTER_CAPTURE","HoCharacterCapture")})
            {
                // Only transfer an explicitly disabled effective source pass to a
                // real target pass. Old HDRP/missing-pass names are never copied.
                if(target.FindPass(mapping.Item2)>=0&&(!source.GetShaderPassEnabled(mapping.Item1)||!source.GetShaderPassEnabled(mapping.Item3)))
                    target.SetShaderPassEnabled(mapping.Item3,false);
            }
        }
        private static bool RgbPositive(Color color)=>Mathf.Max(color.r,Mathf.Max(color.g,color.b))>0;
        private static bool Casts(Material source)=>source.GetShaderPassEnabled("SHADOW_CASTER")&&source.GetShaderPassEnabled("ShadowCaster");
        private static int SurfaceMode(Material source)=>source.shader.name.Contains("Transparent")?2:source.shader.name.Contains("Cutout")?1:0;
        private static void Sync(Material material,HoMigrationFamily family,List<string> blockers)
        {
            string type=family switch
            {
                HoMigrationFamily.Cloth=>"HoShader.ClothURP.Editor.ClothMaterialUtility, HoShader.ClothURP.Editor",
                HoMigrationFamily.Hair=>"HoShader.HairURP.Editor.HairMaterialUtility, HoShader.HairURP.Editor",
                HoMigrationFamily.Skin=>"HoShader.SkinStylizedURP.Editor.SkinMaterialUtility, HoShader.SkinStylizedURP.Editor",
                HoMigrationFamily.Eye=>"HoShader.SkinStylizedURP.Editor.EyeMaterialUtility, HoShader.SkinStylizedURP.Editor",
                _=>"HoShader.EnvironmentURP.Editor.EnvironmentMaterialUtility, HoShader.EnvironmentURP.Editor"
            };
            Type gui=Type.GetType(type,false);
            MethodInfo sync=gui?.GetMethod("SyncMaterial",BindingFlags.Public|BindingFlags.Static)??gui?.GetMethod("SyncSurfaceMode",BindingFlags.Public|BindingFlags.Static);
            if(sync==null){Block(blockers,"TARGET_SYNC: missing public synchronizer "+type);return;}
            sync.Invoke(null,new object[]{material});
        }
        private static float F(Material material,string property,float fallback=0)=>material.HasProperty(property)?material.GetFloat(property):fallback;
        private static Vector4 V(Material material,string property,Vector4 fallback)=>material.HasProperty(property)?material.GetVector(property):fallback;
        private static bool On(Material material,string property,bool fallback=false)=>F(material,property,fallback?1:0)>.5f;
        private static Texture Texture(Material material,string property)=>material.HasProperty(property)?material.GetTexture(property):null;
        private static bool Assigned(Material material,string property)=>Texture(material,property)!=null;
        private static Vector2 Scale(Material material,string property)=>material.HasProperty(property)?material.GetTextureScale(property):Vector2.one;
        private static Vector2 Offset(Material material,string property)=>material.HasProperty(property)?material.GetTextureOffset(property):Vector2.zero;
        private static bool IdentityST(Material material,string property)=>Scale(material,property)==Vector2.one&&Offset(material,property)==Vector2.zero;
        private static void Set(Material material,string property,float value){if(material.HasProperty(property))material.SetFloat(property,value);}
        private static void SetST(Material target,string property,Vector2 scale,Vector2 offset){if(target.HasProperty(property)){target.SetTextureScale(property,scale);target.SetTextureOffset(property,offset);}}
        private static void CopyFloat(Material source,string from,Material target,string to){if(source.HasProperty(from))Set(target,to,source.GetFloat(from));}
        private static void CopyTexture(Material source,string from,Material target,string to,bool chainMain)
        {
            if(!target.HasProperty(to)||!source.HasProperty(from))return;
            target.SetTexture(to,source.GetTexture(from));
            if(chainMain)ComposeST(source,from,target,to);else SetST(target,to,Scale(source,from),Offset(source,from));
        }
        private static void ComposeST(Material source,string from,Material target,string to)
        {Vector2 scale=Scale(source,from);SetST(target,to,Vector2.Scale(Scale(source,"_MainTex"),scale),Vector2.Scale(Offset(source,"_MainTex"),scale)+Offset(source,from));}
        private static bool Hdr(Material material,string property)
        {int index=material.shader.FindPropertyIndex(property);return index>=0&&(material.shader.GetPropertyFlags(index)&ShaderPropertyFlags.HDR)!=0;}
        private static Color LinearColor(Material source,string property,Color fallback)
        {
            if(!source.HasProperty(property))return fallback;
            Color value=source.GetColor(property);
            return QualitySettings.activeColorSpace==ColorSpace.Linear&&!Hdr(source,property)?value.linear:value;
        }
        private static void SetLinearColor(Material target,string property,Color value,List<string> blockers)
        {
            if(!target.HasProperty(property))return;
            if(QualitySettings.activeColorSpace==ColorSpace.Linear&&!Hdr(target,property))
            {
                if(value.r<0||value.g<0||value.b<0)Block(blockers,"COLOR_RANGE: signed HDR cannot be represented by non-HDR "+property+".");
                float alpha=value.a;value=value.gamma;value.a=alpha;
            }
            target.SetColor(property,value);
        }
        private static void CopyColor(Material source,string from,Material target,string to,Color fallback,List<string> blockers)
        {if(source.HasProperty(from))SetLinearColor(target,to,LinearColor(source,from,fallback),blockers);}
        private static void SetAlpha(Material target,string property,float alpha){if(target.HasProperty(property)){Color color=target.GetColor(property);color.a=alpha;target.SetColor(property,color);}}
        private static void SamplerDifference(Material source,string property,List<string> differences)
        {
            var main=Texture(source,"_MainTex");var map=Texture(source,property);
            if(main!=null&&map!=null&&(main.filterMode!=map.filterMode||main.wrapModeU!=map.wrapModeU||main.wrapModeV!=map.wrapModeV))
                Diff(differences,"MAP_SAMPLER: "+property+" uses its own sampler instead of old MainTex state; authored filter/wrap difference requires visual review.");
        }
        private static void StaticBake(List<string> differences,string recipe,Material source)=>Diff(differences,"STATIC_BAKE_"+recipe+": LOD0 texel-center resampling of valid values; nonlinear filtering/mips can differ. Caller preflight must remap/block external old-property animation/script/MPB overrides before Apply (source "+source.name+").");
        private static void Diff(List<string> list,string text){if(!list.Contains(text))list.Add(text);}
        private static void Block(List<string> list,string text){if(!list.Contains(text))list.Add(text);}
    }
}
