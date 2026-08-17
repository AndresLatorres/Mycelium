# Contexto del proyecto — Red de nodos generativa (Unity, Built-in RP)

Este documento resume las decisiones tomadas en una sesión larga de diseño con Claude
(claude.ai), para que Claude Code (u otra sesión) tenga contexto sin tener que
re-explicar todo desde cero.

## Qué es el proyecto

Un sistema que genera proceduralmente una "red" de objetos (imágenes/audios) conectados
por tags compartidos. El jugador se acerca a un nodo, espera un rato, y eso genera un
nuevo objeto "conector" con un tag distinto; acercarse a ESE genera un nuevo grupo
circular de nodos alrededor suyo. Así se arma una red infinita e interconectada.
Estéticamente: micelio de hongos conectando los nodos, imágenes flotantes oníricas,
niebla atmosférica, mundo curvo (bowl effect).

Render pipeline: **Built-in RP** (no URP/HDRP — decisión tomada explícitamente por
performance en hardware de gama baja/media).

## Arquitectura de scripts (Assets/Scenes/Scripts&Shaders/)

- **NetworkTag.cs** — ScriptableObject: un tag como asset independiente (campo
  `tagName`). Los objetos y el generador se refieren a tags arrastrando el asset, no
  escribiendo texto — ver sección de tags como assets más abajo.
- **TaggedObjectData.cs** — ScriptableObject: un objeto (imagen o audio) con sus tags
  (`List<NetworkTag>`), un `infoText` opcional, y un `prefabOverride` opcional.
- **TaggedObjectDatabase.cs** — ScriptableObject: lista central de todos los
  TaggedObjectData, con búsqueda por tag (recibe/devuelve `NetworkTag`, no `string`).
- **SpawnedNode.cs** — Componente en cada nodo instanciado. Detección de cercanía del
  jugador por DISTANCIA (no colliders/triggers — se sacó esa dependencia a propósito
  porque el jugador no tenía Rigidbody). Dispara evento cuando el jugador se queda cerca
  el tiempo suficiente. Tiene la animación de aparición suave (escala + alpha).
- **NetworkGenerator.cs** — El orquestador central. Genera clusters circulares,
  conectores, evita superposición de nodos (reconecta en vez de duplicar si comparten
  tag), dibuja el micelio, genera un nuevo origen si el jugador se aleja demasiado de
  todo. **Centraliza casi toda la configuración** (ver trampa más abajo).
- **MyceliumLink.cs** + **MyceliumLine.shader** — Los hilos de micelio: camino orgánico
  con ruido Perlin, ramificaciones en ángulos cerrados, crecimiento logarítmico
  (temprano = más desarrollado), punta redondeada solo en el shader (nunca
  `numCapVertices` de Unity, que redondea los dos extremos por igual).
- **FloatingImageEffect.cs** + **SoftEdgeSprite.shader** — Nodos con imagen: billboard
  hacia la cámara (NO hacia el Player — su pivote suele estar en los pies), flote
  vertical + vaivén + copias fantasma con movimiento independiente (no un trail).
- **InfiniteGroundFollower.cs** — Piso que sigue al jugador en X/Z, compensando el
  offset de textura para que no "viaje".
- **VolumetricHeightFog.cs** + **VolumetricHeightFog.shader** — Niebla de post-proceso
  (altura + ruido, NO raymarching real — se descartó por costo en gama baja). Usa
  `[ImageEffectOpaque]` para no afectar transparentes (ver trampa más abajo).
- **GazeInfoDisplay.cs** — Texto flotante (TextMeshPro, con fondo semi-transparente)
  que aparece si mirás fijo un nodo con la CÁMARA (no por distancia). Histéresis: ángulo
  angosto para aparecer, amplio para mantenerse visible. Dwell corto para reaparecer
  después de la primera vez.

### Descartado / revertido
- **Niebla volumétrica real (raymarching con god rays)** — evaluada, descartada por
  costo en gama baja. Se implementó la versión liviana (altura + ruido) en su lugar.
- **Curvatura de mundo ("bowl effect", horizonte que se levanta)** — implementada
  completamente (WorldCurvature.cginc, WorldCurvatureController.cs, CurvedGround.shader,
  SubdividedPlaneMesh.cs) y luego **revertida a pedido del usuario** por ser más
  problema que beneficio. Si se retoma en el futuro, el enfoque era: bend en vertex
  shader vía propiedades globales de shader, saturando a una altura máxima.

## Tags como assets (NetworkTag)

Los tags dejaron de ser `string` sueltos y pasaron a ser assets `NetworkTag`
(ScriptableObject, `Assets/Scenes/Scripts&Shaders/NetworkTag.cs`, campo `tagName`).
Motivo: escribir el mismo tag a mano en varios `TaggedObjectData` era propenso a
errores de tipeo (dos tags que deberían ser el mismo terminaban siendo distintos sin
que nadie lo notara), y renombrar un tag requería editarlo objeto por objeto.

- `TaggedObjectData.tags` es `List<NetworkTag>` — se arrastra el asset, no se tipea texto.
- `TaggedObjectDatabase.GetByTag/GetAllTags/GetRandomByTag` y todo `NetworkGenerator`
  (`startingTag`, `ClusterTag`, `visitedTags`, etc.) usan `NetworkTag` en vez de `string`.
- Comparaciones (`Intersect`, `Contains`, `!=`) siguen funcionando igual porque son
  referencias al mismo asset — dos objetos "comparten tag" si apuntan al mismo asset
  `NetworkTag`, no si el texto coincide.
- Para crear un tag nuevo: click derecho en el Project > Create > Network > Tag.
- Renombrar el asset (o su campo `tagName`) actualiza automáticamente a todos los
  objetos que lo referencian, porque todos apuntan al mismo asset.
- El tag "Player" que usa `GameObject.FindGameObjectWithTag("Player")` en varios
  scripts es el sistema de tags NATIVO de Unity (para GameObjects) y es algo
  completamente distinto — no tiene relación con `NetworkTag`, no se tocó.

## Trampas ya pisadas (para no repetirlas)

1. **AddComponent en runtime NO hereda valores custom del prefab.** Si un
   `TaggedObjectData` usa `prefabOverride` y ese prefab no tenía `SpawnedNode` (o
   `GazeInfoDisplay`) puesto a mano, `AddComponent<T>()` lo crea con los valores
   DEFAULT del script, ignorando cualquier ajuste que el usuario creyera haber hecho.
   **Solución aplicada:** centralizar la configuración en `NetworkGenerator`, que
   llama a métodos `ConfigureX(...)` en cada componente después de agregarlo. Varios
   componentes tienen un flag `useCustomSettings` como escape hatch para permitir
   overrides puntuales por prefab cuando hace falta.

2. **TextMeshPro agregado por código no trae fuente asignada.** A diferencia de
   arrastrarlo en el Editor, `AddComponent<TextMeshPro>()` deja `font = null` (texto
   invisible aunque todo lo demás esté bien). Hay que asignar
   `textMesh.font = TMP_Settings.defaultFontAsset` (o un `TMP_FontAsset` custom) a mano.

3. **Objetos transparentes no escriben en el depth buffer.** Cualquier post-proceso
   basado en depth (la niebla) los "ve" como si fueran lo que está detrás suyo,
   generando artefactos (se ven "traslúcidos"/mal calculados). Solución: `[ImageEffectOpaque]`
   para que la niebla corra ANTES de que se dibujen los transparentes.

4. **Trigger physics necesita Rigidbody en algún lado.** Si el jugador se mueve sin
   Rigidbody/CharacterController (por ejemplo moviéndolo a mano en el Editor durante
   Play), `OnTriggerEnter` nunca dispara. Por eso `SpawnedNode` usa detección por
   distancia en vez de colliders/triggers.

5. **`numCapVertices` de LineRenderer redondea los DOS extremos por igual** — no se
   puede tener un extremo plano y el otro redondeado con la API nativa. Se resolvió
   haciendo el redondeo de la punta en el shader (usando `_LineLength`/`_LineWidthWorld`
   en unidades de mundo), dejando `numCapVertices = 0` siempre.

6. **Direction "hacia afuera" calculada mal.** Al spawnear un conector, la dirección se
   calculaba respecto al transform del `NetworkGenerator` (el origen de la escena) en
   vez del centro REAL del cluster de origen — causaba conectores saliendo para el lado
   equivocado en clusters lejanos del origen. Se agregó `SpawnedNode.ClusterCenter` para
   guardar el centro real.

## Estado actual / posibles próximos pasos

- Sistema base, micelio, niebla, piso infinito, y texto por mirada: funcionando.
- Pendiente evaluado pero no resuelto: si se retoma la curvatura de mundo, revisar la
  interacción con el micelio (z-fighting cuando ambos curvan igual y quedan a la misma
  altura — se había resuelto con `ZTest Always` en el micelio, revertido junto con el
  resto).
- El usuario ya tiene movimiento real de jugador implementado (no manual).
- Todos los parámetros de gameplay (radios, tiempos de espera, ángulos, etc.) están
  centralizados como campos públicos en `NetworkGenerator` — es el primer lugar para
  buscar antes de tocar un componente individual.
