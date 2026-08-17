Shader "Hidden/Network/VolumetricHeightFog"
{
    // Shader de post-proceso (no se usa como material normal, se aplica via
    // VolumetricHeightFog.cs sobre la camara). Reconstruye la posicion en el mundo
    // de cada pixel a partir del depth buffer + los rayos de las 4 esquinas del
    // frustum de la camara, y calcula niebla por distancia, por altura, y modulada
    // por una textura de ruido animada para que no sea un degradado perfecto.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _NoiseTex;

            fixed4 _FogColor;
            float _FogDensity;
            float _FogStartDistance;
            float _HeightFogDensity;
            float _BaseHeight;
            float _HeightFalloff;
            float _NoiseScale;
            float2 _NoiseScroll;
            float _NoiseStrength;

            // Rayo (direccion, sin normalizar, ya escalado a farClipPlane) de cada
            // esquina del frustum de la camara, calculados en VolumetricHeightFog.cs.
            float4 _FrustumCorner0; // bottom left
            float4 _FrustumCorner1; // bottom right
            float4 _FrustumCorner2; // top right
            float4 _FrustumCorner3; // top left

            struct appdata
            {
                float4 vertex     : POSITION;
                float2 uv         : TEXCOORD0;
                float2 cornerIndex: TEXCOORD1; // 0,1,2,3 -- que esquina de frustum le toca a este vertice
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float2 uv_depth : TEXCOORD1;
                float3 ray      : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                o.uv = v.uv;
                o.uv_depth = v.uv;

                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    o.uv.y = 1 - o.uv.y;
                #endif

                int idx = (int)(v.cornerIndex.x + 0.5);
                float3 ray = _FrustumCorner0.xyz;
                if (idx == 1) ray = _FrustumCorner1.xyz;
                if (idx == 2) ray = _FrustumCorner2.xyz;
                if (idx == 3) ray = _FrustumCorner3.xyz;

                o.ray = ray;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 sceneColor = tex2D(_MainTex, i.uv);

                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv_depth);
                float linearDepth01 = Linear01Depth(rawDepth);
                bool isSky = linearDepth01 > 0.9999;

                // Aunque sea cielo (sin geometria real), "worldPos" nos da un punto proyectado
                // en esa direccion de vista -- su altura si es util: cerca del horizonte da un
                // punto bajo (mucha niebla), mirando hacia arriba da un punto muy alto (nada).
                float3 worldPos = _WorldSpaceCameraPos + i.ray * linearDepth01;

                // --- Niebla por distancia: SOLO tiene sentido si hay geometria real. Para el
                // cielo, "distancia" seria siempre el far clip plane, igual sin importar el
                // angulo de vision -- por eso antes tapaba el cielo entero como una pared. ---
                float distFog = 0;
                if (!isSky)
                {
                    float distanceToCamera = length(i.ray * linearDepth01);
                    float distFactor = max(distanceToCamera - _FogStartDistance, 0);
                    distFog = 1 - exp(-pow(distFactor * _FogDensity, 2));
                }

                // --- Niebla por altura: mas densa cerca de _BaseHeight, se aclara al subir.
                // Se aplica SIEMPRE, haya o no geometria real -- asi el cielo cerca del
                // horizonte tambien se ve neblinoso (como humo pegado al piso), sin tapar
                // el cielo completo. ---
                float heightDiff = max(worldPos.y - _BaseHeight, 0);
                float heightFog = saturate(_HeightFogDensity * exp(-heightDiff * _HeightFalloff));

                // --- Ruido animado, para que la densidad no sea un degradado perfecto ---
                float2 noiseUV = worldPos.xz * _NoiseScale + _NoiseScroll;
                float noise = tex2D(_NoiseTex, noiseUV).r;
                noise = lerp(1.0, noise, _NoiseStrength); // _NoiseStrength=0 -> sin ruido, plano

                float finalFog = saturate((distFog + heightFog) * noise);

                return lerp(sceneColor, _FogColor, finalFog);
            }
            ENDCG
        }
    }

    Fallback Off
}
