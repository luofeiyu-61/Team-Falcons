Shader "UI/BlurShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurAmount ("模糊量", Range(0, 1)) = 0
        _MaxBlurRadius ("最大模糊半径（纹素）", Float) = 8
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _BlurAmount;
            float _MaxBlurRadius;

            // 17-tap 双环高斯采样（中心 + 环1: 4轴向4对角 + 环2: 4轴向4对角）
            // 权重按 2D 高斯分布归一化，sigma=1.5
            static const float2 offsets[16] = {
                // 环1 轴向（距离1）
                float2( 1, 0), float2(-1, 0), float2(0,  1), float2(0, -1),
                // 环1 对角（距离√2）
                float2( 1, 1), float2(-1,-1), float2(1, -1), float2(-1, 1),
                // 环2 轴向（距离2）
                float2( 2, 0), float2(-2, 0), float2(0,  2), float2(0, -2),
                // 环2 对角（距离2√2）
                float2( 2, 2), float2(-2,-2), float2(2, -2), float2(-2, 2),
            };
            static const float weights[17] = {
                0.1100,
                0.0881, 0.0881, 0.0881, 0.0881,
                0.0706, 0.0706, 0.0706, 0.0706,
                0.0452, 0.0452, 0.0452, 0.0452,
                0.0186, 0.0186, 0.0186, 0.0186,
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float radius = lerp(0.0, _MaxBlurRadius, _BlurAmount);
                float2 step = _MainTex_TexelSize.xy * radius;

                fixed4 col = tex2D(_MainTex, i.uv) * weights[0];

                [unroll]
                for (int j = 0; j < 16; j++)
                {
                    col += tex2D(_MainTex, i.uv + offsets[j] * step) * weights[j + 1];
                }

                col *= i.color;
                return col;
            }
            ENDCG
        }
    }
}
