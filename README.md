# Micelio

Proyecto Unity (Built-in Render Pipeline), primera persona. Genera proceduralmente
una red de nodos (imágenes/audio) conectados por tags compartidos, visualizada con
hilos tipo micelio.

## Sistemas de juego

- Movimiento en primera persona: WASD + mouse look (asset "Mini First Person
  Controller"), Rigidbody + CharacterController estándar.
- Detección de proximidad a nodos: por distancia (no colliders/triggers). Al
  sostenerse dentro de un radio un tiempo configurable, dispara la generación de
  contenido nuevo.
- Detección de mirada: por ángulo entre la cámara y el nodo, con histéresis (ángulo
  angosto para activar, amplio para mantener) y tiempo de sostenido configurable.
- Texto flotante (TextMeshPro) al sostener la mirada sobre un nodo: fondo con
  bordes difuminados vía shader, fade in/out.

## Generación procedural (`NetworkGenerator.cs`)

- Estructura de datos: `TaggedObjectData` (objeto individual: sprite o audio, lista
  de tags, texto opcional, prefab opcional) y `TaggedObjectDatabase` (lista central
  de todos los `TaggedObjectData`, búsqueda por tag).
- Tags: `NetworkTag`, ScriptableObject independiente (no `string`). Los objetos y
  el generador referencian el asset directamente.
- Generación en clusters: un tag común agrupa varios `TaggedObjectData` en un
  anillo circular alrededor de un punto central.
- Conectores: al sostener la mirada/proximidad sobre un nodo de un anillo, se
  genera un único nodo "puente" fuera del anillo, con un tag distinto al del
  cluster. Al aproximarse al puente, se genera un cluster nuevo centrado en su
  posición.
- Detección de superposición: si un nodo nuevo cae dentro de un radio mínimo de
  uno existente que comparte tag, se reutiliza el existente en vez de duplicar. Si
  no comparte tag, se reubica el nuevo nodo hacia afuera y se reintenta.
- Reconexión retroactiva: cualquier par de nodos dentro de un radio configurable
  que comparta al menos un tag se conecta con un hilo de micelio, independientemente
  de cuándo fue generado cada uno.
- Orientación hacia hubs vecinos: al elegir qué objetos poblar en un anillo nuevo,
  se prioriza el que comparte tag con un nodo existente cercano en esa dirección.
- Origen nuevo por alejamiento: si el nodo más cercano al jugador supera una
  distancia umbral, se genera un cluster nuevo cerca del jugador con un tag
  elegido al azar (prioriza tags no usados todavía).
- Objeto especial de centro (`NetworkTag.centerNodeOverride`): un tag puede tener
  asignado un `TaggedObjectData` que se instancia en el centro de cualquier cluster
  nuevo generado con ese tag (incluido el cluster inicial), en vez de dejar el
  centro vacío. Excluido de la selección normal del anillo. No aplica si el cluster
  se genera alrededor de un conector ya existente.
- Nivel de generación (`SpawnedNode.GenerationId`): todos los nodos ubicados en un
  mismo llamado de generación de cluster comparten número. La reconexión
  retroactiva no conecta nodos con el mismo `GenerationId` (ya están conectados vía
  el centro común); sí conecta nodos de generaciones distintas.

## Micelio (`MyceliumLink.cs`, `MyceliumLine.shader`)

- Camino orgánico entre dos puntos generado con ruido Perlin (desvío máximo a
  mitad de camino, cero en los extremos).
- Ramificaciones secundarias: 0–N por hilo, ángulo respecto a la dirección
  original configurable, sin destino (terminan en el aire).
- Animación de crecimiento: el hilo principal crece linealmente de 0 a 1 en un
  tiempo configurable; cada ramificación arranca cuando el crecimiento principal
  llega a su punto de origen, con curva logarítmica propia.
- Punta redondeada/aplanada controlada en el shader (`_TipCapRounded`), no con
  `numCapVertices` de `LineRenderer` (que redondea ambos extremos por igual). Se
  aplana cuando un hilo nuevo continúa desde ese punto.
- Color por colonia: cada cluster generado sin nodo central existente (origen
  genuinamente nuevo) recibe un color aleatorio (variación de tono/saturación en
  HSV sobre el color base del material). Un cluster generado alrededor de un
  conector hereda el color de ese conector. El color se aplica al hilo principal y
  a sus ramificaciones vía `Material.SetColor("_Color", ...)` por instancia.
- Brotes decorativos ("plato de petri"): al final de cada generación de cluster,
  se generan N hilos cortos sin conectar a ningún nodo, repartidos en ángulos
  parejos (con jitter) alrededor del centro. Cantidad, largo y jitter angular
  configurables. Independiente de cuántos nodos reales se ubicaron en el cluster.

## Nodos con imagen (`FloatingImageEffect.cs`, `SoftEdgeSprite.shader`)

- Billboard: rotación hacia la cámara (fallback al Player si no hay cámara).
- Flote vertical (seno) + vaivén lateral opcional + inclinación de balanceo
  (ruido/seno), todos con amplitud/velocidad configurables.
- Copias fantasma: N copias con offset fijo aleatorio, fase/velocidad/amplitud de
  flote propias, alpha decreciente por copia (falloff configurable).
- Bordes difuminados: máscara radial/ovalada aplicada en shader sobre el alpha del
  sprite, en vez de recorte rectangular duro.
- Desvanecido por distancia: entre dos radios configurables, el nodo interpola
  escala hacia 0, posición Y hacia un nivel de piso configurable (X/Z sin cambios),
  y alpha hacia 0. Aplica igual al sprite principal y a las copias fantasma.
  Escala base capturada en `Awake()` (antes de que la animación de aparición la
  modifique).

## Ambientación

- Niebla (`VolumetricHeightFog.cs`, post-proceso): niebla por distancia + niebla
  por altura, moduladas por una textura de ruido animada. Corre en
  `OnRenderImage` con `[ImageEffectOpaque]` (antes de que se dibujen objetos
  transparentes).
- Piso infinito (`InfiniteGroundFollower.cs`): el plano del piso se reposiciona en
  X/Z a la posición del jugador cada frame; el offset de la textura del material se
  compensa en sentido contrario para que no se note el desplazamiento.
- Lámina de agua (`WaterFilm.shader`): plano transparente aparte, por encima del
  piso. Dos normal maps de ripples animados en distinta escala/velocidad/dirección,
  combinados para perturbar un término especular y un término Fresnel (blend entre
  color base y color de "reflejo" según ángulo de vista). Sin reflexión planar
  real. Cola de render (`Queue`) un paso antes que la cola transparente default,
  para que nodos/hilos se dibujen siempre encima independientemente del orden de
  distancia calculado por Unity.

## Compañero flotante (`FloatingOrbitCompanion.cs`)

- Punto de órbita alrededor de un target (radio, altura, velocidad angular
  configurables), independiente de si el target está en movimiento.
- Sistema masa-resorte (integración semi-implícita, sub-pasos por frame) que
  persigue el punto de órbita: frecuencia y amortiguación configurables;
  amortiguación menor a 1 produce sobrepaso y retorno.
- Suavizado de la lectura de posición del target (`Vector3.SmoothDamp`) antes de
  alimentar el resorte, para filtrar ruido en la fuente sin agregar sobrepaso.
- Flote vertical (seno) sumado después del resorte (no pasa por la física del
  resorte).
- Rotación: inclinación en pitch/roll vía ruido Perlin (dos ejes con semillas
  distintas) + rotación constante en yaw.
- Escrito para operar en espacio de mundo (`transform.position/rotation`
  absolutos); no requiere estar parentado al target.

## Herramientas de contenido

- `TaggedObjectDatabase.allObjects`: lista central que el generador consulta en
  runtime.
- Creación en bloque de objetos: script de generación que, a partir de una carpeta
  de imágenes sin usar, crea un prefab por imagen (clonado de un template con
  `SpriteRenderer` + `FloatingImageEffect`) y su `TaggedObjectData`
  correspondiente, y los registra en la base.
- Convención de nombres: `TO_<número>_<nombre de prefab>` para los
  `TaggedObjectData` que tienen prefab asociado.

## Decisiones de render pipeline

- Built-in RP, no URP/HDRP.
- Niebla, agua y (descartada) curvatura de mundo implementadas como
  aproximaciones de bajo costo en vez de sus versiones físicamente más precisas
  (raymarching, reflexión planar real, deformación de malla completa).

## Configuración centralizada

`NetworkGenerator` expone como campos públicos: radios de cluster/conector,
tiempos de dwell (proximidad y mirada), ángulos de detección de mirada, estilo y
fondo del texto flotante, parámetros del micelio (largo/cantidad de brotes,
variación de color de colonia), radios de separación/reconexión, y parámetros de
generación de nuevo origen por alejamiento.

## Estado

- Funcionando: generación de red, micelio (con color de colonia y brotes
  decorativos), niebla, piso infinito, nodos con imagen (con desvanecido por
  distancia), texto por mirada.
- Lámina de agua: implementada y cableada en la escena, no verificada
  visualmente en Editor.
- Compañero flotante: implementado, no instanciado/conectado en la escena.
- Base de datos: 56 `TaggedObjectData` registrados. Uno (`TO_17`) con referencia a
  prefab inexistente (pendiente de reasignar). 24 objetos sin tags asignados
  (pendiente de taggeo manual).

---

Detalle de implementación, historial de decisiones y bugs resueltos:
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md).
