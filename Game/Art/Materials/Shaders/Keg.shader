Shader "Custom/StylizedKeg"
{
    Properties
    {
        [Header(Stave Shape and Depth)]
        _StaveCount ("Stave Count (Planks)", Float) = 16.0
        _StaveCreaseDepth ("Stave Bevel Depth", Range(0.0, 1.0)) = 0.45
        _StaveCreaseWidth ("Stave Crease Sharpness", Range(0.01, 0.2)) = 0.06

        [Header(Wood Colors)]
        _WoodColorLit ("Wood Lit Tint", Color) = (0.58, 0.38, 0.22, 1.0)
        _WoodColorMid ("Wood Mid Tint", Color) = (0.42, 0.25, 0.14, 1.0)
        _WoodColorDark ("Wood Shadow Tint", Color) = (0.21, 0.11, 0.05, 1.0)
        _WoodGrainNoise ("Wood Grain Contrast", Range(0.0, 0.5)) = 0.15

        [Header(Metal Band Settings)]
        _MetalColorLit ("Metal Highlight", Color) = (0.82, 0.85, 0.88, 1.0)
        _MetalColorMid ("Metal Base", Color) = (0.45, 0.48, 0.53, 1.0)
        _MetalColorDark ("Metal Shadow", Color) = (0.18, 0.20, 0.24, 1.0)
        _BandMiddlePos ("Middle Band Y Center", Float) = 0.0
        _BandMiddleWidth ("Middle Band Width", Float) = 0.16
        _BandRimPos ("Top/Bottom Band Y Offset", Float) = 0.42
        _BandRimWidth ("Rim Band Width", Float) = 0.12
        _BandReliefStrength ("Band Normal Bevel Depth", Range(0.0, 1.5)) = 0.85
        _BandAOStrength ("Band Crevice Shadow", Range(0.0, 1.0)) = 0.65

        [Header(Toon Lighting and Specular)]
        _ShadowStep1 ("Shadow Threshold", Range(0.0, 1.0)) = 0.35
        _ShadowStep2 ("Midtone Threshold", Range(0.0, 1.0)) = 0.65
        _RampFeather ("Ramp Softness", Range(0.001, 0.15)) = 0.04
        _Glossiness ("Specular Sharpness", Range(4.0, 128.0)) = 32.0
        _SpecIntensity ("Specular Intensity", Range(0.0, 2.0)) = 0.75
        _RimColor ("Rim Light Tint", Color) = (1.0, 0.9, 0.65, 1.0)
        _RimThreshold ("Rim Cutoff", Range(0.0, 1.0)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

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
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float3 localPos   : TEXCOORD4;
                float2 uv         : TEXCOORD5;
            };

            CBUFFER_START(UnityPerMaterial)
                float _StaveCount;
                float _StaveCreaseDepth;
                float _StaveCreaseWidth;

                float4 _WoodColorLit;
                float4 _WoodColorMid;
                float4 _WoodColorDark;
                float _WoodGrainNoise;

                float4 _MetalColorLit;
                float4 _MetalColorMid;
                float4 _MetalColorDark;
                float _BandMiddlePos;
                float _BandMiddleWidth;
                float _BandRimPos;
                float _BandRimWidth;
                float _BandReliefStrength;
                float _BandAOStrength;

                float _ShadowStep1;
                float _ShadowStep2;
                float _RampFeather;
                float _Glossiness;
                float _SpecIntensity;
                float4 _RimColor;
                float _RimThreshold;
            CBUFFER_END

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.localPos = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * (input.tangentOS.w * GetOddNegativeScale());
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 geomNormalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS);
                float3 bitangentWS = normalize(input.bitangentWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                // --- 1. PROCEDURAL STAVE CARVING (Horizontal Normal Perturbation) ---
                // Angle around barrel circumference
                float angle = atan2(input.localPos.z, input.localPos.x); // [-PI, PI]
                float staveParam = (angle / 6.2831853 + 0.5) * _StaveCount;
                float staveFrac = frac(staveParam); // 0.0 to 1.0 across single stave

                // Bevel curve tilting left or right from stave center
                float staveCurve = (staveFrac - 0.5) * 2.0; // [-1 to +1]
                float staveEdgeMask = smoothstep(0.5 - _StaveCreaseWidth, 0.5, abs(staveFrac - 0.5));

                // --- 2. PROCEDURAL METAL BAND MASKS & HOOP RELIEF ---
                float y = input.localPos.y;

                float midDist = abs(y - _BandMiddlePos);
                float halfMidW = _BandMiddleWidth * 0.5;
                float midMask = step(midDist, halfMidW);

                float topDist = abs(y - _BandRimPos);
                float botDist = abs(y + _BandRimPos);
                float halfRimW = _BandRimWidth * 0.5;
                float topMask = step(topDist, halfRimW);
                float botMask = step(botDist, halfRimW);
                float rimMask = saturate(topMask + botMask);

                float isMetal = saturate(midMask + rimMask);

                // Normal slope along the top/bottom lips of the metal hoops (vertical tilt)
                float midLipSlope = (y - _BandMiddlePos) / max(halfMidW, 0.001);
                float topLipSlope = (y - _BandRimPos) / max(halfRimW, 0.001);
                float botLipSlope = (y + _BandRimPos) / max(halfRimW, 0.001);
                float bandNormalSlopeY = (midLipSlope * midMask) + (topLipSlope * topMask) + (botLipSlope * botMask);

                // Tangent-space normal synthesis:
                // X (tangent) carries stave tilt; Y (bitangent) carries hoop extrusion tilt
                float3 normalTS = float3(
                    staveCurve * _StaveCreaseDepth * (1.0 - isMetal * 0.7),
                    bandNormalSlopeY * _BandReliefStrength * isMetal,
                    1.0
                );
                normalTS = normalize(normalTS);

                // Transform altered normal into World Space
                float3 normalWS = normalize(
                    tangentWS * normalTS.x +
                    bitangentWS * normalTS.y +
                    geomNormalWS * normalTS.z
                );

                // --- 3. AMBIENT OCCLUSION & CAVITY SHADING ---
                // Crevice shadows directly above and below each metal band
                float aoMid = smoothstep(halfMidW, halfMidW + 0.05, midDist);
                float aoTop = smoothstep(halfRimW, halfRimW + 0.05, topDist);
                float aoBot = smoothstep(halfRimW, halfRimW + 0.05, botDist);
                float bandCreviceAO = lerp(1.0 - _BandAOStrength, 1.0, saturate(aoMid * aoTop * aoBot));

                // Groove shadows between staves
                float staveCreviceAO = lerp(1.0, 0.65, staveEdgeMask * (1.0 - isMetal));
                float totalAO = bandCreviceAO * staveCreviceAO;

                // --- 4. ALBEDO & PROCEDURAL MATERIAL VARIATION ---
                // Stave color variation per plank
                float staveID = floor(staveParam);
                float staveNoise = hash(float2(staveID, 13.73)) * 0.3 - 0.15;
                float grain = hash(floor(input.uv * float2(4.0, 32.0))) * _WoodGrainNoise;

                float3 woodBase = lerp(_WoodColorMid.rgb, _WoodColorLit.rgb, 0.35 + staveNoise + grain);
                float3 woodShad = _WoodColorDark.rgb * (1.0 + staveNoise * 0.5);

                float3 metalBase = _MetalColorMid.rgb;
                float3 metalShad = _MetalColorDark.rgb;

                float3 surfLit = lerp(woodBase, metalBase, isMetal);
                float3 surfShad = lerp(woodShad, metalShad, isMetal);

                // --- 5. 3-TONE CELL SHADING LIGHTING ---
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float NdotL = dot(normalWS, mainLight.direction);
                float halfLambert = NdotL * 0.5 + 0.5;

                // Attenuation & Shadows
                halfLambert *= mainLight.shadowAttenuation;

                float ramp1 = smoothstep(_ShadowStep1 - _RampFeather, _ShadowStep1 + _RampFeather, halfLambert);
                float ramp2 = smoothstep(_ShadowStep2 - _RampFeather, _ShadowStep2 + _RampFeather, halfLambert);

                // Blend from Shadow -> Midtone -> Highlight
                float3 litResult = lerp(surfShad, lerp(surfShad * 1.35, surfLit, ramp1), ramp2);
                litResult *= totalAO;

                // --- 6. SHARP TOON SPECULAR (High gloss on metal bands, subtle on polished wood) ---
                float3 halfVec = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfVec));
                float specSize = isMetal ? _Glossiness : _Glossiness * 0.35;
                float specRaw = pow(NdotH, specSize);
                float specToon = smoothstep(0.45, 0.55, specRaw) * _SpecIntensity * (isMetal ? 1.4 : 0.25);
                float3 specColor = isMetal ? _MetalColorLit.rgb : float3(1.0, 0.95, 0.85);

                // --- 7. STYLIZED RIM LIGHT ---
                float NdotV = 1.0 - saturate(dot(normalWS, viewDirWS));
                float rimRaw = pow(NdotV, 3.0);
                float rimToon = smoothstep(_RimThreshold - 0.05, _RimThreshold + 0.05, rimRaw) * ramp1;
                float3 rimFinal = rimToon * _RimColor.rgb * (isMetal ? 1.2 : 0.6);

                float3 finalRGB = (litResult * mainLight.color) + (specToon * specColor) + rimFinal;
                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
}