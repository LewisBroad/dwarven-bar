Shader "Custom/BeerSpill"
{
    Properties
    {
        [Header(Beer Liquid)]
        _BeerColor ("Ale Body Color", Color) = (0.95, 0.65, 0.15, 0.88)
        _FoamColor ("Foam Froth Color", Color) = (1.0, 0.96, 0.85, 0.95)
        _GlossColor ("Stylized Glint Color", Color) = (1.0, 1.0, 1.0, 0.85)

        [Header(Splash Shape)]
        _PuddleRadius ("Base Radius", Range(0.2, 0.38)) = 0.32
        _SplashDistortion ("Splash Lobes", Range(0.0, 0.35)) = 0.18
        _FoamClumpSize ("Foam Clump Frequency", Float) = 14.0
        _FoamAmount ("Foam Coverage", Range(0.0, 1.0)) = 0.35
        _Seed ("Random Seed Offset", Float) = 0.0

        [Header(Subtle Animation)]
        _WiggleSpeed ("Edge Ripple Speed", Range(0.0, 2.0)) = 0.65
        _WiggleIntensity ("Edge Ripple Amount", Range(0.0, 0.05)) = 0.012
        _FoamDriftSpeed ("Foam Drift Speed", Range(0.0, 1.0)) = 0.25
        _GlintPulseSpeed ("Glint Pulse Speed", Range(0.0, 3.0)) = 1.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline"="UniversalPipeline" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _BeerColor;
            float4 _FoamColor;
            float4 _GlossColor;

            float _PuddleRadius;
            float _SplashDistortion;
            float _FoamClumpSize;
            float _FoamAmount;
            float _Seed;

            float _WiggleSpeed;
            float _WiggleIntensity;
            float _FoamDriftSpeed;
            float _GlintPulseSpeed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise2D(float2 uv)
            {
                float2 id = floor(uv);
                float2 lv = frac(uv);
                lv = lv * lv * (3.0 - 2.0 * lv);

                float bl = hash21(id);
                float br = hash21(id + float2(1, 0));
                float tl = hash21(id + float2(0, 1));
                float tr = hash21(id + float2(1, 1));

                return lerp(lerp(bl, br, lv.x), lerp(tl, tr, lv.x), lv.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // 1. HARD SAFETY BOUNDARY:
                float2 center = float2(0.5, 0.5);
                float2 dir = uv - center;
                float dist = length(dir);

                if (dist > 0.46)
                {
                    discard;
                }

                // 2. TIME-BASED SUBTLE ANIMATION:
                // _Time.y is elapsed time in seconds
                float tEdge = _Time.y * _WiggleSpeed;
                float tDrift = _Time.y * _FoamDriftSpeed;

                // Desynchronize noise coordinate sampling with time + seed
                float2 seededUV = uv + float2(_Seed * 1.37, _Seed * 2.81);
                float angle = atan2(dir.y, dir.x);

                // Very gentle breathing ripple along splash lobes
                float ripple = sin(angle * 4.0 + tEdge) * cos(angle * 2.0 - tEdge * 0.7) * _WiggleIntensity;

                // Static splash lobe geometry + dynamic ripple
                float splashLobes = sin(angle * 3.0 + _Seed) * 0.35 + cos(angle * 5.0 + 1.2 + _Seed) * 0.25;
                float noiseWarp = (noise2D(seededUV * 6.0) - 0.5) * _SplashDistortion;

                float dynamicRadius = _PuddleRadius + (splashLobes * 0.06) + noiseWarp + ripple;
                dynamicRadius = clamp(dynamicRadius, 0.15, 0.44);

                if (dist > dynamicRadius)
                {
                    discard;
                }

                // 3. FOAM WITH DRIFTING MICRO-BUBBLES:
                // Slowly scroll UVs for foam bubbles so froth feels active
                float2 foamDriftUV = seededUV + float2(sin(tDrift * 0.5), cos(tDrift * 0.6)) * 0.05;
                float foamNoise = noise2D(foamDriftUV * _FoamClumpSize);

                float edgeFactor = smoothstep(dynamicRadius - 0.07, dynamicRadius, dist);
                float foamMask = step(1.0 - (_FoamAmount * 0.65), foamNoise * edgeFactor * 1.5);

                // Floating bubble islands with subtle wander
                float bubbleNoise = noise2D((seededUV + float2(tDrift * 0.03, -tDrift * 0.02)) * 18.0);
                if (bubbleNoise > 0.90 && dist < dynamicRadius * 0.65)
                {
                    foamMask = 1.0;
                }

                // 4. COLOR COMPOSITION:
                float4 finalColor = _BeerColor;

                if (foamMask > 0.5)
                {
                    finalColor = _FoamColor;
                }

                // 5. CARTOON SPECULAR HIGHLIGHT WITH SLOW GLINT PULSE:
                float glintPulse = 0.85 + 0.15 * sin(_Time.y * _GlintPulseSpeed + _Seed);

                float2 glintPos1 = (uv - float2(0.40, 0.60)) * float2(2.5, 6.0);
                float glint1 = 1.0 - step(0.05, length(glintPos1));

                float2 glintPos2 = (uv - float2(0.33, 0.65)) * float2(4.5, 4.5);
                float glint2 = 1.0 - step(0.025, length(glintPos2));

                float totalGlint = saturate(glint1 + glint2) * (1.0 - foamMask) * glintPulse;
                finalColor.rgb = lerp(finalColor.rgb, _GlossColor.rgb, totalGlint * _GlossColor.a);

                return finalColor;
            }
            ENDHLSL
        }
    }
}