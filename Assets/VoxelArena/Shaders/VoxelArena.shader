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
        [Header(Build mode)]
        _Desaturate ("Приглушить: обесцветить", Range(0, 1)) = 0
        _Darken ("Приглушить: затемнить", Range(0, 1)) = 0
        [Toggle] _HideWalls ("Прятать стены при установке здания (их рисует прозрачный проход)", Float) = 1
        _Alpha ("Непрозрачность", Range(0, 1)) = 1
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
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
            half _Desaturate;
            half _Darken;
            half _HideWalls;
            half _Alpha;
        CBUFFER_END

        // Режим установки здания — глобальный ключ _VOXEL_BUILD_FADE. Непрозрачная земля в нём
        // не рисует стены, а стены рисуются второй раз прозрачным материалом с ключом _WALLS_ONLY
        // (он, наоборот, рисует только стены). Без ключей отсечения пикселей в шейдере нет.
        #define PALETTE_WALL_TOP 15
        #define PALETTE_WALL_SIDE 16

        void BuildModeClip(int paletteIndex)
        {
            bool wall = paletteIndex == PALETTE_WALL_TOP || paletteIndex == PALETTE_WALL_SIDE;
        #if defined(_VOXEL_BUILD_FADE)
            clip(wall && _HideWalls > 0.5 ? -1 : 1);
        #endif
        #if defined(_WALLS_ONLY)
            clip(wall ? 1 : -1);
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ _VOXEL_BUILD_FADE
            #pragma multi_compile_local _ _WALLS_ONLY

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
                BuildModeClip(paletteIndex);
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
                // Приглушение (другие постройки, пока ставится новое здание)
                half gray = dot(color, half3(0.299, 0.587, 0.114));
                color = lerp(color, gray.xxx, _Desaturate) * (1.0 - _Darken);
                color = MixFog(color, input.fogFactor);
                return half4(color, _Alpha);
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
            #pragma multi_compile _ _VOXEL_BUILD_FADE

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float paletteIndex : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.paletteIndex = input.color.r * 255.0;
                return output;
            }

            half frag(Varyings input) : SV_Target
            {
                BuildModeClip((int)round(input.paletteIndex));
                return 0;
            }
            ENDHLSL
        }
    }
}
