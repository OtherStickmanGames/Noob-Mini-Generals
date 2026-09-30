// Сетка застройки: у каждой клетки яркая рамка (линии сетки) и бледная заливка того же цвета.
// Цвет вершины: r — клетка занята (1) или свободна (0), a — затухание к краю сетки.
// UV — координаты внутри клетки 0..1. Цвета и линии задаёт BuildGridStyle (инспектор HUD).
// Лежит в Resources, чтобы Shader.Find находил его и в сборке.
Shader "NoobGenerals/BuildGrid"
{
    Properties
    {
        _FreeColor ("Свободная клетка", Color) = (0.27, 0.92, 0.35, 1)
        _BlockedColor ("Занятая клетка", Color) = (0.96, 0.24, 0.18, 1)
        _LineWidth ("Половина толщины линии, доля клетки", Range(0, 0.2)) = 0.045
        _LineAlpha ("Непрозрачность линий", Range(0, 1)) = 0.9
        _FillAlpha ("Непрозрачность заливки", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "BuildGrid"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FreeColor;
                half4 _BlockedColor;
                half _LineWidth;
                half _LineAlpha;
                half _FillAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Расстояние до края клетки и ширина пикселя в долях клетки — для ровной линии без лесенки
                float2 edge = min(input.uv, 1.0 - input.uv);
                float2 pixel = fwidth(input.uv);
                // Вдали линия не тоньше пикселя, иначе сетка рассыпается
                float2 width = max(_LineWidth, pixel * 0.75);
                float2 onLine = saturate((width - edge) / max(pixel, 1e-5) + 0.5);
                half lineMask = max(onLine.x, onLine.y);

                half4 cell = lerp(_FreeColor, _BlockedColor, input.color.r);
                half alpha = lerp(_FillAlpha, _LineAlpha, lineMask) * cell.a * input.color.a;
                half3 color = lerp(cell.rgb, cell.rgb * 1.15 + 0.05, lineMask);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
