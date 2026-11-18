Shader "Purgatory/Library Wood"
{
    Properties { [MainColor] _BaseColor("Wood",Color)=(.40,.25,.16,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            Varyings vert(Attributes a)
            {
                Varyings o; VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(a.normalOS);
                o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.positionWS.xz;
                float row=floor(p.x/.42);float along=p.y+fmod(abs(row),2)*1.9;
                float2 grid=float2(p.x/.42,along/3.8);
                float2 board=frac(grid),edge=min(board,1-board);
                float2 aa=max(fwidth(grid),.001);
                float2 seamInk=1-smoothstep(float2(.008,.002),float2(.008,.002)+aa,edge);
                float grain=noise(float2(p.x*19,p.y*.6))*.055+noise(float2(p.x*41,p.y*1.1))*.02;
                float variation=.94+.06*hash(float2(row,floor(along/3.8)));
                half3 wood=_BaseColor.rgb*(variation+grain);
                wood*=1-.11*max(seamInk.x,seamInk.y);
                half3 n=normalize(i.normalWS);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lighting=SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count=GetAdditionalLightsCount();
                for(uint index=0;index<count;index++)
                {
                    Light l=GetAdditionalLight(index,i.positionWS);
                    lighting+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation;
                }
                #endif
                return half4(MixFog(wood*max(lighting,.22),i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
