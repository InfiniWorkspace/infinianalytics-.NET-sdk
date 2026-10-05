# InfiniAnalytics en Power Automate Desktop

Esta guía explica cómo registrar las ejecuciones de un flujo de Power Automate Desktop en
InfiniAnalytics usando el SDK de .NET desde la acción "Ejecutar script de .NET".

Power Automate Desktop no instala paquetes NuGet, así que el SDK se carga desde una carpeta con
sus DLL.

Esta guía es solo para flujos de escritorio. Los flujos de nube de Power Automate no pueden
ejecutar código .NET.

## Requisitos

- Power Automate Desktop con la acción "Ejecutar script de .NET" (grupo "Scripting").
- El token de tu organización, que encontrarás en el panel de InfiniAnalytics.
- El identificador (UUID) de la automatización en InfiniAnalytics.
- El fichero `InfiniAnalytics-<versión>-dotnet-framework-dlls.zip`, que se descarga de la página
  de [Releases](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/releases) del
  repositorio.

## 1. Preparar la carpeta de DLL

1. Descarga el `.zip` y, antes de descomprimirlo, haz clic derecho sobre él, abre
   "Propiedades" y marca "Desbloquear" si aparece. Así Windows no bloquea las DLL por venir de
   Internet.
2. Descomprímelo en una carpeta fija del equipo donde se ejecuta el flujo, por ejemplo
   `C:\InfiniAnalytics\dlls`. Debe contener estas 10 DLL:
   - `InfiniAnalytics.dll`
   - `Microsoft.Bcl.AsyncInterfaces.dll`
   - `System.Buffers.dll`
   - `System.Memory.dll`
   - `System.Numerics.Vectors.dll`
   - `System.Runtime.CompilerServices.Unsafe.dll`
   - `System.Text.Encodings.Web.dll`
   - `System.Text.Json.dll`
   - `System.Threading.Tasks.Extensions.dll`
   - `System.ValueTuple.dll`

Si el flujo se ejecuta en varias máquinas, la carpeta tiene que existir en todas con la misma
ruta.

## 2. Configurar la acción "Ejecutar script de .NET"

Todas las acciones del SDK se configuran igual. Solo cambian los parámetros y el código.

| Campo | Valor |
|---|---|
| Idioma | `C#` |
| Importaciones de scripts .NET | `System` y `InfiniAnalytics`, **uno por línea** (ver abajo) |
| Referencias que se cargarán | La carpeta de las DLL, por ejemplo `C:\InfiniAnalytics\dlls` |
| Parámetros de script | Los de cada acción (ver los apartados siguientes) |
| Código .NET para ejecutar | El de cada acción (ver los apartados siguientes) |

El campo "Importaciones de scripts .NET" es un cuadro de texto, no una lista de variables.
Escribe exactamente esto:

```
System
InfiniAnalytics
```

En "Parámetros de script", pulsa "Editar" y añade una fila por parámetro:

- Los parámetros de **Entrada** reciben un valor en la columna "Valor de entrada". Esa columna
  solo se activa cuando la dirección es Entrada.
- Los parámetros de **Salida** devuelven un valor al flujo en la variable indicada en la columna
  "Variable de salida".
- El nombre del parámetro tiene que coincidir exactamente (mayúsculas incluidas) con el nombre
  que usa el código.

## 3. Registrar el inicio de la ejecución

Añade esta acción al principio del flujo.

**Parámetros de script:**

| Nombre del parámetro | Tipo | Dirección | Valor de entrada | Variable de salida |
|---|---|---|---|---|
| `token` | Cadena | Entrada | `%IA_Token%` | |
| `automationId` | Cadena | Entrada | `%IA_AutomationId%` | |
| `descripcion` | Cadena | Entrada | `Inicio del proceso` | |
| `executionId` | Cadena | Salida | | `%IA_ExecutionId%` |

**Código .NET para ejecutar:**

```csharp
var client = new InfiniAnalyticsClient(token);
var execution = client.StartExecution(automationId, null, descripcion);
executionId = execution.ExecutionId;
```

Guarda el token y el identificador de la automatización en las variables `%IA_Token%` y
`%IA_AutomationId%` al principio del flujo, con la acción "Establecer variable". Marca
`%IA_Token%` como sensible para que no aparezca en los registros del flujo.

`%IA_ExecutionId%` identifica la ejecución. Las acciones siguientes lo necesitan para asociar
sus eventos a esta ejecución.

### De dónde sale `%IA_ExecutionId%`

El identificador de ejecución no lo genera Power Automate Desktop ni la API de InfiniAnalytics:
lo genera el SDK dentro de la acción "Ejecutar script de .NET". El recorrido del dato es este:

1. El código llama a `client.StartExecution(automationId, null, descripcion)`. El segundo
   argumento es el identificador de ejecución. Al pasar `null`, el SDK genera uno nuevo con la
   fecha y hora actuales en UTC, por ejemplo `2026-10-05T14:38:14.640Z`.
2. El SDK envía ese identificador a la API junto con el evento `START` y lo guarda en
   `execution.ExecutionId`.
3. La línea `executionId = execution.ExecutionId;` lo copia en el parámetro de salida
   `executionId`.
4. Al terminar la acción, Power Automate Desktop copia el parámetro de salida en la variable
   indicada en "Variable de salida", `%IA_ExecutionId%`.
5. Las acciones del apartado 4 reciben `%IA_ExecutionId%` como parámetro de entrada y
   reconstruyen la ejecución con `new Execution(...)`, que no envía un nuevo `START`. Así todos
   los eventos llegan a la API con el mismo identificador y aparecen juntos en el panel.

```
Acción "inicio" (script .NET)
  StartExecution(automationId, null, ...)   el SDK genera "2026-10-05T14:38:14.640Z"
    -> envía START a la API con ese id
  executionId = execution.ExecutionId        parámetro de salida
Power Automate Desktop
  parámetro de salida -> %IA_ExecutionId%
Acción "registrar" (script .NET)
  %IA_ExecutionId% -> parámetro de entrada executionId
  new Execution(..., executionId).Event(...) envía el evento con el mismo id
```

Las variables de un script .NET no sobreviven de una acción a otra: lo único que pasa de una
acción a la siguiente son los parámetros de salida guardados en variables del flujo. Por eso el
identificador tiene que viajar en `%IA_ExecutionId%`.

Si el mismo flujo puede ejecutarse a la vez en varias máquinas, dos ejecuciones que empiecen en
el mismo milisegundo tendrían el mismo identificador. Para evitarlo, pasa tu propio
identificador único en lugar de `null`, por ejemplo `Guid.NewGuid().ToString()`:

```csharp
var execution = client.StartExecution(automationId, Guid.NewGuid().ToString(), descripcion);
```

## 4. Registrar eventos, warnings, errores y el fin

Usa esta misma acción para cualquier evento posterior al inicio. El tipo de evento se indica en
el parámetro `tipo`.

**Parámetros de script:**

| Nombre del parámetro | Tipo | Dirección | Valor de entrada | Variable de salida |
|---|---|---|---|---|
| `token` | Cadena | Entrada | `%IA_Token%` | |
| `automationId` | Cadena | Entrada | `%IA_AutomationId%` | |
| `executionId` | Cadena | Entrada | `%IA_ExecutionId%` | |
| `tipo` | Cadena | Entrada | `EVENT`, `WARNING`, `ERROR` o `END` | |
| `descripcion` | Cadena | Entrada | Texto del evento | |
| `errorId` | Cadena | Entrada | Código de error (solo para `ERROR`, puede ir vacío) | |
| `errorDetalle` | Cadena | Entrada | Detalle del error (solo para `ERROR`, puede ir vacío) | |
| `resultado` | Cadena | Salida | | `%IA_Resultado%` |

**Código .NET para ejecutar:**

```csharp
var execution = new Execution(new InfiniAnalyticsClient(token), automationId, executionId);
object registrado = null;
switch (tipo)
{
    case "EVENT":
        registrado = execution.Event(descripcion);
        break;
    case "WARNING":
        registrado = execution.Warning(descripcion);
        break;
    case "ERROR":
        registrado = execution.Error(
            descripcion,
            null,
            string.IsNullOrEmpty(errorId) ? null : errorId,
            string.IsNullOrEmpty(errorDetalle) ? null : errorDetalle);
        break;
    case "END":
        registrado = execution.End(descripcion);
        break;
}
resultado = registrado != null ? "OK" : "NO REGISTRADO";
```

`%IA_Resultado%` vale `OK` si la API ha guardado el evento, o `NO REGISTRADO` si no se ha podido
registrar (fallo de red, timeout, token no válido o un `tipo` mal escrito). No es necesario
comprobarlo: el SDK nunca detiene el flujo por un fallo de comunicación.

Para no repetir la configuración, crea un subflujo (por ejemplo `IA_Registrar`) con esta única
acción, usando variables como `%IA_Tipo%` y `%IA_Descripcion%` en los valores de entrada. En
cada punto del flujo, establece esas variables y llama al subflujo con "Ejecutar subflujo".

## 5. Registrar los errores del flujo

Para notificar a InfiniAnalytics los errores que se produzcan en el flujo:

1. Agrupa las acciones del proceso en un bloque "Al producirse un error en el bloque".
2. En la gestión de errores del bloque, añade la acción "Obtener último error" para guardar el
   error en `%LastError%`.
3. Añade la acción del apartado 4 con `tipo` = `ERROR`, una `descripcion` como
   `Fallo en el proceso` y `errorDetalle` = `%LastError.Message%`.
4. Al final del flujo, registra siempre el `END`. Si antes se registró un `ERROR`, la ejecución
   queda como "Finalizada con error" en el panel.

## 6. Comprobar el resultado

Ejecuta el flujo y abre la sección "Ejecuciones" del panel de InfiniAnalytics. Debe aparecer una
ejecución con el identificador de `%IA_ExecutionId%` y todos los eventos registrados. Con un
`END` sin errores previos, el estado es "Finalizada". Si hubo un `ERROR`, es "Finalizada con
error".

## Problemas frecuentes

- **"Se ha definido la variable 'X' pero no se ha inicializado".** Ninguna acción anterior
  produce esa variable. Comprueba que la "Variable de salida" del parámetro tiene exactamente
  ese nombre y que la acción "Ejecutar script de .NET" está antes en el flujo.
- **No puedo escribir en "Valor de entrada".** La dirección del parámetro es Salida. Cámbiala a
  Entrada.
- **Error de compilación del tipo "The name 'X' does not exist in the current context".** El
  nombre de un parámetro no coincide con el del código, o falta `InfiniAnalytics` en las
  importaciones (recuerda: un namespace por línea).
- **`FileNotFoundException` o `FileLoadException` de alguna DLL.** Falta alguna de las 10 DLL en
  la carpeta de referencias, o la ruta de "Referencias que se cargarán" no es correcta.
- **Error del tipo "An attempt was made to load an assembly from a network location".**
  Windows ha bloqueado las DLL por venir de Internet. Desbloquea el `.zip` (apartado 1) y vuelve
  a descomprimirlo.
- **El resultado es `NO REGISTRADO`.** Comprueba el token, el identificador de la automatización
  y que el equipo tiene acceso a Internet. Cada llamada espera como máximo 10 segundos a la API.
