Shader "UI/CircleMaskShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaskProgress ("遮罩进度", Range(0, 1)) = 0
        _EdgeSoftness ("边缘柔和度", Range(0, 0.1)) = 0.01
        _MaskColor ("遮罩外颜色", Color) = (0, 0, 0, 1)
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
        ZTest Always
        Blend Off

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
            float _MaskProgress;
            float _EdgeSoftness;
            float4 _MaskColor;

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
                // 中心到当前像素的 UV 距离，修正宽高比使圆形不变形
                float2 center = float2(0.5, 0.5);
                float2 delta = i.uv - center;
                float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                delta.x *= aspect;
                float dist = length(delta);

                // 可见圆半径：progress=0 → 覆盖全屏四角，progress=1 → 收拢到 0
                float maxRadius = 0.5 * sqrt(aspect * aspect + 1.0);
                float radius = lerp(maxRadius, 0.0, _MaskProgress);

                // 圆内可见（显示画面），圆外显示 _MaskColor，边缘平滑过渡
                float visible = 1.0 - smoothstep(radius - _EdgeSoftness, radius + _EdgeSoftness, dist);

                fixed4 texCol = tex2D(_MainTex, i.uv);
                texCol.rgb *= i.color.rgb;

                fixed4 col;
                col.rgb = lerp(_MaskColor.rgb, texCol.rgb, visible);
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
}
