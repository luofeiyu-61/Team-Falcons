Shader "UI/MosaicShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MosaicProgress ("马赛克进度", Range(0, 1)) = 0
        _MaxBlockSize ("最大块大小（纹素）", Float) = 64
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
            float _MosaicProgress;
            float _MaxBlockSize;

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
                // 当前块大小（纹素单位）：从 1（原图）到 _MaxBlockSize
                float blockSize = lerp(1.0, _MaxBlockSize, _MosaicProgress);
                // 转为 UV 空间步长
                float2 blockSizeUV = blockSize * _MainTex_TexelSize.xy;
                // 将 UV 对齐到每个块中心
                float2 blockCoord = floor(i.uv / blockSizeUV);
                float2 centeredUV = (blockCoord + 0.5) * blockSizeUV;

                fixed4 col = tex2D(_MainTex, centeredUV);
                col *= i.color;
                return col;
            }
            ENDCG
        }
    }
}
