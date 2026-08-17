Shader "Network/MyceliumLine"
{
    // Pensado para usarse en un LineRenderer con Texture Mode = Stretch,
    // asi el UV.x recorre 0->1 a lo largo de TODO el camino (no por segmento).
    //
    // El LineRenderer se deja siempre con numCapVertices = 0 (plano en ambos
    // extremos). El redondeo de la punta que esta creciendo se hace ACA, en el
    // shader, usando distancias en unidades de mundo (_LineLength/_LineWidthWorld)
    // para que el semicirculo salga proporcionado. El origen del hilo (uv.x ~ 0)
    // nunca se redondea -- solo la zona cercana a _GrowAmount, y solo si
    // _TipCapRounded esta en 1.
    Properties
    {
        _MainTex ("Textura de ruido (grayscale)", 2D) = "white" {}
        _Color ("Color base del hilo", Color) = (0.65, 0.85, 0.55, 0.9)
        _GlowColor ("Color de brillo en la punta", Color) = (1, 1, 0.75, 1)
        _GrowAmount ("Crecimiento (0-1)", Range(0,1)) = 1
        _ScrollSpeed ("Velocidad de pulso del ruido", Float) = 0.4
        _NoiseTiling ("Tiling del ruido", Float) = 5
        _EdgeSoftness ("Suavizado de borde del hilo", Range(0.01, 1)) = 0.5
        _TipGlowWidth ("Ancho del brillo en la punta", Range(0.001, 0.3)) = 0.06
        _LineLength ("Largo total del hilo, en unidades de mundo", Float) = 1
        _LineWidthWorld ("Ancho del hilo, en unidades de mundo", Float) = 0.1
        _TipCapRounded ("Punta redondeada (1) o plana (0)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _GlowColor;
            float _GrowAmount;
            float _ScrollSpeed;
            float _NoiseTiling;
            float _EdgeSoftness;
            float _TipGlowWidth;
            float _LineLength;
            float _LineWidthWorld;
            float _TipCapRounded;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Recorta lo que todavia no crecio
                if (i.uv.x > _GrowAmount) discard;

                // Redondeo de la punta que esta creciendo (NUNCA en el origen).
                // Se calcula en unidades de mundo para que el semicirculo quede
                // proporcionado en vez de una elipse deformada por el largo del hilo.
                if (_TipCapRounded > 0.5)
                {
                    float distFromTipX = (_GrowAmount - i.uv.x) * _LineLength;
                    float capRadius = _LineWidthWorld * 0.5;

                    if (distFromTipX < capRadius)
                    {
                        float distFromCenterY = (i.uv.y - 0.5) * _LineWidthWorld;
                        float circDist = sqrt(distFromTipX * distFromTipX + distFromCenterY * distFromCenterY);
                        if (circDist > capRadius) discard;
                    }
                }

                // Ruido organico que fluye con el tiempo, da sensacion de pulso/vida
                float2 noiseUV = float2(i.uv.x * _NoiseTiling - _Time.y * _ScrollSpeed, i.uv.y * _NoiseTiling);
                fixed noise = tex2D(_MainTex, noiseUV).r;
                noise = lerp(0.55, 1.0, noise); // que nunca desaparezca del todo

                // Desvanece los bordes a lo ancho del hilo
                float edgeFade = 1 - smoothstep(0.5 - _EdgeSoftness, 0.5, abs(i.uv.y - 0.5) * 2);

                // Brillo extra justo en la punta que esta creciendo ahora mismo
                float tip = 1 - smoothstep(0, _TipGlowWidth, abs(_GrowAmount - i.uv.x));
                fixed4 col = lerp(_Color, _GlowColor, tip);

                fixed alpha = edgeFade * noise * col.a;
                fixed4 finalColor = fixed4(col.rgb, alpha);
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return finalColor;
            }
            ENDCG
        }
    }
}
