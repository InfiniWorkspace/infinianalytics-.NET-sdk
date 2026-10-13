# Guía de usuario del SDK de .NET de InfiniAnalytics

Esta guía explica cómo instalar el SDK de .NET de InfiniAnalytics y cómo usarlo para registrar
las ejecuciones de tus automatizaciones en la plataforma de Infini Analytics: cuándo empiezan,
qué eventos se producen, qué advertencias y errores se producen y cuándo terminan.

## Índice

1. [Qué es el SDK](#qué-es-el-sdk)
2. [Requisitos y compatibilidad](#requisitos-y-compatibilidad)
3. [Datos que necesitas](#datos-que-necesitas)
4. [Instalación](#instalación)
5. [Primeros pasos](#primeros-pasos)
6. [Registrar eventos](#registrar-eventos)
7. [Cómo se agrupan los eventos en una ejecución](#cómo-se-agrupan-los-eventos-en-una-ejecución)
8. [Uso síncrono](#uso-síncrono)
9. [Uso en plataformas RPA](#uso-en-plataformas-rpa)
10. [Configuración](#configuración)
11. [Comprobar la conectividad](#comprobar-la-conectividad)
12. [API de bajo nivel](#api-de-bajo-nivel)
13. [Comportamiento ante fallos](#comportamiento-ante-fallos)
14. [Problemas frecuentes](#problemas-frecuentes)

## Qué es el SDK

El SDK es una librería de .NET (paquete NuGet `InfiniAnalytics.Sdk`) que envía a Infini Analytics
los eventos de cada ejecución de una automatización. Está pensado para:

- Aplicaciones y servicios escritos en C# o VB.NET.
- Flujos de Power Automate Desktop (acción "Ejecutar script de .NET").
- Robots de UiPath (actividad "Invoke Code").
- Objetos de negocio de Blue Prism ("code stages").

Su principio de diseño es que **nunca debe romper tu automatización**. Si la API no responde,
tarda demasiado o devuelve un error, el SDK lo anota en el log y sigue adelante sin lanzar
excepciones. Tu proceso continúa como si nada hubiera pasado.

## Requisitos y compatibilidad

El paquete incluye varias compilaciones y .NET elige automáticamente la adecuada:

| Plataforma | Compilación que se usa |
|---|---|
| .NET Framework 4.6.2 o superior (Power Automate Desktop, UiPath Legacy, Blue Prism) | `net462` |
| .NET 10 o superior | `net10.0` (sin dependencias externas) |
| Resto de plataformas compatibles con .NET Standard 2.0 (.NET 6, 8, 9, Mono...) | `netstandard2.0` |

Además, el equipo donde se ejecuta la automatización necesita acceso por HTTPS (TLS 1.2) a
`https://api.analytics.infini.es`.

## Datos que necesitas

Antes de empezar, ten a mano estos dos datos:

- **Token de organización.** Es una cadena secreta que identifica a tu organización ante la API.
  La encontrarás en el panel de Infini Analytics, en la página donde se gestiona tu organización.
  Trátala como una contraseña: no la escribas en el código, léela de la configuración, de una
  variable de entorno o de un almacén de credenciales (Assets de Orchestrator en UiPath,
  Credentials en Blue Prism).
- **Identificador de la automatización (`automationId`).** Es un UUID con el formato
  `44444444-4444-4444-4444-444444444444`, que identifica la automatización cuyas ejecuciones vas
  a registrar. Lo encontrarás en tu listado de automatismos del panel; si no lo tienes, pídeselo
  al administrador de la plataforma.

## Instalación

### Proyectos .NET (C#, VB.NET)

Con la línea de comandos de .NET:

```bash
dotnet add package InfiniAnalytics.Sdk
```

En Visual Studio, abre "Administrar paquetes NuGet" en el proyecto, busca `InfiniAnalytics.Sdk`
e instálalo.

### UiPath

En UiPath Studio, abre "Manage Packages", busca `InfiniAnalytics.Sdk` e instálalo en el proyecto.

### Power Automate Desktop y Blue Prism

Estas plataformas no instalan paquetes NuGet. En su lugar, se usa un fichero `.zip` con el SDK y
todas sus dependencias:

1. Descarga el fichero `InfiniAnalytics-<versión>-dotnet-framework-dlls.zip` de la página de
   [Releases](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/releases).
2. Antes de descomprimirlo, haz clic derecho sobre él, abre "Propiedades" y marca "Desbloquear"
   si aparece. Así Windows no bloquea las DLL por venir de Internet.
3. Descomprímelo en una carpeta fija del equipo donde se ejecuta el robot.

Los pasos concretos de cada plataforma están en [Uso en plataformas RPA](#uso-en-plataformas-rpa).

## Primeros pasos

Este ejemplo completo registra una ejecución con un inicio, un evento, un posible error y un fin:

```csharp
using System;
using System.Threading.Tasks;
using InfiniAnalytics;

class Program
{
    static async Task Main()
    {
        var client = new InfiniAnalyticsClient(
            Environment.GetEnvironmentVariable("INFINIANALYTICS_TOKEN"));

        var execution = await client.StartExecutionAsync(
            "44444444-4444-4444-4444-444444444444",
            description: "Inicio del proceso de facturas");

        try
        {
            // Lógica de tu automatización
            await execution.EventAsync("Facturas descargadas");
        }
        catch (Exception ex)
        {
            await execution.ErrorAsync("Fallo procesando facturas", ex, errorId: "E001");
        }

        await execution.EndAsync("Fin del proceso de facturas");
    }
}
```

Los pasos son siempre los mismos:

1. **Crear el cliente** (`InfiniAnalyticsClient`) con el token. Es seguro usarlo desde varios
   hilos: crea uno y reutilízalo.
2. **Iniciar la ejecución** con `StartExecutionAsync(automationId)`. Registra el evento `START`
   y devuelve un objeto `Execution`.
3. **Registrar eventos** sobre ese `Execution`: eventos intermedios, advertencias y errores.
4. **Cerrar la ejecución** con `EndAsync`.

Cada método envía a la API uno de los cinco tipos de evento que acepta: `START`, `EVENT`,
`WARNING`, `ERROR` y `END`. Están explicados en [Tipos de evento](#tipos-de-evento).

`StartExecutionAsync` admite tres parámetros:

| Parámetro | Obligatorio | Descripción |
|---|---|---|
| `automationId` | Sí | UUID de la automatización. |
| `executionId` | No | Identificador único de la ejecución: una fecha y hora (por ejemplo `2026-07-27T11:52:15.716617`) o un UUID. Si lo omites, se genera automáticamente. Ver [El identificador de ejecución](#el-identificador-de-ejecución-executionid). |
| `description` | No | Texto del evento `START`. |

Siempre devuelve un `Execution`, incluso si el registro del `START` falla, para que tu código
pueda seguir llamando a sus métodos sin comprobaciones adicionales.

## Registrar eventos

### Evento intermedio (`EventAsync`)

```csharp
await execution.EventAsync("Facturas descargadas");
```

Registra un evento relevante en el transcurso de la ejecución.

### Advertencia (`WarningAsync`)

```csharp
await execution.WarningAsync("La factura 1042 no tiene fecha de vencimiento");
```

Registra una situación que no es un error crítico pero requiere atención: valores inesperados,
configuraciones mejorables o condiciones que podrían causar problemas más adelante.

### Error (`ErrorAsync`)

Lo habitual es llamarlo dentro de un bloque `catch`:

```csharp
try
{
    // Lógica que puede fallar
}
catch (Exception ex)
{
    await execution.ErrorAsync("Ocurrió un error durante el proceso", ex, errorId: "E001");
}
```

Parámetros:

| Parámetro | Descripción |
|---|---|
| `description` | Descripción general del error. |
| `exception` | La excepción capturada. Si la pasas, el detalle del error se genera a partir de ella con el formato `"<Tipo>: <mensaje>"`, por ejemplo `InvalidOperationException: boom`. |
| `errorId` | Código de error propio, para agrupar o filtrar errores en el panel. |
| `errorDescription` | Detalle del error. Si lo indicas, tiene prioridad sobre el generado a partir de la excepción. |

Por ejemplo, para enviar la traza completa de la excepción:

```csharp
await execution.ErrorAsync(
    "Ocurrió un error durante el proceso",
    errorId: "E001",
    errorDescription: ex.ToString());
```

### Fin (`EndAsync`)

```csharp
await execution.EndAsync("Fin del proceso");
```

Registra el final de la ejecución. Si antes no se ha registrado ningún `ERROR`, la ejecución se
contabiliza como exitosa.

### Resultado de cada llamada

Cada método devuelve el evento tal y como lo ha guardado la API (un `ExecutionEventResult`), o
`null` si no se ha podido registrar. El resultado incluye estas propiedades:

| Propiedad | Descripción |
|---|---|
| `AutomationId` | UUID de la automatización. |
| `ExecutionId` | Identificador de la ejecución. |
| `EventType` | Tipo de evento (`Start`, `Event`, `Warning`, `Error`, `End`). |
| `Description` | Descripción del evento. |
| `ErrorId` | Código de error. |
| `ErrorDescription` | Detalle del error. |
| `CreatedAt` | Fecha y hora en que la API guardó el evento. |
| `Environment` | Entorno al que se ha asignado el evento, si la API lo devuelve. |

No es necesario comprobar el resultado: si algo falla, el SDK ya lo ha anotado en el log.

## Cómo se agrupan los eventos en una ejecución

### Tipos de evento

La API de InfiniAnalytics solo acepta estos cinco tipos de evento: **`START`, `EVENT`, `WARNING`,
`ERROR` y `END`**. Cualquier otro valor se rechaza. No tienes que escribirlos a mano: cada método
del SDK envía el suyo.

| Evento | Método | Efecto en la ejecución |
|---|---|---|
| `START` | `StartExecutionAsync` | Registra el inicio de la ejecución. Debe ser el primer evento que se envía para un `executionId`. |
| `EVENT` | `EventAsync` | Registra un evento relevante durante la ejecución, sin marcarla como finalizada, con advertencia o fallida. |
| `WARNING` | `WarningAsync` | Registra una advertencia: algo que merece la pena registrar pero no es lo bastante grave como para que la ejecución falle. |
| `ERROR` | `ErrorAsync` | Registra un fallo o excepción. La ejecución se contabiliza como fallida. |
| `END` | `EndAsync` | Registra el fin de la ejecución. Si no se registró ningún `ERROR` antes, la ejecución se contabiliza como exitosa. |

Si usas la [API de bajo nivel](#api-de-bajo-nivel), el tipo se indica con el enumerado
`EventType` (`Start`, `Event`, `Warning`, `Error` y `End`), que el SDK convierte en esos mismos
cinco valores al enviarlo.

### Errores no fatales

Un `ERROR` no obliga a empezar otra ejecución. Si después de un `ERROR` llegan más eventos con el
mismo identificador de ejecución en menos de 1 hora, se asocian a esa misma ejecución, y un `END`
posterior la deja en estado "finalizada con error". Así puedes notificar errores puntuales y
continuar con el proceso:

```csharp
foreach (var invoice in invoices)
{
    try
    {
        Process(invoice);
    }
    catch (Exception ex)
    {
        await execution.ErrorAsync($"Factura {invoice.Number} no procesada", ex);
    }
}

await execution.EndAsync("Proceso terminado");
```

### El identificador de ejecución (`executionId`)

El `executionId` identifica una ejecución concreta y cumple dos reglas:

- **Es el mismo para toda la ejecución.** Todos sus eventos, desde el `START` hasta el `END`,
  deben enviarse con el mismo `executionId` que se usó en el `START`. Es lo que permite a
  InfiniAnalytics agruparlos.
- **Es único para cada ejecución.** Cada nueva ejecución de la automatización debe tener un
  `executionId` distinto, y nunca puede reutilizarse en dos ejecuciones concurrentes. Si
  reutilizas uno, los eventos de dos ejecuciones se mezclan.

Lo habitual es usar uno de estos dos formatos:

| Formato | Ejemplo |
|---|---|
| Marca de tiempo (fecha y hora en que empieza la ejecución) | `2026-07-27T11:52:15.716617` |
| UUID | `3f2b8c1e-9d4a-4e7b-8f6c-2a1d5e9b7c30` |

También puedes usar un identificador que ya utilices internamente en tu sistema, siempre que
cumpla las dos reglas anteriores.

Cómo se obtiene:

- Si no indicas ninguno en `StartExecutionAsync`, el SDK genera uno con la fecha y hora actuales
  en UTC, en formato ISO 8601, por ejemplo `2026-10-02T15:04:05.123Z`. Es la opción más sencilla
  si tu automatización no se ejecuta varias veces a la vez.
- Lo genera el SDK en el equipo donde se ejecuta la automatización, no la API. Lo que ves en el
  panel en la columna "ID Ejecución" es exactamente ese valor.
- Puedes consultarlo en cualquier momento con `execution.ExecutionId`.

Si tu automatización puede ejecutarse en paralelo (por ejemplo, en varios robots a la vez), dos
ejecuciones podrían empezar en el mismo instante y recibir la misma fecha. En ese caso, usa un
UUID:

```csharp
var execution = await client.StartExecutionAsync(
    automationId,
    executionId: Guid.NewGuid().ToString());
```

### Continuar una ejecución en otro paso

En algunas plataformas, los objetos no sobreviven de un paso a otro (otra actividad "Invoke
Code" de UiPath, otra acción de Power Automate Desktop, otro stage de Blue Prism). En ese caso:

1. Guarda `execution.ExecutionId` en una variable o argumento de tu plataforma.
2. En el paso siguiente, reconstruye el `Execution` a partir de los identificadores:

```csharp
var execution = new Execution(client, automationId, executionId);
await execution.EndAsync("Fin del proceso");
```

Reconstruir el `Execution` **no** envía un nuevo `START`: los eventos siguientes se añaden a la
ejecución que ya estaba abierta.

## Uso síncrono

Cada método asíncrono tiene una versión síncrona con el mismo nombre sin el sufijo `Async`:
`StartExecution`, `Event`, `Warning`, `Error`, `End`, `Register` y `Ping`. Son útiles en entornos
donde no es cómodo usar `await`, como las plataformas RPA, y se pueden llamar desde cualquier hilo
(incluidos hilos de interfaz y el hilo del robot) sin riesgo de bloqueos.

```csharp
var client = new InfiniAnalyticsClient(token);
var execution = client.StartExecution(automationId, description: "Inicio");

execution.Event("Facturas descargadas");
execution.End("Fin del proceso");
```

## Uso en plataformas RPA

### Power Automate Desktop

El SDK se usa desde la acción "Ejecutar script de .NET" en C#, cargando las DLL desde la carpeta
donde descomprimiste el `.zip`. La configuración tiene varios pasos (variables del flujo, un
subflujo de registro y la gestión de errores de bloque), así que está explicada en una guía
propia:
[InfiniAnalytics en Power Automate Desktop](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/blob/main/docs/power-automate-desktop.es.md).

Solo funciona con flujos de escritorio. Los flujos de nube de Power Automate no pueden ejecutar
código .NET.

### UiPath (Invoke Code)

1. Instala el paquete `InfiniAnalytics.Sdk` desde "Manage Packages".
2. En el panel "Imports" del workflow, añade el namespace `InfiniAnalytics`.
3. En la actividad "Invoke Code" (VB.NET por defecto), usa la API síncrona:

```vb
Dim client As New InfiniAnalyticsClient(token)
Dim execution = client.StartExecution(automationId, description:="Inicio del proceso")
Try
    execution.Event("Facturas descargadas")
Catch ex As Exception
    execution.Error("Fallo procesando facturas", ex, errorId:="E001")
End Try
execution.End("Fin del proceso")
executionId = execution.ExecutionId
```

Aquí `token` y `automationId` son argumentos de entrada de la actividad y `executionId` es un
argumento de salida. Lee el token de un Asset de Orchestrator en lugar de escribirlo en el
workflow.

Para registrar más eventos de la misma ejecución en otra actividad "Invoke Code", pásale el
`executionId` como argumento y reconstruye el `Execution`:

```vb
Dim execution As New Execution(New InfiniAnalyticsClient(token), automationId, executionId)
execution.End("Fin del proceso")
```

Crear un cliente en cada "Invoke Code" no supone ningún problema: todos comparten internamente la
misma conexión HTTP y no hace falta liberar nada.

### Blue Prism

1. Copia en la carpeta de instalación de Blue Prism todas las DLL del `.zip`:
   `InfiniAnalytics.dll` y sus dependencias (`System.Text.Json.dll`,
   `System.Text.Encodings.Web.dll`, `System.Memory.dll`, `System.Buffers.dll`,
   `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`,
   `System.Threading.Tasks.Extensions.dll`, `System.ValueTuple.dll` y
   `Microsoft.Bcl.AsyncInterfaces.dll`).
2. En las "Code Options" del objeto de negocio, añade `InfiniAnalytics.dll` a las referencias
   externas e `InfiniAnalytics` a los namespaces importados.
3. En los code stages, usa la API síncrona igual que en el ejemplo de UiPath.
4. Guarda el `executionId` en un data item para reconstruir el `Execution` en otros stages.

## Configuración

Para algo más que el token, crea el cliente con `InfiniAnalyticsClientOptions`:

```csharp
var client = new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions
{
    Token = configuration["InfiniAnalytics:Token"],
    Timeout = TimeSpan.FromSeconds(10),
    Logger = (level, message, exception) =>
        Console.WriteLine($"{level}: {message} {exception?.Message}"),
});
```

| Opción | Valor por defecto | Descripción |
|---|---|---|
| `Token` | (obligatorio) | Token de la organización. Se eliminan los espacios y saltos de línea de los extremos. |
| `BaseUrl` | `https://api.analytics.infini.es` | URL base de la API. Normalmente no hace falta cambiarla. Si la cambias, usa siempre `https://`: la API responde a las peticiones HTTP con una redirección y el evento no se registra. |
| `Timeout` | 10 segundos | Tiempo máximo de cada llamada. Si se agota, la llamada se anota en el log y devuelve `null`. `Timeout.InfiniteTimeSpan` lo desactiva. |
| `Logger` | Salida de error estándar (`Console.Error`) | Recibe los mensajes del SDK: nivel (`Warning` o `Error`), mensaje y excepción si la hay. |

El timeout corto por defecto está pensado para que un robot nunca se quede colgado esperando a la
API.

### Logging

Por defecto, los fallos se escriben en la salida de error estándar con el prefijo
`[InfiniAnalytics]`. En un robot esa salida no suele verse, así que conviene redirigirla al log
de tu plataforma con la opción `Logger`. Los mensajes nunca contienen el token, y si tu propio
logger lanza una excepción, el SDK la ignora.

Ejemplo con `Microsoft.Extensions.Logging`:

```csharp
ILogger logger = loggerFactory.CreateLogger("InfiniAnalytics");

var options = new InfiniAnalyticsClientOptions
{
    Token = "TU_TOKEN",
    Logger = (level, message, exception) => logger.Log(
        level == InfiniAnalyticsLogLevel.Error ? LogLevel.Error : LogLevel.Warning,
        exception,
        "{Message}",
        message),
};
```

### HttpClient e IHttpClientFactory

Si no indicas otro, el SDK usa un `HttpClient` interno compartido por todo el proceso, así que
puedes crear tantos clientes como necesites sin agotar las conexiones.

En aplicaciones ASP.NET Core o servicios con inyección de dependencias puedes pasar tu propio
`HttpClient`, por ejemplo uno gestionado por `IHttpClientFactory`. El SDK no lo libera y no usa
su `BaseAddress` (la URL se toma siempre de `BaseUrl`):

```csharp
builder.Services.AddHttpClient<InfiniAnalyticsClient>((http, services) =>
    new InfiniAnalyticsClient(http, new InfiniAnalyticsClientOptions
    {
        Token = builder.Configuration["InfiniAnalytics:Token"]!,
    }));
```

### Límites de longitud de los textos

La API rechaza los textos que superan estos límites. Para no perder el evento, el SDK los recorta
antes de enviarlos y anota un aviso en el log:

| Campo | Máximo |
|---|---|
| Descripción (`description`) | 2000 caracteres |
| Código de error (`errorId`) | 255 caracteres |
| Detalle del error (`errorDescription`) | 5000 caracteres |

### Timeout y cancelación

Todos los métodos asíncronos aceptan un `CancellationToken` opcional como último parámetro:

- Si se agota el `Timeout` del SDK, la llamada se anota en el log y devuelve `null`, como
  cualquier otro fallo de comunicación.
- Si eres tú quien cancela el `CancellationToken`, el método lanza `OperationCanceledException`,
  como es habitual en .NET: la cancelación es una decisión tuya, no un fallo de comunicación.

## Comprobar la conectividad

```csharp
bool reachable = await client.PingAsync();
```

Llama al endpoint `/v1/ping/` de la API con tu token. Devuelve `true` si la API está accesible
desde el equipo y acepta el token, y `false` en caso contrario (por ejemplo, si el token no es
válido). El motivo del fallo queda anotado en el log.

Es útil para comprobar la configuración antes de empezar a registrar eventos. Ten en cuenta que no
comprueba el `automationId`: para verificarlo, registra una ejecución de prueba y comprueba que
aparece en el panel.

## API de bajo nivel

Si necesitas control total sobre lo que se envía, o prefieres indicar el `automationId` y el
`executionId` en cada llamada en lugar de usar un `Execution`, usa `client.RegisterAsync()`:

```csharp
await client.RegisterAsync(new RegisterEventPayload
{
    AutomationId = "44444444-4444-4444-4444-444444444444",
    ExecutionId = "2026-10-02T15:04:05.123Z",
    Event = EventType.Start,
    Description = "Inicio de la ejecución",
});
```

`RegisterEventPayload` tiene las propiedades `AutomationId`, `ExecutionId`, `Event` (`Start`,
`Event`, `Warning`, `Error` o `End`), `Description`, `ErrorId` y `ErrorDescription`.

## Comportamiento ante fallos

| Situación | Qué hace el SDK |
|---|---|
| La API no responde, hay un error de red o la API devuelve un error | Lo anota en el log y devuelve `null` (`false` en `Ping`). No lanza excepciones. |
| Se agota el timeout | Lo anota en el log y devuelve `null`. |
| Un texto supera el límite de longitud | Lo recorta, anota un aviso y envía el evento. |
| Creas el cliente sin token, con una URL no válida o con un timeout no positivo | Lanza una excepción al crear el cliente. Es un error de programación, no un fallo de comunicación, y conviene detectarlo cuanto antes. |
| Cancelas tu propio `CancellationToken` | Lanza `OperationCanceledException`. |

## Problemas frecuentes

**No veo los errores del SDK en el log del robot.**
Por defecto se escriben en `Console.Error`, que en muchas plataformas RPA no se muestra. Configura
la opción `Logger` para enviarlos al log de tu plataforma.

**Error de TLS o de conexión segura en .NET Framework antiguo.**
La API requiere TLS 1.2. Las aplicaciones compiladas para .NET Framework 4.7 o superior lo usan
por defecto; en versiones anteriores puede ser necesario activarlo en la aplicación que aloja el
robot. El SDK no cambia esta configuración porque afecta a todo el proceso.

**`FileNotFoundException` o `FileLoadException` de `System.Text.Json` u otra DLL en .NET
Framework.**
Faltan dependencias junto a `InfiniAnalytics.dll`, o la aplicación que aloja el robot carga otra
versión. Copia todas las DLL del `.zip` y, si hace falta, añade las redirecciones de ensamblado
(`bindingRedirect`) en el fichero `.config` de la aplicación.

**Las DLL no se cargan en Power Automate Desktop o Blue Prism.**
Comprueba que desbloqueaste el `.zip` antes de descomprimirlo (clic derecho, "Propiedades",
"Desbloquear"). Si no, Windows puede impedir que se carguen por venir de Internet.

**Los eventos aparecen en ejecuciones distintas.**
Todos los eventos deben compartir el mismo `executionId`. Si registras eventos en pasos
distintos, pasa `execution.ExecutionId` de un paso a otro y reconstruye el `Execution` como se
explica en [Continuar una ejecución en otro paso](#continuar-una-ejecución-en-otro-paso).

**La API responde 401.**
Falta el token o no es válido. Comprueba que es el token de tu organización, tal y como aparece
en el panel.

**La API responde 400.**
La automatización no existe o el tipo de evento no es válido.

**La API responde 422.**
Los datos enviados no son válidos, por ejemplo porque el `automationId` no es un UUID.

**La API responde 500 u otro error 5xx.**
El servidor tiene una carga inusualmente alta o un problema temporal. El SDK lo anota en el log y
tu automatización continúa. Si el error persiste, contacta con el soporte de Infini Analytics.
