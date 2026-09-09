Shader "Custom/StylizedBeerLiquid"
{
    Properties
    {
        [Header(Ale Colors)]
        _BeerColor ("Beer Body Color", Color) = (0.95, 0.65, 0.12, 1.0)
        _DeepColor ("Deep Malt Color", Color) = (0.55, 0.22, 0.04, 1.0)
        _FoamColor ("Foam Head Color", Color) = (1.0, 0.98, 0.88, 1.0)
        _RimColor  ("Rim Glint Color", Color) = (1.0, 1.0, 1.0, 0.75)

        [Header(Fluid Surface)]
        _FillLevel ("Fill Level (0 to 1)", Range(0, 1)) = 0.8
        _TiltX ("Local Tilt X", Float) = 0.0
        _TiltZ ("Local Tilt Z", Float) = 0.0
        _FoamThickness ("Foam Thickness", Range(0.005, 0.08)) = 0.025
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry" 
            "RenderPipeline"="UniversalPipeline" 
        }

        Cull Off // Ensure we see the inside top surface of the beer

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            float4 _BeerColor;
            float4 _DeepColor;
            float4 _FoamColor;
            float4 _RimColor;

            float _FillLevel;
            float _TiltX;
            float _TiltZ;
            float _FoamThickness;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Standard Unity cylinder primitive extends from -1 to +1 in Y
                // Convert to normalized 0.0 (base) to 1.0 (rim)
                float normalizedY = (input.positionOS.y + 1.0) * 0.5;

                // Tilts the fluid surface inside the local cylinder.
                // input.positionOS.x and z range from -0.5 to +0.5.
                float surfaceCutoff = _FillLevel + (input.positionOS.x * _TiltX) + (input.positionOS.z * _TiltZ);

                // 1. DISCARD ABOVE SURFACE
                if (normalizedY > surfaceCutoff)
                {
                    discard;
                }

                // 2. FOAM CAP
                // Highlights vertices right at the surface boundary
                float distFromTop = surfaceCutoff - normalizedY;
                float isFoam = step(distFromTop, _FoamThickness);

                // 3. COLOR GRADIENT (Dark malt bottom -> golden ale top)
                float depthFactor = saturate(normalizedY * 1.4);
                float3 aleRgb = lerp(_DeepColor.rgb, _BeerColor.rgb, depthFactor);
                float3 finalRgb = lerp(aleRgb, _FoamColor.rgb, isFoam);

                // 4. CLEAN CARTOON SPECULAR EDGE
                float3 viewDirWS = normalize(GetCameraPositionWS() - TransformObjectToWorld(input.positionOS));
                float fresnel = 1.0 - saturate(dot(normalize(input.normalWS), viewDirWS));
                float cartoonRim = step(0.72, fresnel) * (1.0 - isFoam);

                finalRgb = lerp(finalRgb, _RimColor.rgb, cartoonRim * _RimColor.a);

                return float4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
}