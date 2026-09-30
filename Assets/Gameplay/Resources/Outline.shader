// Экранный контур выделенных объектов (здание при установке).
// Проход 0: силуэт целиком (без проверки глубины) → канал R маски.
// Проход 1: видимая часть силуэта (проверка по глубине камеры) → канал G маски.
// Проход 2: сведение на экран — линия постоянной толщины в пикселях вокруг силуэта;
//           где объект закрыт (стеной, зданием), линия тусклее, а закрытая часть слегка заливается.
Shader "Hidden/NoobGenerals/Outline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 MaskVert(float4 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS.xyz);
        }
        ENDHLSL

        Pass
        {
            Name "Silhouette"
            ZTest Always
            ZWrite Off
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return half4(1, 0, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "Visible"
            ZTest LEqual
            ZWrite Off
            Cull Back
            ColorMask G

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float _OutlineWidth;     // толщина линии в пикселях
            float _HiddenLineAlpha;  // сила линии там, где объект закрыт
            float _HiddenFillAlpha;  // заливка закрытой части объекта

            #define DIRECTIONS 16
            #define RINGS 4

            half2 Mask(int2 pixel)
            {
                return LOAD_TEXTURE2D_X(_BlitTexture, pixel).rg;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                int2 pixel = int2(input.positionCS.xy);
                half2 center = Mask(pixel);

                // Внутри силуэта: видимую часть не трогаем, закрытую слегка заливаем цветом контура.
                // На краю силуэта маска дробная (MSAA) — это и есть сглаживание.
                half inside = center.r;
                half hiddenInside = saturate(center.r - center.g);

                // Снаружи: ищем ближайший пиксель силуэта по кольцам вокруг
                half coverage = 0;
                half visible = 0;
                [unroll]
                for (int ring = 1; ring <= RINGS; ring++)
                {
                    float radius = _OutlineWidth * ring / RINGS;
                    // Ближние кольца дают полную линию, дальнее — мягкий край
                    half weight = saturate(_OutlineWidth + 0.5 - radius);
                    [unroll]
                    for (int i = 0; i < DIRECTIONS; i++)
                    {
                        float angle = (i + 0.5 * (ring & 1)) * (6.2831853 / DIRECTIONS);
                        int2 offset = int2(round(float2(cos(angle), sin(angle)) * radius));
                        half2 m = Mask(pixel + offset);
                        coverage = max(coverage, m.r * weight);
                        visible = max(visible, m.g);
                    }
                }

                half lineAlpha = coverage * (1 - inside) * lerp(_HiddenLineAlpha, 1, visible);
                half fillAlpha = hiddenInside * _HiddenFillAlpha;
                half alpha = max(lineAlpha, fillAlpha) * _OutlineColor.a;
                return half4(_OutlineColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
