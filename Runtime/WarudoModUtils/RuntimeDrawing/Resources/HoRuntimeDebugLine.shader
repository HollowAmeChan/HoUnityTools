Shader "Hidden/HoUnityTools/WarudoModUtils/DebugLine"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _LineWidth ("Line Width (Pixels)", Float) = 3
        [HideInInspector] _ZTest ("Depth Test", Float) = 8
        [HideInInspector] _ZWrite ("Depth Write", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 otherVertex : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float _LineWidth;

            v2f vert(appdata input)
            {
                v2f output;
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 worldOtherPosition = mul(unity_ObjectToWorld, float4(input.otherVertex, 1.0)).xyz;
                float4 positionClip = UnityWorldToClipPos(worldPosition);
                float4 otherPositionClip = UnityWorldToClipPos(worldOtherPosition);
                float2 positionNdc = positionClip.xy / max(positionClip.w, 0.00001);
                float2 otherPositionNdc = otherPositionClip.xy / max(otherPositionClip.w, 0.00001);
                float2 direction = otherPositionNdc - positionNdc;
                float directionLength = max(length(direction), 0.00001);
                direction /= directionLength;
                float2 side = float2(-direction.y, direction.x);
                float2 pixelToNdc = 2.0 / _ScreenParams.xy;
                float2 offsetNdc = side * input.uv.y * (_LineWidth * 0.5) * pixelToNdc;
                positionClip.xy += offsetNdc * positionClip.w;
                output.vertex = positionClip;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return input.color;
            }
            ENDCG
        }
    }
}
