# InfiniAnalytics para .NET

InfiniAnalytics es una librería de .NET que facilita el registro de eventos, inicios y finales
de procesos, así como la notificación de errores, en la plataforma de Analítica de Infini.

Está pensada para automatizaciones escritas en .NET: aplicaciones y servicios en C#, robots de
UiPath (actividad "Invoke Code") y "code stages" de Blue Prism.

El diseño está orientado a que nunca quiebre el flujo de tu automatismo o aplicación: ante
cualquier error de comunicación, timeout o respuesta inesperada de la API, la librería registra
el fallo en el log y devuelve `null` (o `false` en el caso de `Ping`), pero no lanza excepciones.

## Características principales

- Registro de inicio (`client.StartExecutionAsync(automationId, executionId?, description?)`)
- Registro de evento (`execution.EventAsync(description)`)
- Registro de warning (`execution.WarningAsync(description)`)
- Registro de errores (`execution.ErrorAsync(description, exception?, errorId?, errorDescription?)`)
- Registro de fin (`execution.EndAsync(description)`)
- Versión síncrona de cada método (`StartExecution`, `Event`, `Warning`, `Error`, `End`) para
  entornos donde no es cómodo usar `await`, sin riesgo de bloqueos (deadlocks).
- **No lanza excepciones** ante un fallo de comunicación, un timeout o una respuesta no 2xx de la
  API: cada llamada devuelve `null` y lo registra en el log. Un uso incorrecto del propio SDK,
  como no pasar un token, sí lanza una excepción al crear el cliente: eso es un error de
  programación, no un fallo de comunicación.
- Timeout corto por defecto (10 segundos), para que un robot no se quede colgado si la API no
  responde.
- El token nunca se escribe en el log.

## Compatibilidad

| Plataforma | Build que se usa |
|---|---|
| .NET Framework 4.6.2 o superior (UiPath Legacy, Blue Prism) | `net462` |
| .NET 10 o superior | `net10.0` (sin dependencias externas) |
| Resto de plataformas compatibles con .NET Standard 2.0 (.NET 6, 8, 9, Mono...) | `netstandard2.0` |

## Instalación

```bash
dotnet add package InfiniAnalytics.Sdk
```

En UiPath, instálalo desde "Manage Packages" buscando `InfiniAnalytics.Sdk`. En Blue Prism,
consulta la sección [Blue Prism](#blue-prism).

## Uso básico

### 1. Crear el cliente e iniciar una ejecución

```csharp
using InfiniAnalytics;

var client = new InfiniAnalyticsClient("TU_TOKEN");

var execution = await client.StartExecutionAsync(
    "44444444-4444-4444-4444-444444444444",
    description: "Starting the process");
```

- `token`: token de tu organización, que te identifica para la autenticación. Lo encontrarás en
  el panel de InfiniAnalytics. No lo escribas en el código: léelo de la configuración, de una
  variable de entorno o de un almacén de credenciales (Orchestrator Assets en UiPath, Credentials
  en Blue Prism).
- `automationId`: identificador (UUID) de tu automatización.
- `executionId` (opcional): identificador de la ejecución. Si lo omites, se genera automáticamente.

`StartExecutionAsync(...)` registra el evento `START` y devuelve un `Execution` con el que
registrar el resto de eventos de esa ejecución. Siempre devuelve un `Execution`, aunque el
registro del `START` falle, para que tu automatización pueda seguir llamando a sus métodos.

El cliente es seguro para usarse desde varios hilos: crea uno y reutilízalo.

### 2. Registrar eventos

#### Evento intermedio (`EventAsync`)

```csharp
await execution.EventAsync("An event occurred");
```

Registra un evento relevante (un hito) en el transcurso de la ejecución.

#### Warning (`WarningAsync`)

```csharp
await execution.WarningAsync("Warning: This is a non-critical issue");
```

Registra una advertencia durante la ejecución. Los warnings son útiles para notificar
situaciones que no son errores críticos pero que requieren atención, como valores inesperados,
configuraciones subóptimas o condiciones que podrían llevar a problemas futuros.

#### Error (`ErrorAsync`)

Para registrar un error dentro de un `try/catch`:

```csharp
try
{
    // Lógica que puede fallar
}
catch (Exception ex)
{
    await execution.ErrorAsync("Ocurrió un error durante el proceso", ex, errorId: "1234");
}
```

Si pasas la excepción, el detalle del error (`error_description`) se deriva de ella con el
formato `"<Tipo>: <mensaje>"`, por ejemplo `InvalidOperationException: boom`. También puedes
pasar `errorDescription` explícitamente, que tiene prioridad sobre el derivado de la excepción:

```csharp
await execution.ErrorAsync(
    "Ocurrió un error durante el proceso",
    errorId: "1234",
    errorDescription: ex.ToString());
```

**Importante:** se recomienda llamar siempre a `execution.ErrorAsync(...)` dentro de un bloque
`catch`, para asegurarte de capturar cualquier excepción y notificarla a la API. La librería no
lanza excepciones, por lo que tu automatismo no se detendrá.

#### Fin (`EndAsync`)

```csharp
await execution.EndAsync("Ending the process");
```

Registra el final de la ejecución.

#### Resultado de cada llamada

Cada método devuelve el evento tal y como lo ha guardado la API (`ExecutionEventResult`, con
`AutomationId`, `ExecutionId`, `EventType`, `Description`, `ErrorId`, `ErrorDescription`,
`CreatedAt` y `Environment`), o `null` si no se ha podido registrar. No es necesario
comprobarlo: el fallo ya queda registrado en el log.

## Semántica de los eventos

| Evento | Método | Efecto en la ejecución |
|---|---|---|
| `START` | `StartExecutionAsync` | Abre una ejecución. |
| `EVENT` | `EventAsync` | Hito intermedio. |
| `WARNING` | `WarningAsync` | Hito intermedio que requiere atención. |
| `ERROR` | `ErrorAsync` | Cierra la ejecución como fallida (ver abajo). |
| `END` | `EndAsync` | Cierra la ejecución como finalizada. |

Un `ERROR` no obliga a abrir otra ejecución. Si después de un `ERROR` llegan más eventos con el
mismo `executionId` en menos de 1 hora, se vuelven a asociar a esa misma ejecución, y un `END`
posterior la deja en estado "finalizada con error". Así puedes notificar errores no fatales y
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

### Identificador de ejecución (`executionId`)

Cada vez que llamas a `StartExecutionAsync(...)` se usa el `executionId` que le pases o, si no
pasas ninguno (o pasas una cadena vacía), se genera uno con la fecha y hora actuales en UTC en
formato ISO 8601, por ejemplo `2026-10-02T15:04:05.123Z`.

Un `executionId` identifica una ejecución y no debe reutilizarse en dos ejecuciones simultáneas
de la misma automatización. Si una automatización puede ejecutarse en paralelo (por ejemplo, en
varios robots a la vez), pasa un identificador propio único, como `Guid.NewGuid().ToString()`.

## Uso síncrono

Cada método asíncrono tiene una versión síncrona con el mismo nombre sin el sufijo `Async`:
`StartExecution`, `Event`, `Warning`, `Error`, `End`, `Register` y `Ping`. Se pueden llamar desde
cualquier hilo (incluidos hilos de interfaz y el hilo del robot) sin riesgo de bloqueos.

```csharp
var client = new InfiniAnalyticsClient("TU_TOKEN");
var execution = client.StartExecution("44444444-4444-4444-4444-444444444444", description: "Inicio");

execution.Event("Facturas descargadas");
execution.End("Fin del proceso");
```

### UiPath (Invoke Code)

1. Instala el paquete `InfiniAnalytics.Sdk` desde "Manage Packages".
2. En el panel "Imports" del workflow, añade el namespace `InfiniAnalytics`.
3. En la actividad "Invoke Code" (VB.NET por defecto):

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

Donde `token` y `automationId` son argumentos de entrada y `executionId` un argumento de salida
de la actividad. Lee el token de un Asset de Orchestrator en lugar de escribirlo en el workflow.

Los objetos no sobreviven de un "Invoke Code" a otro. Para registrar más eventos de la misma
ejecución en otra actividad, pásale el `executionId` como argumento y reconstruye el `Execution`
(esto no envía un nuevo `START`):

```vb
Dim execution As New Execution(New InfiniAnalyticsClient(token), automationId, executionId)
execution.End("Fin del proceso")
```

### Blue Prism

1. Copia en la carpeta de instalación de Blue Prism la DLL `InfiniAnalytics.dll` del build
   `net462` del paquete (carpeta `lib/net462`) y sus dependencias: `System.Text.Json.dll`,
   `System.Text.Encodings.Web.dll`, `System.Memory.dll`, `System.Buffers.dll`,
   `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`,
   `System.Threading.Tasks.Extensions.dll`, `System.ValueTuple.dll` y
   `Microsoft.Bcl.AsyncInterfaces.dll`.
2. En las "Code Options" del objeto de negocio, añade `InfiniAnalytics.dll` a las referencias
   externas y `InfiniAnalytics` a los namespaces importados.
3. Usa la API síncrona en los code stages, igual que en el ejemplo de UiPath. Guarda el
   `executionId` en un data item para reconstruir el `Execution` en otros stages.

## Comprobar conectividad

```csharp
bool reachable = await client.PingAsync();
```

Devuelve `true` si la API responde con un código 2xx y `false` en caso contrario.

**Nota:** de momento `PingAsync` consulta `GET /health`, que confirma que la API está accesible
pero **no valida el token**. Cuando el backend publique `GET /v1/ping/`, el SDK pasará a usarlo y
validará también el token. Para comprobar que el token y el `automationId` son correctos, registra
una ejecución de prueba.

## API de bajo nivel

Si necesitas control total sobre el payload (o quieres enviar tu propio `automationId` y
`executionId` en cada llamada en vez de usar el `Execution` devuelto por `StartExecutionAsync`),
usa `client.RegisterAsync()` directamente:

```csharp
await client.RegisterAsync(new RegisterEventPayload
{
    AutomationId = "44444444-4444-4444-4444-444444444444",
    ExecutionId = "2025-03-03T15:32:17.571404",
    Event = EventType.Start,
    Description = "Starting the execution",
});
```

## Configuración

```csharp
var client = new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions
{
    Token = configuration["InfiniAnalytics:Token"],
    BaseUrl = "https://api.analytics.infini.es",
    Timeout = TimeSpan.FromSeconds(10),
    Logger = (level, message, exception) => Console.WriteLine($"{level}: {message} {exception?.Message}"),
});
```

| Opción | Por defecto | Descripción |
|---|---|---|
| `Token` | (obligatorio) | Token de la organización. Se eliminan los espacios y saltos de línea de los extremos. |
| `BaseUrl` | `https://api.analytics.infini.es` | URL base de la API. |
| `Timeout` | 10 segundos | Tiempo máximo de cada llamada. Al agotarse, la llamada se registra en el log y devuelve `null`. `Timeout.InfiniteTimeSpan` lo desactiva. |
| `Logger` | Salida de error estándar (`Console.Error`) | Recibe los mensajes del SDK (nivel `Warning` o `Error`, mensaje y excepción si la hay). |

### Logging

Por defecto, los fallos se escriben en la salida de error estándar con el prefijo
`[InfiniAnalytics]`. En un robot esa salida no suele verse, así que conviene redirigirla con
`Logger`. Nunca contiene el token. Si el propio logger lanza una excepción, se ignora.

Para usar `Microsoft.Extensions.Logging`:

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

Si no se indica otro, el SDK usa un `HttpClient` interno compartido por todo el proceso, así que
crear muchos clientes (por ejemplo, uno en cada "Invoke Code") no agota los sockets y no hace
falta liberar nada.

También puedes pasar tu propio `HttpClient`, por ejemplo uno gestionado por `IHttpClientFactory`.
El SDK no lo libera y no usa su `BaseAddress` (la URL se toma de `BaseUrl`):

```csharp
builder.Services.AddHttpClient<InfiniAnalyticsClient>((http, services) =>
    new InfiniAnalyticsClient(http, new InfiniAnalyticsClientOptions
    {
        Token = builder.Configuration["InfiniAnalytics:Token"]!,
    }));
```

### Límites de longitud

La API rechaza (con un error 422) los textos que superan estos límites. Para no perder el evento,
el SDK los recorta antes de enviarlos y registra un warning en el log:

| Campo | Máximo |
|---|---|
| `description` | 2000 caracteres |
| `error_id` | 255 caracteres |
| `error_description` | 5000 caracteres |

### Timeout y cancelación

Todos los métodos asíncronos aceptan un `CancellationToken` opcional:

- Si se agota el `Timeout` del SDK, la llamada se registra en el log y devuelve `null`, como
  cualquier otro fallo de comunicación.
- Si eres tú quien cancela el `CancellationToken`, el método lanza `OperationCanceledException`,
  como es habitual en .NET: la cancelación es una decisión tuya, no un fallo de comunicación.

## Problemas frecuentes

- **No veo los errores del SDK en el log del robot.** Por defecto se escriben en `Console.Error`.
  Configura `Logger` para enviarlos al log de tu plataforma.
- **Error de TLS o de conexión segura en .NET Framework antiguo.** La API requiere TLS 1.2. Las
  aplicaciones compiladas para .NET Framework 4.7 o superior lo usan por defecto; en versiones
  anteriores puede ser necesario activarlo en la aplicación que aloja el robot. El SDK no cambia
  esta configuración porque afecta a todo el proceso.
- **`FileNotFoundException` o `FileLoadException` de `System.Text.Json` u otra DLL en .NET
  Framework.** Faltan dependencias junto a `InfiniAnalytics.dll`, o la aplicación que aloja el
  robot carga otra versión. Copia todas las dependencias indicadas en la sección
  [Blue Prism](#blue-prism) y, si hace falta, añade las redirecciones de ensamblado
  (`bindingRedirect`) en el fichero `.config` de la aplicación.
- **La API responde 401.** El token no es válido, ha caducado o no tiene acceso a esa
  automatización.
- **La API responde 400.** La automatización no existe o el tipo de evento no es válido.
- **La API responde 422.** El cuerpo no es válido, por ejemplo porque `automationId` no es un UUID.

## Desarrollo

Requiere el SDK de .NET 10. Los tests de .NET Framework 4.8 solo se ejecutan en Windows.

```bash
dotnet build
dotnet test
dotnet pack src/InfiniAnalytics -c Release -o artifacts
```

Ningún test llama a la API real. Para una prueba de integración real (no se ejecuta en CI),
copia `.env.example` como `.env` en la raíz del repositorio, rellena el token de organización y
el `automationId` de una automatización de pruebas, y ejecuta:

```bash
dotnet run --project samples/SmokeTest
```

`.env` está en `.gitignore` y nunca se sube al repositorio. Las variables de entorno con el mismo
nombre, si existen, tienen prioridad sobre el fichero.

## Licencia

MIT. Consulta el fichero [LICENSE](LICENSE).
