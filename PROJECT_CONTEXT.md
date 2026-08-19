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
  escribiendo texto — ver sección de tags como assets más abajo. Campo opcional
  `centerNodeOverride` (`TaggedObjectData`): si se asigna, ESE objeto especial se
  instancia en el centro de cualquier cluster NUEVO (origen de verdad, sin nodo
  existente ahi) generado con este tag — el inicial incluido. Si se deja vacío,
  el centro queda vacío como siempre. Ver detalle en `NetworkGenerator.GenerateCluster`.
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
  `MyceliumLink.Build()` recibe un `Color colorTint` que aplica a `_Color` del hilo
  principal Y de sus ramas (ver color de colonia mas abajo).
- **NetworkGenerator — nodo especial de centro (`NetworkTag.centerNodeOverride`)** —
  al arrancar `GenerateCluster`, si `centerNode == null` (origen nuevo de verdad,
  no una continuacion alrededor de un conector existente) Y el tag tiene
  `centerNodeOverride` asignado, se instancia ESE objeto en `center` via `PlaceNode`
  (variable local `effectiveCenterNode`) y de ahi en mas se lo trata exactamente
  como cualquier `centerNode`: el anillo se conecta a el con `CreateMyceliumLink`
  real (no un punto vacio), participa de `allNodes`/deteccion de cercania/gaze
  como cualquier nodo. Se excluye a mano del `pool` de seleccion del anillo (para
  que no aparezca DE NUEVO como uno de los nodos del circulo). Si el tag no tiene
  override, o ya habia un `centerNode` real (cluster alrededor de un conector),
  el comportamiento es identico al de antes de este cambio.
- **NetworkGenerator — brotes de "plato de petri"** — al final de CADA
  `GenerateCluster` (cluster inicial, o cualquiera generado despues), se llama
  `SpawnPetriSprouts(center, colonyColor)`: genera `petriSproutCount` hilos de
  `MyceliumLink` CORTOS repartidos en todas direcciones desde el centro, sin
  conectar a ningun nodo real (no dependen de cuantos nodos se lograron ubicar en
  el cluster) — asi el origen siempre se ve como una colonia creciendo, tenga 1
  nodo conectado o `objectsPerCluster`. No se registran en `incomingLinkAtNode` a
  proposito (no hay ningun `SpawnedNode` en la punta) -- se quedan con la punta
  redondeada para siempre, que es lo correcto para un brote que no continua a
  ningun lado.
- **NetworkGenerator — nivel de generacion y color de colonia** — `SpawnedNode`
  tiene `GenerationId` (todos los nodos de UN MISMO llamado a `GenerateCluster`
  comparten numero) y `ColonyColor`. `LinkToNearbySharedTagNodes` NO conecta dos
  nodos con el mismo `GenerationId` (son "hermanos" del mismo anillo, ya conectados
  via el centro comun) pero SI conecta nodos de tandas distintas (anterior o
  posterior) que comparten tag y estan cerca -- ese era el comportamiento pedido
  originalmente y se mantiene. `ColonyColor` se hereda del `centerNode` (o del
  `originNode` para un conector) cuando existe -- osea, un cluster generado
  alrededor de un conector CONTINUA el color de su colonia, no genera uno nuevo.
  Solo se genera un color nuevo (`GenerateColonyColor`, variacion aleatoria de tono
  y saturacion en HSV sobre el `_Color` original del material del prefab de
  micelio, leido una sola vez y cacheado) cuando `centerNode == null`: el cluster
  inicial de `Start()`, o uno generado por `GenerateNewRandomOrigin` (el jugador se
  alejo demasiado) -- osea, un ORIGEN nuevo de verdad, no una continuacion.
- **FloatingImageEffect.cs** + **SoftEdgeSprite.shader** — Nodos con imagen: billboard
  hacia la cámara (NO hacia el Player — su pivote suele estar en los pies), flote
  vertical + vaivén + copias fantasma con movimiento independiente (no un trail).
  Desvanecido por distancia: mas alla de `fadeStartDistance` se van achicando (escala
  -> 0) y hundiendo en Y hacia `fadeGroundY` (solo Y, no se desliza en X/Z) hasta
  `fadeEndDistance`, donde quedan invisibles (alpha -> 0 tambien). Se aplica igual a
  las copias fantasma. La escala "base" se captura en `Awake()` (no en `Start()`) a
  proposito -- `Awake` corre durante `Instantiate()`, ANTES de que
  `NetworkGenerator` llame a `SpawnedNode.Initialize()`, que dispara la animacion de
  aparicion (`AppearRoutine`) que anima la escala desde 0. Si se capturara en
  `Start()` se arriesgaba una carrera (podia leer la escala ya en 0). No es un
  componente que agregue `NetworkGenerator` en runtime (no sufre la trampa #1) --
  se configura directo en el prefab, como el resto de los campos de este script.
- **InfiniteGroundFollower.cs** — Piso que sigue al jugador en X/Z, compensando el
  offset de textura para que no "viaje".
- **VolumetricHeightFog.cs** + **VolumetricHeightFog.shader** — Niebla de post-proceso
  (altura + ruido, NO raymarching real — se descartó por costo en gama baja). Usa
  `[ImageEffectOpaque]` para no afectar transparentes (ver trampa más abajo).
- **GazeInfoDisplay.cs** — Texto flotante (TextMeshPro, con fondo semi-transparente)
  que aparece si mirás fijo un nodo con la CÁMARA (no por distancia). Histéresis: ángulo
  angosto para aparecer, amplio para mantenerse visible. Dwell corto para reaparecer
  después de la primera vez. El fondo del texto reutiliza `SoftEdgeSprite.shader` (el
  mismo que los nodos con imagen) para difuminar el borde en vez de un rectángulo de
  corte duro — `backgroundFeatherAmount`/`backgroundFeatherShape`, centralizados en
  `NetworkGenerator` como el resto (`gazeBackgroundFeatherAmount`/`Shape`).
- **WaterFilm.shader** — Lámina fina de "agua" para poner en un plano APARTE encima del
  piso (no reemplaza `TilingShader.shader`). Sin reflexión planar real (mismo criterio
  de costo que la niebla): dos normal maps de ripples animados en distinta
  escala/velocidad/dirección (mismo truco anti-repetición que el piso, aplicado al
  movimiento) que perturban un brillo especular, más un Fresnel que se aclara en ángulo
  rasante para simular reflectividad barata. UVs en `worldPos.xz`, así no "viaja" si el
  plano se mueve. Ya cableado como plano hijo del piso en `Micelio.unity` (ver Estado
  actual más abajo) — no se probó visualmente todavía porque no hay Editor de Unity
  disponible en estas sesiones.
- **FloatingOrbitCompanion.cs** — Pensado para el prefab "Vintage table lamp" (ya
  importado, instanciado en `Micelio.unity` como hijo del Player). Hace que acompañe
  al jugador orbitando en vez de seguirlo pegado, en capas independientes: (1) un
  punto que orbita al jugador a velocidad angular constante (así el jugador quieto
  igual lo hace moverse), (2) un resorte masa-amortiguado persiguiendo ese punto —
  con `springDamping < 1` se pasa de largo y rebota (a propósito, no es
  `SmoothDamp`, que nunca overshootea), (3) flote vertical tipo boya sumado
  ENCIMA de la posición del resorte (no pasa por el resorte, para que no se sienta
  reactivo al movimiento), (4) tambaleo con ruido Perlin (pitch/roll) + giro
  constante en yaw. Todos los parámetros son campos públicos editables por
  Inspector. No requiere reparentar nada — escribe `transform.position/rotation`
  en espacio de mundo cada frame, funciona igual esté o no bajo el Player en la
  jerarquía. **No wireado todavía** — falta que alguien con el Editor abierto le
  agregue el componente al GameObject de la lámpara (el campo `target` se
  autocompleta solo con tag "Player" si se deja vacío).
  El `deltaTime` que usa el resorte esta clampeado a 0.05s -- sin eso, un salto de
  frame grande (Instantiate pesado de NetworkGenerator, pausa del GC) puede hacer
  que la integracion del resorte se vuelva inestable por un instante ("crop"
  intermitente). `orbitAngle`/`spinAngle` tambien wrappean mod 360 para no perder
  precision de float en sesiones largas. Si en algun momento la lampara "no se
  inclina nunca", sospechar primero de un Rigidbody en el GameObject (o algun
  padre) con Freeze Rotation en X/Z tildado -- eso cancelaria el pitch/roll del
  tambaleo sin tirar ningun error; este componente no necesita Rigidbody, es
  puramente cinematico.
  **Historial de debugging de jitter (por si vuelve a aparecer):** (1) la lampara
  estaba parentada al Player, que tiene Rigidbody (`FirstPersonMovement.cs` mueve
  por `rigidbody.velocity` en `FixedUpdate`) -- se resolvio DESPARENTANDOLA (ya no
  es hija del Player en la jerarquia; el script no lo necesita, calcula todo en
  espacio de mundo). (2) Con eso mejoro pero seguia habiendo jitter al caminar --
  se activo Interpolation "Interpolate" en el Rigidbody del Player (ayuda a que
  `transform.position` no salte en discretos al tick de fisica de 50Hz al leerlo
  desde `LateUpdate`). (3) Todavia quedaba un jitter CONSTANTE (no puntual) y
  EXCLUSIVO de la lampara (el resto de la escena fluido) mientras el jugador se
  movia, nulo con el jugador quieto -- diagnostico: el resorte se integraba en un
  solo paso de Euler semi-implicito por frame, sensible a la variacion normal de
  `deltaTime` entre frames SOLO cuando hay delta != 0 (target moviendose). Se
  resolvio subdividiendo la integracion en `SpringSubsteps = 8` pasos mas chicos
  por frame (mismo resultado final, mucho mas estable) -- SIN EFECTO, el jitter
  seguia igual, lo cual confirmo que el problema no era la integracion. (4) Se
  aisló con dos preguntas: el jitter era de POSICION (no rotacion -- se probo con
  `wobbleAmount = 0` y seguia igual, asi que tampoco era el tambaleo) y CONSTANTE
  mientras camina. Se agrego `targetPositionSmoothing` (`Vector3.SmoothDamp` sobre
  la lectura de `target.position`, ANTES de la orbita/resorte -- a proposito
  `SmoothDamp` y no mas resorte, aca no se quiere overshoot, solo limpiar ruido) --
  mejoro yendo en linea recta pero NO al girar la camara (con el mouse). (5) Causa
  raiz real, encontrada en el controller de terceros
  (`Assets/Mini First Person Controller/Scripts/FirstPersonLook.cs`): el yaw del
  personaje se rotaba con `character.localRotation = ...` directo en `Update()`,
  sobre el MISMO GameObject que tiene el Rigidbody del Player. Eso rompe la
  garantia que asume `Rigidbody.Interpolation` (que SOLO el motor de fisica toca
  ese transform entre pasos de `FixedUpdate`) -- de ahi que el jitter apareciera
  especificamente al girar la camara, sin importar cuanto se suavizara del lado de
  la lampara (el dato de origen ya venia inconsistente). Se corrigio moviendo esa
  rotacion a `FixedUpdate` via `Rigidbody.MoveRotation` (la funcion de `Reset()`
  sigue igual; el pitch de camara, que no es Rigidbody, se mantuvo en `Update()`
  sin problema). Mejoro mucho, queda un resto minimo esperable (el mouse se sigue
  leyendo en `Update()` pero se aplica una vez por tick de fisica, asi que varios
  frames de input se acumulan y aplican de una vez -- la interpolacion lo suaviza
  pero no es identico a aplicar cada frame). Con la causa grande resuelta,
  `targetPositionSmoothing` probablemente necesite MENOS valor que durante el
  debugging (se probo hasta 0.4 para tapar el sintoma grande; con la causa
  arreglada alcanza con menos, algo como 0.1-0.15).

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

## Control de versiones

El proyecto está en git, conectado a `https://github.com/AndresLatorres/Mycelium`
(rama `master`, no `main` — el repo estaba vacío al crearse y tomó ese nombre por
default, es solo cosmético). `.gitignore` en la raíz excluye `Library/`, `Temp/`,
`Logs/`, `UserSettings/` (cache regenerable de Unity, no debe versionarse — `Library/`
sola pesa cientos de MB). Los binarios más pesados en `Assets/` son texturas de ~14 MB,
dentro del límite de GitHub, así que no hace falta Git LFS por ahora.

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
- Sistema de tags migrado de `string` a assets `NetworkTag` — los 32 `TaggedObjectData`
  existentes (`Assets/Scenes/TaggedObjects/TO_*.asset`) y el `startingTag` de
  `NetworkGenerator` en la escena `Micelio.unity` ya fueron migrados a referencias
  (no hace falta reasignar nada a mano).
- Pendiente evaluado pero no resuelto: si se retoma la curvatura de mundo, revisar la
  interacción con el micelio (z-fighting cuando ambos curvan igual y quedan a la misma
  altura — se había resuelto con `ZTest Always` en el micelio, revertido junto con el
  resto).
- **WaterFilm ya está cableado en la escena** (`Assets/Scenes/Micelio.unity`): plano
  hijo llamado "WaterFilm" bajo el GameObject del piso (así hereda
  `InfiniteGroundFollower` sin tocar el script), `localPosition.y = 0.02` para evitar
  z-fighting con el piso real, usando `Assets/Scenes/Material/WaterFilm.mat`
  (shader `Network/WaterFilm`, `_RippleNormal` = `Assets/Scenes/Imagenes/WaterNormal.jpg`
  que ya estaba en el proyecto importada como Normal Map). Ripples animados por
  default (`_RippleSpeed1`/`_RippleSpeed2` != 0 en el material) — para dejarlo
  estático alcanza con poner esos dos vectores en `(0,0,0,0)` desde el Inspector, sin
  tocar código. Sin sombras/light probes en el MeshRenderer (capa decorativa, no
  necesita esa carga). **No se verificó visualmente** (sin acceso al Editor de Unity en
  estas sesiones) — falta abrir el proyecto y confirmar que se ve bien y sin errores
  de consola, y ajustar colores/tiling a ojo.
- El usuario ya tiene movimiento real de jugador implementado (no manual).
- Todos los parámetros de gameplay (radios, tiempos de espera, ángulos, etc.) están
  centralizados como campos públicos en `NetworkGenerator` — es el primer lugar para
  buscar antes de tocar un componente individual.
- **24 objetos nuevos creados en bloque** (`TO_33`..`TO_56`, más sus prefabs en
  `Assets/Scenes/Prefabs/`) a partir de imágenes de `Assets/Scenes/Imagenes/ImagenesObjects/`
  que todavía no tenían objeto. Se clonó el template dominante que ya usaban 24 de
  los 27 objetos previos (mismo `FloatingImageEffect`, `localScale` child = 0.2,
  mismo material `SoftEdges.mat`) — NO se intentó normalizar tamaño por resolución
  real de cada imagen, porque el `m_Size` del SpriteRenderer resultó ser idéntico
  (12, 15.99) en los 27 prefabs existentes pese a fotos de resoluciones distintas —
  o sea, es un valor sin usar en Draw Mode Simple, no algo que haya que replicar con
  cuidado. **Sin tags** (`tags: []`) y con `infoText` placeholder
  (`"(Descripcion pendiente -- <nombre>)"`) a propósito — el usuario los va a
  taggear el mismo a mano en el Editor. Ya están registrados en
  `TaggedObjectDatabase.allObjects` (antes tenía 27 entradas, no 32 — `TO_28`..`TO_32`,
  los de test con tags "1"/"4"/"5", nunca se habían agregado a la base). Un nombre
  se tuvo que desambiguar: había dos fotos que hubieran generado el mismo nombre de
  prefab (`LaCienciaEsMujer.jpeg` vs `LaCienciaEsMujer.JPG`, esta última ya usada) —
  la nueva quedó como `LaCienciaEsMujer2`.
- **Convención de nombres `TO_<numero>_<prefab>`** — todos los `TaggedObjectData`
  que tienen `prefabOverride` están renombrados (archivo + `m_Name` interno) para
  incluir el nombre del prefab que referencian (ej. `TO_1_TutiAmigaNona.asset`).
  Los que NO tienen `prefabOverride` (`TO_28`..`TO_32`, los de test) quedaron sin
  sufijo. **`TO_17` es una excepción a proposito**: su `prefabOverride` apunta a un
  guid (`a6af345b00bbfd8468cda0377e729b75`) que no corresponde a NINGUN prefab
  existente en el proyecto — referencia rota, ya estaba asi antes de esta sesion
  (probablemente de un prefab borrado en algun momento anterior), no algo que se
  haya roto ahora. En el juego esto hace que ese nodo caiga al `nodePrefab`
  generico en vez de a su prefab con imagen (`FloatingImageEffect` etc.) --
  pendiente de que el usuario le reasigne el prefab correcto (o cree uno nuevo)
  a mano en el Editor. Renombrar assets por archivo (conservando el guid en el
  `.meta`) es seguro -- todas las referencias en el proyecto son por guid, nunca
  por nombre de archivo.
