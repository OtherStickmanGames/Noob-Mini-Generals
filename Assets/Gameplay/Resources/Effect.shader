// Шейдер эффектов боя: снаряды, вспышки, обломки, дым. Кубики в стиле арены.
// Режимы (локальные ключи, материалы создаются в коде — поэтому multi_compile, а не shader_feature):
//   _PALETTE  — цвет из палитры арены: цвет вершины r — индекс палитры, a — непрозрачность.
//               Обломки земли и зданий сразу в цветах биома и команд.
//   _EMISSIVE — светится сам, без освещения: _Color * цвет вершины (снаряды, вспышки).
//   без ключей — освещённый _Color * цвет вершины (дым).
// Поддерживает инстансинг (Graphics.RenderMeshInstanced для снарядов).
Shader "NoobGenerals/Effect"
{
    Properties
    {
        _Color ("Цвет", Color) = (1, 1, 1, 1)
        _AmbientStrength ("Сила окружающего света", Range(0, 2)) = 1
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _PALETTE _EMISSIVE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _AmbientStrength;
            CBUFFER_END

            TEXTURE2D(_VoxelPaletteTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
            #if defined(_EMISSIVE)
                half3 color = _Color.rgb * input.color.rgb;
                half alpha = _Color.a * input.color.a;
            #else
                #if defined(_PALETTE)
                    int paletteIndex = (int)round(input.color.r * 255.0);
                    half3 albedo = LOAD_TEXTURE2D(_VoxelPaletteTex, int2(paletteIndex, 0)).rgb;
                    half alpha = input.color.a;
                #else
                    half3 albedo = _Color.rgb * input.color.rgb;
                    half alpha = _Color.a * input.color.a;
                #endif

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half3 diffuse = mainLight.color * saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS) * _AmbientStrength;
                half3 color = albedo * (diffuse + ambient);
            #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
