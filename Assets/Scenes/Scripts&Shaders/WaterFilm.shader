Shader "Network/WaterFilm"
{
    // Lamina fina de "agua" para poner ENCIMA del piso, en un plano aparte (no
    // reemplaza a TilingShader.shader). No hay reflexion planar real (costo en
    // gama baja, mismo criterio que la niebla): es una capa transparente con dos
    // normal maps de ripples animados, en distinta escala/velocidad/direccion
    // (mismo truco que el anti-tiling del piso, pero para que el movimiento no se
    // note en loop) que perturban un brillo especular, mas un Fresnel que se
    // aclara en angulos rasantes -- de frente se ve casi transparente (se ve el
    // piso debajo), de costado parece reflectante. Asume una superficie mas o
    // menos horizontal (normal ~ (0,1,0)), como un plano de piso.
    Properties
    {
        _RippleNormal ("Normal de ripples (tileable)", 2D) = "bump" {}
        _RippleTiling1 ("Escala ripples A", Float) = 0.4
        _RippleSpeed1 ("Velocidad ripples A (u/seg, xy)", Vector) = (0.05, 0.03, 0, 0)
        _RippleTiling2 ("Escala ripples B", Float) = 0.65
        _RippleSpeed2 ("Velocidad ripples B (u/seg, xy)", Vector) = (-0.02, 0.06, 0, 0)
        _RippleStrength ("Fuerza del ripple (perturbacion normal)", Range(0, 2)) = 0.5

        _WaterColor ("Color base (mirando de frente, alpha = opacidad)", Color) = (0.55, 0.7, 0.68, 0.18)
        _FresnelColor ("Color de reflejo (angulo rasante)", Color) = (0.85, 0.9, 0.95, 0.6)
        _FresnelPower ("Dureza del fresnel", Range(0.5, 8)) = 3
        _FresnelIntensity ("Cuanto suma el fresnel al alpha", Range(0, 1)) = 0.5

        _SparkleColor ("Color del brillo especular", Color) = (1, 1, 1, 1)
        _SparklePower ("Dureza del brillo (shininess)", Range(1, 256)) = 80
        _SparkleIntensity ("Intensidad del brillo", Range(0, 4)) = 1.2
    }

    SubShader
    {
        // Queue "Transparent-1" (2999, no el 3000 default) a proposito: este plano es
        // ENORME (hereda la escala del piso, 100x100) y sigue al jugador, asi que su
        // "centro" para el sorteo por distancia de Unity siempre queda pegado a la
        // camara -- eso lo hacia pintarse SIEMPRE ultimo (encima de nodos e hilos de
        // micelio, sin importar la posicion real de cada pixel). Bajando la cola un
        // paso, se dibuja SIEMPRE antes que cualquier transparente en la cola default,
        // asi que nodos/micelio quedan encima de forma consistente.
        Tags { "Queue"="Transparent-1" "IgnoreProjector"="True" "RenderType"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            sampler2D _RippleNormal;
            float _RippleTiling1;
            float4 _RippleSpeed1;
            float _RippleTiling2;
            float4 _RippleSpeed2;
            float _RippleStrength;

            fixed4 _WaterColor;
            fixed4 _FresnelColor;
            float _FresnelPower;
            float _FresnelIntensity;

            fixed4 _SparkleColor;
            float _SparklePower;
            float _SparkleIntensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // UVs en espacio de mundo (como TilingShader): el plano puede moverse
                // (InfiniteGroundFollower) sin que el patron de ripples "viaje" con el.
                float2 uvA = i.worldPos.xz * _RippleTiling1 + _RippleSpeed1.xy * _Time.y;
                float2 uvB = i.worldPos.xz * _RippleTiling2 + _RippleSpeed2.xy * _Time.y;

                float3 normalA = UnpackNormal(tex2D(_RippleNormal, uvA));
                float3 normalB = UnpackNormal(tex2D(_RippleNormal, uvB));
                float3 rippleNormal = normalize(float3(normalA.xy + normalB.xy, normalA.z * normalB.z));

                // Pasamos el ripple (en espacio tangente) a espacio de mundo a mano,
                // asumiendo normal ~vertical -- mas simple que TBN completo y alcanza
                // para un piso horizontal.
                float3 worldNormal = normalize(i.worldNormal);
                float3 tangent = normalize(cross(worldNormal, float3(0, 0, 1)));
                float3 bitangent = cross(worldNormal, tangent);
                float3 perturbedNormal = normalize(
                    worldNormal
                    + (tangent * rippleNormal.x + bitangent * rippleNormal.y) * _RippleStrength
                );

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                // Fresnel: de frente, color base (transparente, se ve el piso debajo);
                // en angulo rasante, se acerca al color de "reflejo" -- simula
                // reflectividad sin reflexion planar real.
                float fresnel = pow(1 - saturate(dot(viewDir, perturbedNormal)), _FresnelPower);
                fixed3 baseColor = lerp(_WaterColor.rgb, _FresnelColor.rgb, fresnel);
                float alpha = saturate(_WaterColor.a + fresnel * _FresnelIntensity);

                // Brillo especular de la luz principal sobre la normal perturbada --
                // se mueve con los ripples, da el efecto de destellos en la superficie.
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(saturate(dot(perturbedNormal, halfDir)), _SparklePower);
                fixed3 sparkle = spec * _SparkleIntensity * _SparkleColor.rgb * _LightColor0.rgb;

                return fixed4(baseColor + sparkle, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
