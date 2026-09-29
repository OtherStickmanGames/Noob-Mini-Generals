// Шейдер воксельной арены без текстур.
// Цвет вершины: r — индекс в палитре (_VoxelPaletteTex, 256x1), g — затенение угла.
// Палитра и размер вокселя задаются глобально из VoxelArena.
Shader "NoobGenerals/VoxelArena"
{
    Properties
    {
        _TintStrength ("Разброс оттенка вокселей", Range(0, 0.3)) = 0.06
        _AOStrength ("Затенение углов", Range(0, 1)) = 0.65
        _AmbientStrength ("Сила окружающего света", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _TintStrength;
            half _AOStrength;
            half _AmbientStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_VoxelPaletteTex);
            float _VoxelSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                int paletteIndex = (int)round(input.color.r * 255.0);
                half3 albedo = LOAD_TEXTURE2D(_VoxelPaletteTex, int2(paletteIndex, 0)).rgb;

                // Свой оттенок у каждого вокселя: меш склеен, а воксели всё равно читаются
                float3 voxel = floor(input.positionWS / _VoxelSize - normalWS * 0.5);
                albedo *= 1.0 + (Hash(voxel) - 0.5) * 2.0 * _TintStrength;

                half ao = lerp(1.0, input.color.g, _AOStrength);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuseTerm = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = mainLight.color * diffuseTerm * mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normalWS) * _AmbientStrength;

                half3 color = albedo * (diffuse + ambient) * ao;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Shadows.hlsl использует LerpWhiteTo из CommonMaterial.hlsl, но сам его не подключает
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
