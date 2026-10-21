Shader "Hidden/PalletStacker/SilhouetteOutline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "SilhouetteOutline"
            Cull Off
            ZTest Always
            ZWrite Off
            Blend One One
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _OutlineColor;
            float _OutlineThickness;

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float center = SampleMask(uv);
                float2 radius = _BlitTexture_TexelSize.xy * max(_OutlineThickness, 0.5);

                static const float2 directions[8] =
                {
                    float2(1.0, 0.0),
                    float2(0.70710678, 0.70710678),
                    float2(0.0, 1.0),
                    float2(-0.70710678, 0.70710678),
                    float2(-1.0, 0.0),
                    float2(-0.70710678, -0.70710678),
                    float2(0.0, -1.0),
                    float2(0.70710678, -0.70710678)
                };

                float expanded = center;
                [unroll]
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    float2 direction = directions[directionIndex] * radius;
                    expanded = max(expanded, SampleMask(uv + direction * 0.33333333));
                    expanded = max(expanded, SampleMask(uv + direction * 0.66666667));
                    expanded = max(expanded, SampleMask(uv + direction));
                }

                float outsideOutline = saturate(expanded - center);
                half3 color = _OutlineColor.rgb * outsideOutline * _OutlineColor.a;
                return half4(color, 0.0h);
            }
            ENDHLSL
        }
    }
}
