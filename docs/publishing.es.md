# Publicar una versión del SDK de .NET

[English](publishing.md) | **Español**

Esta guía explica cómo publicar una nueva versión de `InfiniAnalytics.Sdk` en
[nuget.org](https://www.nuget.org/packages/InfiniAnalytics.Sdk) y en GitHub Releases, y qué
configuración hay detrás. Cualquier persona del equipo con los permisos indicados puede hacerlo
sin depender de nadie.

## Resumen rápido

```bash
git checkout main
git pull
git tag v0.2.0
git push origin v0.2.0
```

El workflow `.github/workflows/ci.yml` se encarga del resto.

## Permisos necesarios

| Para... | Necesitas |
|---|---|
| Publicar una versión (push de una etiqueta) | Permiso de escritura en el repositorio `InfiniWorkspace/infinianalytics-.NET-sdk` |
| Cambiar secretos, variables o el workflow | Rol **Admin** en el repositorio |
| Gestionar el paquete en nuget.org (ocultar, marcar como obsoleto) | Ser miembro de la organización **InfiniAnalytics** en nuget.org |
| Gestionar los miembros de la organización o las políticas de Trusted Publishing | Rol **Administrator** en la organización **InfiniAnalytics** de nuget.org |

## Versionado

Seguimos [Semantic Versioning](https://semver.org/lang/es/): `MAYOR.MENOR.PARCHE`.

- **PARCHE** (de `0.1.0` a `0.1.1`): corrección de errores sin cambios en la API pública.
- **MENOR** (de `0.1.1` a `0.2.0`): funcionalidades nuevas compatibles con lo anterior.
- **MAYOR** (de `0.2.0` a `1.0.0`): cambios que rompen la compatibilidad.
- **Preliminares**: `v1.0.0-beta.1`, `v1.0.0-rc.1`. NuGet las marca como prerelease y no las
  instala por defecto.

Mientras la versión sea `0.x`, la API puede cambiar entre versiones menores.

La versión se toma **de la etiqueta** (`v0.2.0` pasa a ser `0.2.0`). No hace falta cambiar
`<Version>` en el `.csproj`: ese valor solo se usa al compilar en local.

## Antes de publicar

**Una versión publicada en nuget.org no se puede borrar ni sustituir.** Solo se puede ocultar
(unlist). Si algo sale mal, hay que publicar una versión nueva.

Comprueba que:

- `main` está actualizado y el CI del commit que vas a etiquetar está en verde.
- Si hay cambios que rompen la compatibilidad, has subido la versión MAYOR (o la MENOR mientras
  estemos en `0.x`).
- Si hay cambios que afectan a Power Automate Desktop, has probado la
  [guía de Power Automate Desktop](power-automate-desktop.es.md) en un flujo nuevo, con la carpeta
  de DLL generada con `dotnet build tools/DllBundle -c Release -o artifacts/dll-bundle`.
- Has probado el paquete en local (ver abajo).

### Probar el paquete en local

Crea el proyecto de prueba **fuera del repositorio**. Dentro, heredaría el `Directory.Build.props`
del repositorio (los `using` implícitos están desactivados, así que la plantilla de consola no
compila) y el `Directory.Packages.props` (`dotnet add package` intentaría modificarlo).

Desde la raíz del repositorio:

```bash
dotnet pack src/InfiniAnalytics -c Release -o ../nupkg-test -p:Version=0.2.0
cd ..
dotnet new console -o PruebaSdk
cd PruebaSdk
dotnet add package InfiniAnalytics.Sdk --source ../nupkg-test --version 0.2.0
dotnet build
```

NuGet guarda una copia de cada paquete que instala. Si repites la prueba con el mismo número de
versión, usa esa copia en lugar del paquete nuevo: borra la carpeta
`%USERPROFILE%\.nuget\packages\infinianalytics.sdk\0.2.0` (`~/.nuget/packages/...` en Linux y
macOS) antes de volver a probar.

## Publicar

1. Crea la etiqueta sobre el commit de `main` que quieres publicar:
   ```bash
   git checkout main
   git pull
   git tag v0.2.0
   git push origin v0.2.0
   ```
2. Sigue la ejecución en la pestaña **Actions** del repositorio.

### Qué hace el workflow

Al recibir una etiqueta `v*`, `ci.yml` ejecuta estos jobs por orden:

| Job | Qué hace |
|---|---|
| Build and test | Compila y ejecuta los tests en Windows y Linux. |
| Pack | Genera el `.nupkg`, el `.snupkg` y el zip de DLL con la versión de la etiqueta. |
| GitHub release | Crea la Release de GitHub con esos ficheros adjuntos. Las notas de la Release se generan a partir de los commits. |
| Publish to NuGet | Obtiene una API key temporal de nuget.org mediante **Trusted Publishing** y publica el paquete y sus símbolos. Solo se ejecuta si la variable `NUGET_PUBLISH_ENABLED` vale `true`. |

## Después de publicar

- nuget.org valida el paquete antes de publicarlo. Suele tardar entre 15 minutos y una hora, y la
  búsqueda puede tardar unos minutos más. El paquete de símbolos se suele validar antes. nuget.org
  envía un email a la cuenta que ha publicado cada uno.
- Comprueba la página del paquete en nuget.org: versión, README, icono y licencia.
- Comprueba la Release en GitHub y que el zip de DLL está adjunto. Revisa las notas generadas y
  edítalas en GitHub si hace falta.

## Si algo sale mal

| Problema | Qué hacer |
|---|---|
| Falla un job **anterior** a "GitHub release" (tests, compilación, empaquetado) | Corrige el problema en `main` y vuelve a crear la etiqueta sobre el nuevo commit: `git tag -d v0.2.0`, `git push origin :refs/tags/v0.2.0`, y después créala y súbela otra vez. |
| "GitHub release" funciona pero "Publish to NuGet" falla por configuración (login, política, variable) | No toques la etiqueta. Corrige la configuración y pulsa "Re-run failed jobs" en la ejecución. |
| Hay que mover la etiqueta a otro commit después de crear la Release, pero no ha llegado nada a nuget.org | Borra la Release (`gh release delete v0.2.0`) y la etiqueta, y vuelve a crear la etiqueta. |
| La versión ya está en nuget.org pero tiene un error | No se puede reemplazar. Ocúltala en nuget.org (Manage package > Listing) y publica una versión de parche (`v0.2.1`). |
| Error de autenticación en el paso "NuGet login" | Revisa la sección [Trusted Publishing](#trusted-publishing). La política puede estar inactiva, o `NUGET_USER` puede no coincidir con el usuario propietario de la política. |
| El job "Publish to NuGet" no se ejecuta | Comprueba que la variable `NUGET_PUBLISH_ENABLED` vale `true`. |

## Configuración

### GitHub (Settings > Secrets and variables > Actions)

| Tipo | Nombre | Valor |
|---|---|---|
| Secreto | `NUGET_USER` | Usuario de nuget.org (nombre de perfil, no email) del propietario de la política de Trusted Publishing |
| Variable | `NUGET_PUBLISH_ENABLED` | `true` para publicar en nuget.org; cualquier otro valor lo desactiva |

No usamos API keys de larga duración.

### Trusted Publishing

En lugar de guardar una API key como secreto, en cada ejecución el workflow pide a nuget.org una
clave temporal. nuget.org solo la concede si el repositorio y el workflow coinciden con una
política configurada.

Política actual en nuget.org (menú de tu usuario > **Trusted Publishing**):

| Campo | Valor |
|---|---|
| Package Owner | `InfiniAnalytics` |
| Repository Owner | `InfiniWorkspace` |
| Repository | `infinianalytics-.NET-sdk` |
| Workflow File | `ci.yml` |
| Environment | (vacío) |

**La política pertenece al usuario que la creó.** Si ese usuario sale de la organización
InfiniAnalytics en nuget.org, la política deja de funcionar y las publicaciones fallan.

#### Recrear la política

Cualquier administrador de la organización puede hacerlo:

1. Inicia sesión en nuget.org, abre el menú de tu usuario > **Trusted Publishing** y crea una
   política con los valores de la tabla anterior.
2. En GitHub, cambia el secreto `NUGET_USER` por tu usuario de nuget.org.

Si se cambia el nombre del fichero del workflow o del repositorio, hay que actualizar también la
política o las publicaciones fallarán.

### Organización en nuget.org

- Organización: **InfiniAnalytics**.
- Debe haber siempre **al menos dos administradores**, para que el paquete y la política no
  dependan de una sola persona.
- Prefijo de ID reservado: `InfiniAnalytics.*`. Nadie fuera de la organización puede publicar
  paquetes con ese prefijo, y los nuestros aparecen con la marca de verificado en nuget.org. Se
  gestiona escribiendo a account@nuget.org.

## Distribución

- **nuget.org**: para proyectos .NET y UiPath (`dotnet add package InfiniAnalytics.Sdk`).
- **GitHub Releases**: el zip de DLL, para Power Automate Desktop y Blue Prism.
