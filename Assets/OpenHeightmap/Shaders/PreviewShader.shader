/*
 * Asset: OpenHeightmap
 * Filename: PreviewShader.shader
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

Shader "OpenHeightmap/PreviewShader"
{
    Properties
    {
        _MainTex ("Heightmap", 2D) = "white" {}
        _MinHeight ("Min Height", Float) = 0
        _MaxHeight ("Max Height", Float) = 1
        _ColorMode ("Color Mode", Float) = 0
        _TriplanarBlend ("Triplanar Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _MinHeight;
            float _MaxHeight;
            float _ColorMode;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 HeightToColor(float h)
            {
                if (h < 0.1) return lerp(float3(0.0, 0.0, 0.3), float3(0.0, 0.1, 0.5), h / 0.1);
                if (h < 0.2) return lerp(float3(0.0, 0.1, 0.5), float3(0.1, 0.4, 0.7), (h - 0.1) / 0.1);
                if (h < 0.3) return lerp(float3(0.76, 0.70, 0.50), float3(0.60, 0.65, 0.35), (h - 0.2) / 0.1);
                if (h < 0.5) return lerp(float3(0.20, 0.55, 0.15), float3(0.15, 0.40, 0.10), (h - 0.3) / 0.2);
                if (h < 0.7) return lerp(float3(0.45, 0.40, 0.35), float3(0.55, 0.50, 0.45), (h - 0.5) / 0.2);
                if (h < 0.85) return lerp(float3(0.55, 0.50, 0.45), float3(0.65, 0.62, 0.60), (h - 0.7) / 0.15);
                return lerp(float3(0.65, 0.62, 0.60), float3(0.95, 0.95, 0.97), (h - 0.85) / 0.15);
            }

            float4 frag (v2f i) : SV_Target
            {
                float h = tex2D(_MainTex, i.uv).r;
                float normalizedH = saturate((h - _MinHeight) / max(_MaxHeight - _MinHeight, 0.0001));

                float3 color;
                if (_ColorMode < 0.5)
                {
                    color = HeightToColor(normalizedH);
                }
                else
                {
                    color = float3(normalizedH, normalizedH, normalizedH);
                }

                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
