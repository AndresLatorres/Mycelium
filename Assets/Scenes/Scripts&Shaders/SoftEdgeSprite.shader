Shader "Network/SoftEdgeSprite"
{
    // Shader para SpriteRenderer que difumina el borde de la imagen con un mask
    // radial u ovalado, sin importar si la imagen original tiene canal alpha.
    // Asignalo como Material del SpriteRenderer (no como textura).
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FeatherAmount ("Cuanto se difumina el borde", Range(0, 0.5)) = 0.15
        _FeatherShape ("Forma (0 = rectangular, 1 = ovalada)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            fixed4 _Color;
            sampler2D _MainTex;
            float _FeatherAmount;
            float _FeatherShape;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                UNITY_TRANSFER_FOG(OUT, OUT.vertex);
                return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                // Distancia del pixel al centro del sprite (0 = centro, 1 = borde)
                float2 centered = IN.texcoord - 0.5;
                float rectDist = max(abs(centered.x), abs(centered.y)) * 2;
                float ovalDist = length(centered) * 2;
                float dist = lerp(rectDist, ovalDist, _FeatherShape);

                float mask = 1 - smoothstep(1 - _FeatherAmount, 1.0, dist);
                c.a *= mask;

                UNITY_APPLY_FOG(IN.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
