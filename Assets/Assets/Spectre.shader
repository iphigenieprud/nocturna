Shader "Custom/Spectre"
{
    Properties
    {
        [Header(Body)]
        _Color ("Body Color", Color) = (0.03, 0.03, 0.05, 0.63)
        [HDR] _RimColor ("Glow Color", Color) = (0.8, 0.85, 1.0, 0.4)
        _RimPower ("Glow Sharpness", Range(0.5, 8)) = 2.5
        _RimIntensity ("Glow Brightness", Range(0, 6)) = 0.8

        [Header(Drifting Fog)]
        _SmokeScale ("Fog Size", Range(0.5, 10)) = 2.5
        _SmokeSpeed ("Fog Speed", Range(0, 2)) = 0.4
        _SmokeAmount ("Fog Eats Body", Range(0, 0.8)) = 0.3
        _SmokeSoftness ("Fog Softness", Range(0.02, 0.5)) = 0.18
        [HDR] _SmokeEdgeColor ("Fog Edge Color", Color) = (0.9, 0.95, 1.2, 1)
        _SmokeEdgeGlow ("Fog Edge Glow", Range(0, 3)) = 1

        [Header(Inner Light)]
        [HDR] _InnerColor ("Inner Light Color", Color) = (0.6, 0.7, 1.0, 1)
        _InnerIntensity ("Inner Light Strength", Range(0, 3)) = 1

        [Header(Shape)]
        _FadeHeight ("Feet Fade Height", Range(0, 2)) = 0.8

        [Header(Dissolve)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        [HDR] _EdgeColor ("Dissolve Edge Color", Color) = (2.5, 2.6, 3, 1)
        _EdgeWidth ("Dissolve Edge Width", Range(0.01, 0.3)) = 0.08

        [Header(Overall)]
        _Visibility ("Visibility", Range(0, 1)) = 1
        _FogIgnore ("Ignore Scene Fog", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _RimColor;
            half _RimPower;
            half _RimIntensity;
            half _SmokeScale;
            half _SmokeSpeed;
            half _SmokeAmount;
            half _SmokeSoftness;
            half4 _SmokeEdgeColor;
            half _SmokeEdgeGlow;
            half4 _InnerColor;
            half _InnerIntensity;
            half _FadeHeight;
            half _Dissolve;
            half4 _EdgeColor;
            half _EdgeWidth;
            half _Visibility;
            half _FogIgnore;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float  fogFactor  : TEXCOORD2;
        };

        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise3(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);

            return lerp(
                lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                     lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                     lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y),
                f.z);
        }

        // Layered noise for soft, cloudy fog
        float Fbm(float3 p)
        {
            float v = Noise3(p) * 0.5;
            v += Noise3(p * 2.1 + 13.7) * 0.25;
            v += Noise3(p * 4.3 + 31.1) * 0.125;
            return v / 0.875;
        }

        float3 ObjPos()
        {
            return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
        }

        // Two fog layers drifting in different directions, so it swirls
        float SmokeValue(float3 positionWS)
        {
            float3 rel = positionWS - ObjPos();
            float t = _Time.y * _SmokeSpeed;
            float a = Fbm(rel * _SmokeScale + float3(t * 0.3, -t, t * 0.2));
            float b = Fbm(rel * _SmokeScale * 1.7 + float3(-t * 0.25, -t * 0.6, -t * 0.4) + 50.0);
            return (a + b) * 0.5;
        }

        float SmokeMask(float s)
        {
            return smoothstep(_SmokeAmount - _SmokeSoftness, _SmokeAmount + _SmokeSoftness, s);
        }

        float DissolveValue(float3 positionWS)
        {
            float3 p = (positionWS - ObjPos()) * 3.0;
            return Noise3(p) * 0.65 + Noise3(p * 2.7) * 0.35;
        }

        float FeetFade(float3 positionWS)
        {
            if (_FadeHeight <= 0.001) return 1.0;
            float h = positionWS.y - ObjPos().y;
            float n = Noise3(positionWS * 4.0 + float3(0, _Time.y * 0.3, 0));
            return saturate((h + (n - 0.5) * 0.3 * _FadeHeight) / _FadeHeight);
        }

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL

        // Pass 1: depth only, so you never see inside her body
        Pass
        {
            Name "SpectreDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragDepth

            half4 FragDepth(Varyings i) : SV_Target
            {
                if (_Dissolve > 0.001) clip(DissolveValue(i.positionWS) - _Dissolve);
                clip(SmokeMask(SmokeValue(i.positionWS)) - 0.3);
                clip(FeetFade(i.positionWS) - 0.3);
                clip(_Visibility - 0.01);
                return 0;
            }
            ENDHLSL
        }

        // Pass 2: the foggy spectre
        Pass
        {
            Name "SpectreForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            half4 Frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float ndv = saturate(dot(N, V));

                // Glowing outline
                float rim = pow(1.0 - ndv, _RimPower);

                // Drifting fog that eats away parts of her
                float smoke = SmokeValue(i.positionWS);
                float mask = SmokeMask(smoke);
                float fogEdge = saturate(mask * (1.0 - mask) * 4.0);

                // Light deep inside
                float inner = pow(ndv, 2.0) * _InnerIntensity * smoke;

                float feet = FeetFade(i.positionWS);

                float3 col = _Color.rgb
                           + _RimColor.rgb * rim * _RimIntensity
                           + _InnerColor.rgb * inner
                           + _SmokeEdgeColor.rgb * fogEdge * _SmokeEdgeGlow;

                float a = saturate(_Color.a + rim * _RimColor.a + inner * 0.3);
                a = a * mask + fogEdge * 0.4 * _SmokeEdgeGlow;

                // Dissolve
                if (_Dissolve > 0.001)
                {
                    float d = DissolveValue(i.positionWS) - _Dissolve;
                    clip(d);
                    float edge = 1.0 - saturate(d / _EdgeWidth);
                    col += _EdgeColor.rgb * edge;
                    a = max(a, edge);
                }

                a = saturate(a) * feet * _Visibility;

                // Stay dark in the scene fog instead of fading to gray
                col = lerp(MixFog(col, i.fogFactor), col, _FogIgnore);
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}