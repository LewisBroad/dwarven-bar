Shader "Custom/PBRDirtyGlass"
{
    Properties
    {
        [Header(Clean Base Glass)]
        _BaseColor ("Glass Tint (Clean)", Color) = (0.784, 0.902, 0.941, 0.15) // #C8E6F0 with low alpha
        _Smoothness ("Clean Smoothness", Range(0.0, 1.0)) = 0.95

        [Header(Beer Dirt Stains)]
        _DirtIntensity ("Dirt Amount", Range(0.0, 1.0)) = 0.85
        _StainColor ("Beer Residue Color", Color) = (0.35, 0.20, 0.05, 0.85) // Amber-brown ale stain
        _FoamLineColor ("Dried Foam Rings", Color) = (0.90, 0.86, 0.72, 0.90) // Pale dried froth
        _StainRoughness ("Stain Smoothness", Range(0.0, 0.4)) = 0.1

        [Header(Pattern Controls)]
        _Tiling ("Noise Tiling", Float) = 5.0
        _FoamLinesCount ("Foam Line Count", Float) = 4.0
        _BottomPoolHeight ("Bottom Dregs Fill", Range(0.0, 0.4)) = 0.15
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline"="UniversalPipeline" 
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Smoothness;
                float _DirtIntensity;
                float4 _StainColor;
                float4 _FoamLineColor;
                float _StainRoughness;
                float _Tiling;
                float _FoamLinesCount;
                float _BottomPoolHeight;
            CBUFFER_END

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i + float2(0,0)), hash(i + float2(1,0)), f.x),
                            lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // 1. Procedural dirt shapes: streaks, bottom pool, dried rings
                float2 uv = input.uv * _Tiling;
                float splotches = noise(uv * float2(1.0, 0.5)) * 0.6 + noise(uv * 3.0) * 0.4;
                float bottomPool = smoothstep(_BottomPoolHeight, 0.0, input.uv.y);

                // Horizontal tide lines where foam dried as beer was drunk
                float foamSine = sin(input.uv.y * _FoamLinesCount * 6.2831853);
                float foamRings = pow(saturate(foamSine), 10.0) * noise(float2(input.uv.x * 12.0, 0.0));

                // Composite dirt masks
                float stainMask = saturate((splotches * 0.7 + bottomPool * 0.8) * _DirtIntensity);
                stainMask = smoothstep(0.3, 0.75, stainMask);

                float foamMask = saturate(foamRings * 2.5 * _DirtIntensity);
                float totalDirt = saturate(stainMask + foamMask);

                // 2. Blend clean glass (#C8E6F0) with dried beer colors
                float3 albedo = _BaseColor.rgb;
                albedo = lerp(albedo, _StainColor.rgb, stainMask);
                albedo = lerp(albedo, _FoamLineColor.rgb, foamMask);

                // Opacity: clean is low alpha; stains are thicker/cloudier
                float alpha = lerp(_BaseColor.a, saturate(_BaseColor.a + 0.65), totalDirt);

                // Smoothness: dirty patches become matte
                float smoothness = lerp(_Smoothness, _StainRoughness, totalDirt);

                // 3. Standard URP Lit PBR Lighting
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = albedo * mainLight.color * (NdotL * mainLight.shadowAttenuation + 0.25);

                // Specular highlight
                float3 halfVec = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfVec));
                float specPower = exp2(smoothness * 10.0 + 1.0);
                float spec = pow(NdotH, specPower) * smoothness * mainLight.shadowAttenuation;

                // Fresnel edge glow for glass look
                float NdotV = 1.0 - saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(NdotV, 3.0) * 0.5;

                float3 finalRGB = diffuse + (spec * mainLight.color) + (fresnel * float3(1, 1, 1) * (1.0 - totalDirt * 0.5));
                float finalAlpha = saturate(alpha + fresnel * 0.4);

                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}