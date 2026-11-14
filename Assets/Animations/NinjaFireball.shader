Shader "Purgatory/Ninja Fireball"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS.xyz);
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half3 orange = half3(1.55, 0.36, 0.025);
                half3 glow = half3(2.8, 0.98, 0.09);
                return half4(lerp(orange, glow, pow(1.0 - facing, 2.0)), 1);
            }
            ENDHLSL
        }
    }
}
