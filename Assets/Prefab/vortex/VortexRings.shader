Shader "Custom/VortexRings"
{
    Properties
    {
        _Color ("Ring Color", Color) = (0.2, 0.5, 1, 1)
        _Phase ("Phase", Float) = 0
        _RingWidth ("Ring Width", Range(0.01, 0.2)) = 0.035
        _RingSpacing ("Ring Spacing", Range(0, 0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _Phase;
            float _RingWidth;
            float _RingSpacing;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // phase < 0: 停顿期，光环不可见
                if (_Phase < 0.0)
                    return fixed4(0, 0, 0, 0);

                // 0 at center, 1 at edge
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;

                // Single ring contracts from edge (phase=0) to center (phase=1)
                float ringPos = lerp(1.0, 0.0, _Phase);

                // Ring intensity: bright at ring position, fades within _RingWidth
                float alpha = 1.0 - smoothstep(0.0, _RingWidth, abs(dist - ringPos));

                // Fade out as ring approaches center
                alpha *= smoothstep(0.0, 0.08, ringPos);

                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
