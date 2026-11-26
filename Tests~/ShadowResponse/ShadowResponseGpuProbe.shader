Shader "Hidden/HoShadowResponse/GpuProbe"
{
    Properties { _Lookup("Actual Baked LUT",2D)="white"{} }
    SubShader { Pass { Cull Off ZWrite Off ZTest Always
        HLSLPROGRAM
        #pragma target 4.5
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        #include "Assets/HoShadowResponseSnapshot/Effect_URP/Runtime/Shaders/effect_blend.hlsl"
        Texture2D _Lookup; SamplerState sampler_LinearClamp;
        float4 _Lookup_TexelSize, _Bright, _First, _Second, _Third, _Base;
        float4 _Borders, _Blurs;
        float _Receive, _Cast, _Mode, _Output, _Blend, _Strength, _Shift;
        // Frozen old EffectBand, with AA retained as a separately measured witness.
        float OldBand(float value,float border,float blur)
        {
            float softness=max(blur*.5,max(fwidth(value)*.5,.0001));
            return smoothstep(border-softness,border+softness,value);
        }
        float FixedBand(float value,float border,float blur)
        {
            float softness=max(blur*.5,.0001);
            return smoothstep(border-softness,border+softness,value);
        }
        float4 OldResponse(float t,bool aa)
        {
            float first=aa?OldBand(t,_Borders.x,_Blurs.x):FixedBand(t,_Borders.x,_Blurs.x);
            float second=aa?OldBand(t,min(_Borders.y,_Borders.x),_Blurs.y):FixedBand(t,min(_Borders.y,_Borders.x),_Blurs.y);
            float third=aa?OldBand(t,min(_Borders.z,min(_Borders.y,_Borders.x)),_Blurs.z):FixedBand(t,min(_Borders.z,min(_Borders.y,_Borders.x)),_Blurs.z);
            float3 shade=lerp(_Bright.rgb,_First.rgb,(1-first)*saturate(_First.a));
            shade=lerp(shade,_Second.rgb,(1-second)*saturate(_Second.a));
            shade=lerp(shade,_Third.rgb,(1-third)*saturate(_Third.a));
            return float4(shade,first);
        }
        float4 NewResponse(float coordinate,bool centered)
        {
            coordinate=saturate(coordinate);
            float width=max(_Lookup_TexelSize.z,1.0);
            float2 uv=float2(centered?(0.5+coordinate*(width-1.0))/width:coordinate,.5);
            return _Lookup.SampleLevel(sampler_LinearClamp,uv,0);
        }
        float3 OuterBlend(float3 baseColor,float3 shade)
        {
            if((int)round(_Blend)==10)
            {
                float peak=max(1,max(baseColor.r,max(baseColor.g,baseColor.b)));
                return float3(SoftLight(baseColor/peak,shade))*peak;
            }
            return ApplyLayerBlend(baseColor,shade,_Blend);
        }
        float4 frag(v2f_img i):SV_Target
        {
            float halfLambert=(i.uv.x*4096.0-.5)/4095.0+_Shift;
            float coordinate=saturate(halfLambert)*lerp(1,_Cast,saturate(_Receive));
            float4 response=_Mode==0?NewResponse(coordinate,true):_Mode==1?OldResponse(coordinate,false):_Mode==2?OldResponse(coordinate,true):NewResponse(coordinate,false);
            return _Output>.5?float4(lerp(_Base.rgb,OuterBlend(_Base.rgb,response.rgb),_Strength),response.a):response;
        }
        ENDHLSL
    } }
}
