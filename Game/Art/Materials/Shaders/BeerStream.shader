Shader "Custom/BeerStream"
{
    Properties
    {
        _BeerColor ("Beer Color", Color) = (0.85, 0.45, 0.05, 1.0)
        _FoamColor ("Foam Color", Color) = (1.0, 0.95, 0.85, 1.0)
        _HighlightColor ("Highlight Color", Color) = (1.0, 1.0, 1.0, 0.8)
        
        _FlowSpeed ("Flow Speed", Float) = 8.0
        _WobbleSpeed ("Wobble Speed", Float) = 25.0
        _WobbleStrength ("Wobble Strength", Float) = 0.015
        
        // Where the foam starts blending in (0 = very bottom, 1 = top)
        _FoamPoint ("Foam Threshold", Range(0, 1)) = 0.15 
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalPipeline" 
            "Queue"="Geometry" 
        }

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

            float4 _BeerColor;
            float4 _FoamColor;
            float4 _HighlightColor;
            float _FlowSpeed;
            float _WobbleSpeed;
            float _WobbleStrength;
            float _FoamPoint;

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // VERTEX WOBBLE: The further down the cylinder (UV.y -> 0), the more it wobbles
                float verticalFall = 1.0 - input.uv.y; 
                
                float waveX = sin(_Time.y * _WobbleSpeed + (input.positionOS.y * 15.0));
                float waveZ = cos(_Time.y * _WobbleSpeed * 0.8 + (input.positionOS.y * 15.0));
                
                input.positionOS.x += waveX * _WobbleStrength * verticalFall;
                input.positionOS.z += waveZ * _WobbleStrength * verticalFall;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.uv         = input.uv;
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 1. SCROLLING FLOW BANDS
                float flowPattern = sin((input.uv.y - (_Time.y * _FlowSpeed)) * 30.0);
                float flowBands = smoothstep(0.0, 1.0, flowPattern) * 0.15; 

                // 2. FOAM GRADIENT
                float isFoam = smoothstep(_FoamPoint + 0.1, _FoamPoint - 0.1, input.uv.y);
                float3 baseColor = lerp(_BeerColor.rgb, _FoamColor.rgb, isFoam);
                
                // Add scrolling internal waves (only to liquid, not foam)
                baseColor += flowBands * (1.0 - isFoam); 

                // 3. FAKE CEL-SHADED SPECULAR SHINE
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                float NdotV = 1.0 - saturate(dot(normalize(input.normalWS), viewDirWS));
                float shine = step(0.75, NdotV); // Sharp cartoon cutoff
                
                float3 finalColor = lerp(baseColor, _HighlightColor.rgb, shine * _HighlightColor.a);

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}