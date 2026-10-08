# InfiniAnalytics en Power Automate Desktop

[English](power-automate-desktop.md) | **Español**

Esta guía explica cómo registrar en InfiniAnalytics las ejecuciones de un flujo de Power
Automate Desktop (PAD) usando el SDK de .NET desde la acción "Ejecutar script de .NET".

Es solo para flujos de escritorio. Los flujos de nube de Power Automate no pueden ejecutar
código .NET.

## Requisitos

- Power Automate Desktop con la acción "Ejecutar script de .NET" (grupo "Scripting").
- El token de tu organización, que encontrarás en el panel de InfiniAnalytics.
- El identificador (UUID) de la automatización en InfiniAnalytics.
- El fichero `InfiniAnalytics-<versión>-dotnet-framework-dlls.zip`, que se descarga de la página
  de [Releases](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/releases) del
  repositorio.

## Cómo queda el flujo

Al terminar la guía, el flujo tendrá esta forma:

```
Main
  Establecer variable       IA_Token, IA_AutomationId, IA_Tipo, IA_Descripcion,
                            IA_ErrorId, IA_ErrorDetalle            (apartado 3)
  Ejecutar script de .NET   inicio: registra START                 (apartado 4)

  ... acciones de tu proceso ...
  Establecer variable       IA_Tipo = EVENT, IA_Descripcion = ...  (apartado 6)
  Ejecutar subflujo         IA_Registrar

  En error del bloque                                              (apartado 7)
    ... acciones de tu proceso que pueden fallar ...
  Fin

  Establecer variable       IA_Tipo = END, IA_Descripcion = ...    (apartado 6)
  Ejecutar subflujo         IA_Registrar

Subflujo IA_Registrar       registra EVENT, WARNING, ERROR o END   (apartado 5)
Subflujo IA_Error           registra el error del bloque           (apartado 7)
```

Solo hay dos códigos C#, que se copian tal cual: el de inicio (en Main) y el de registro (en el
subflujo `IA_Registrar`). El resto del flujo son acciones normales de PAD.

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

## 2. Tres ideas antes de empezar

**Los parámetros de script conectan el código C# con el flujo.** En la tabla "Parámetros de
script" de la acción "Ejecutar script de .NET", cada fila es un dato que entra o sale del código:

| Columna | Qué se escribe |
|---|---|
| Nombre del parámetro | El nombre que usa el código. Se copia tal cual de esta guía, no se inventa. |
| Dirección | `En` si el dato entra en el código, `Salida` si el código lo devuelve. |
| Valor de entrada | Solo en las filas `En`: de dónde sale el dato. Una variable del flujo con `%` (por ejemplo `%IA_Token%`) o un texto fijo. |
| Variable de salida | Solo en las filas `Salida`: el nombre de la variable donde se guarda el resultado, sin `%`. |

**Las variables de entrada se crean antes; las de salida, no.** Una variable que aparece en
"Valor de entrada" tiene que existir cuando se ejecuta la acción, así que se crea antes con
"Establecer variable". Una variable que aparece en "Variable de salida" la crea la propia acción
al terminar, igual que "Mostrar mensaje" crea `ButtonPressed`.

**Las variables son comunes a todo el flujo.** Una variable creada en Main se puede usar en
cualquier subflujo. Para cambiar su valor, se añade otra acción "Establecer variable" con el
mismo nombre: no se crea una variable nueva, se sustituye el valor.

## 3. Crear las variables

Al principio de Main, añade una acción "Establecer variable" por cada fila:

| Variable | Valor |
|---|---|
| `IA_Token` | Tu token |
| `IA_AutomationId` | El identificador de la automatización |
| `IA_Tipo` | `%''%` |
| `IA_Descripcion` | `%''%` |
| `IA_ErrorId` | `%''%` |
| `IA_ErrorDetalle` | `%''%` |

`%''%` es un texto vacío: esas variables tendrán valor más adelante. En el panel de variables,
marca `IA_Token` como sensible para que no aparezca en los registros del flujo.

## 4. Registrar el inicio (Main)

Debajo de las variables, añade la acción "Ejecutar script de .NET":

| Campo | Valor |
|---|---|
| Idioma | `C#` |
| Importaciones de scripts .NET | `System` y `InfiniAnalytics`, **uno por línea** (ver abajo) |
| Referencias que se cargarán | La carpeta de las DLL, por ejemplo `C:\InfiniAnalytics\dlls` |

El campo "Importaciones de scripts .NET" es un cuadro de texto. Escribe exactamente esto:

```
System
InfiniAnalytics
```

**Parámetros de script** (pulsa "Editar"), exactamente estas 4 filas, todas de tipo `Cadena`:

| Nombre del parámetro | Dirección | Valor de entrada | Variable de salida |
|---|---|---|---|
| `token` | En | `%IA_Token%` | |
| `automationId` | En | `%IA_AutomationId%` | |
| `descripcion` | En | `Inicio del proceso` | |
| `executionId` | Salida | | `IA_ExecutionId` |

**Código .NET para ejecutar:**

```csharp
var client = new InfiniAnalyticsClient(token);
var execution = client.StartExecution(automationId, null, descripcion);
executionId = execution.ExecutionId;
```

Esta acción registra el `START` y guarda en `%IA_ExecutionId%` el identificador de la
ejecución. El subflujo del apartado siguiente lo usa para que todos los eventos aparezcan juntos
en la misma ejecución del panel.

## 5. Subflujo para registrar eventos (`IA_Registrar`)

Crea un subflujo llamado `IA_Registrar` con una única acción "Ejecutar script de .NET". Idioma,
importaciones y referencias, igual que en el apartado 4.

**Parámetros de script**, exactamente estas 8 filas, todas de tipo `Cadena`:

| Nombre del parámetro | Dirección | Valor de entrada | Variable de salida |
|---|---|---|---|
| `token` | En | `%IA_Token%` | |
| `automationId` | En | `%IA_AutomationId%` | |
| `executionId` | En | `%IA_ExecutionId%` | |
| `tipo` | En | `%IA_Tipo%` | |
| `descripcion` | En | `%IA_Descripcion%` | |
| `errorId` | En | `%IA_ErrorId%` | |
| `errorDetalle` | En | `%IA_ErrorDetalle%` | |
| `resultado` | Salida | | `IA_Resultado` |

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

El código registra lo que diga `%IA_Tipo%`:

| Valor de `IA_Tipo` | Qué registra |
|---|---|
| `EVENT` | Un hito del proceso |
| `WARNING` | Un aviso que requiere atención |
| `ERROR` | Un error |
| `END` | El fin de la ejecución |

Al terminar, `%IA_Resultado%` vale `OK` si la API ha guardado el evento, o `NO REGISTRADO` si no
se ha podido registrar (fallo de red, timeout, token no válido o un `IA_Tipo` mal escrito). No es
necesario comprobarlo: el SDK nunca detiene el flujo por un fallo de comunicación.

## 6. Registrar eventos y el fin (Main)

Para registrar algo en cualquier punto de Main, añade estas tres acciones:

1. "Establecer variable" `IA_Tipo` con el tipo, por ejemplo `EVENT`. Se escribe tal cual, sin
   comillas ni `%`.
2. "Establecer variable" `IA_Descripcion` con el texto, por ejemplo `Facturas descargadas`.
3. "Ejecutar subflujo" `IA_Registrar`.

Al final del flujo, haz lo mismo con `IA_Tipo` = `END` y una descripción como
`Fin del proceso`. Registra siempre el `END`: sin él, la ejecución se queda abierta en el panel.

## 7. Registrar los errores del flujo

Si una acción de PAD falla, el flujo se detiene. Para registrar el error en InfiniAnalytics y
terminar la ejecución correctamente:

**1. Crea el subflujo `IA_Error`** con estas acciones:

| # | Acción | Configuración |
|---|---|---|
| 1 | Obtener último error | Guarda el error en `LastError` |
| 2 | Establecer variable | `IA_Tipo` = `ERROR` |
| 3 | Establecer variable | `IA_Descripcion` = `Fallo en el proceso` |
| 4 | Establecer variable | `IA_ErrorDetalle` = `%LastError.Message%` |
| 5 | Ejecutar subflujo | `IA_Registrar` |

**2. En Main, mete las acciones del proceso en un bloque de error.** Añade la acción "Al
producirse un error en el bloque" (en el flujo aparece como "En error del bloque") y coloca
dentro, entre ella y su "Fin", las acciones que pueden fallar. Las acciones del `END` van
**después** del "Fin" del bloque, nunca dentro.

**3. Configura el bloque.** Abre la acción "En error del bloque":

- Añade una regla nueva para **ejecutar el subflujo** `IA_Error`.
- Activa **continuar la ejecución del flujo** y elige continuar desde el **final del bloque**.

Si no activas la opción de continuar, el flujo se detiene después de `IA_Error`, el `END` no
se registra y la ejecución aparece en el panel con estado "Error" en lugar de "Finalizada con
error".

## 8. Comprobar el resultado

Ejecuta el flujo y abre la ejecución en el panel de InfiniAnalytics. Su "ID Ejecución" es el
valor de `%IA_ExecutionId%`. Según los eventos registrados, el estado será:

| Eventos | Estado |
|---|---|
| `START` (y eventos intermedios), sin `END` | Iniciada |
| `START` ... `END` | Finalizada |
| `START` ... `ERROR` ... `END` | Finalizada con error |
| `START` ... `ERROR`, sin `END` | Error |

## Si el flujo se ejecuta en paralelo

El SDK genera el identificador de ejecución con la fecha y hora actuales en UTC, por ejemplo
`2026-10-08T13:15:03.649Z`. Si el mismo flujo puede ejecutarse a la vez en varias máquinas, dos
ejecuciones que empiecen en el mismo milisegundo tendrían el mismo identificador. Para evitarlo,
en el código de inicio pasa un identificador único en lugar de `null`:

```csharp
var execution = client.StartExecution(automationId, Guid.NewGuid().ToString(), descripcion);
```

## Problemas frecuentes

- **"Se ha definido la variable 'X' pero no se ha inicializado".** El flujo usa la variable `X`
  antes de que exista. Si es una variable de entrada, créala antes con "Establecer variable"
  (apartado 3). Si es `IA_ExecutionId`, comprueba que la acción de inicio está antes y que su
  "Variable de salida" se llama exactamente así.
- **"El nombre 'X' no existe en el contexto actual"** (error de compilación). El código usa un
  parámetro `X` que no está en la tabla de parámetros, o está escrito de otra forma. Compara la
  tabla con la de esta guía, mayúsculas incluidas. También aparece si falta `InfiniAnalytics` en
  las importaciones (recuerda: un namespace por línea).
- **"El valor no es único"** en la tabla de parámetros. Hay dos filas con el mismo nombre de
  parámetro. Borra una.
- **No puedo escribir en "Valor de entrada" o en "Variable de salida".** "Valor de entrada" solo
  se activa con la dirección `En`, y "Variable de salida" con la dirección `Salida`.
- **La acción falla con un parámetro de salida.** Todos los parámetros `Salida` tienen que
  recibir un valor en el código. Si la tabla tiene una fila `Salida` que el código no usa,
  bórrala.
- **`FileNotFoundException` o `FileLoadException` de alguna DLL.** Falta alguna de las 10 DLL en
  la carpeta, o la ruta de "Referencias que se cargarán" no es correcta.
- **Error del tipo "An attempt was made to load an assembly from a network location".**
  Windows ha bloqueado las DLL por venir de Internet. Desbloquea el `.zip` (apartado 1) y vuelve
  a descomprimirlo.
- **La ejecución aparece con estado "Error" y sin `END`.** El bloque de error no está
  configurado para continuar, o las acciones del `END` están dentro del bloque (apartado 7).
- **`%IA_Resultado%` vale `NO REGISTRADO`.** Comprueba el token, el identificador de la
  automatización, que `%IA_Tipo%` es uno de los cuatro valores válidos y que el equipo tiene
  acceso a Internet. Cada llamada espera como máximo 10 segundos a la API.
