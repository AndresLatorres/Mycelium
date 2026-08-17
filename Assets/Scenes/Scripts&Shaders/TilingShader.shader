Shader "Custom/SueloAntiTilingReal"
{
    Properties
    {
        _MainTex ("Textura Suelo", 2D) = "white" {}
        _NoiseTex ("Textura Ruido (Suave, gris)", 2D) = "gray" {}
        _MainScale ("Escala Suelo", Float) = 0.5
        _NoiseScale ("Escala Ruido", Float) = 0.05
        _BlendSharpness ("Suavidad del Corte", Range(0.1, 5.0)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NoiseTex;
        float _MainScale;
        float _NoiseScale;
        float _BlendSharpness;

        struct Input
        {
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Coordenadas base basadas en el mundo
            float2 uvBase = IN.worldPos.xz * _MainScale;
            float2 uvNoise = IN.worldPos.xz * _NoiseScale;

            // 1. Obtenemos un factor de ruido para usar de máscara
            float mask = tex2D(_NoiseTex, uvNoise).r;
            
            // Hacemos que la transición sea más suave o marcada según el slider
            mask = saturate((mask - 0.5) * _BlendSharpness + 0.5);

            // 2. Creamos dos versiones de la misma textura con UVs diferentes
            // Muestra A: Textura normal
            half4 colorA = tex2D(_MainTex, uvBase);

            // Muestra B: Textura con un desfase (offset) arbitrario en diagonal 
            // y a una escala ligeramente diferente (puedes cambiar 0.8 por otro número)
            float2 uvDesfasadas = (uvBase + float2(17.3, 29.1)) * 0.83;
            half4 colorB = tex2D(_MainTex, uvDesfasadas);

            // 3. MEZCLA (Lerp): Aquí está la magia. Mezclamos A y B usando la máscara de ruido.
            // Esto elimina la repetición porque corta el patrón usando formas orgánicas.
            o.Albedo = lerp(colorA.rgb, colorB.rgb, mask);
            
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}