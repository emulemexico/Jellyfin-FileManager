# Jellyfin File Manager

Administrador de archivos integrado al Dashboard de Jellyfin que permite navegar, copiar, mover, renombrar, subir, descargar y eliminar archivos utilizando el filesystem visible para el proceso de Jellyfin.

## Características

- **Navegación completa**: Exploración de directorios y unidades visibles para el servidor.
- **Operaciones de archivos**: Crear carpetas, renombrar, copiar, mover y eliminar archivos y directorios.
- **Movimientos inteligentes entre discos/volúmenes**: Detecta transferencias entre volúmenes distintos y aplica automáticamente fallback de copia, verificación de integridad por tamaño y posterior eliminación segura del origen.
- **Subida y descarga**: Subida de uno o múltiples archivos (con límite configurable) y descarga directa desde el navegador.
- **Seguridad y elevación**: Todos los endpoints del plugin están protegidos y requieren una cuenta con permisos de administrador en Jellyfin (`RequiresElevation`).
- **Modos de acceso**:
  - **Full File System Access (predeterminado)**: Acceso a todo el filesystem visible para el proceso de Jellyfin.
  - **Modo restringido**: Configuración opcional de rutas raíz permitidas.
- **Soporte para enlaces simbólicos**: Navegación de symlinks y junctions con protecciones durante movimientos entre volúmenes.

## Compatibilidad

- **Jellyfin Server**: 10.11.x (Target ABI: `10.11.0.0`)
- **Framework de ejecución**: .NET 9 (`net9.0`)
- **Compilación**: Requiere .NET 9 SDK **únicamente para compilar** desde el código fuente. Para usuarios finales que instalen desde el catálogo o Release, Jellyfin ya provee el runtime necesario.

---

## Instalación desde Repositorio de Jellyfin (Recomendado)

Puedes instalar y recibir actualizaciones automáticas del plugin agregando el repositorio oficial a tu servidor Jellyfin:

1. Inicia sesión en tu servidor Jellyfin como **Administrador**.
2. Dirígete a **Dashboard** (Panel de control) ? **Plugins** ? pestaña **Repositories** (Repositorios).
3. Haz clic en el botón **+** (Add / Añadir) e introduce los siguientes datos:
   - **Nombre del repositorio**: `Jellyfin File Manager`
   - **URL del repositorio**:
     ```text
     https://raw.githubusercontent.com/emulemexico/Jellyfin-FileManager/main/manifest.json
     ```
4. Guarda los cambios.
5. Ve a la pestaña **Catalog** (Catálogo) en **Plugins**.
6. Busca **File Manager** en la categoría **General**, selecciónalo e instala la versión más reciente.
7. **Reinicia el servidor Jellyfin** para cargar el plugin.
8. Accede a **Dashboard** ? **File Manager** en la barra lateral de administración.

---

## Actualización del Plugin

- **Desde el Catálogo**: Cuando se publique una nueva versión en este repositorio, Jellyfin mostrará la notificación de actualización en **Dashboard** ? **Plugins**. Simplemente pulsa *Update* y reinicia Jellyfin.
- **Instalación Manual**: Reemplaza el archivo `Jellyfin.Plugin.FileManager.dll` en la carpeta de plugins y reinicia Jellyfin.

---

## Instalación Manual

1. Descarga el archivo `Jellyfin.Plugin.FileManager_10.11.0.2.zip` desde la sección [Releases](https://github.com/emulemexico/Jellyfin-FileManager/releases).
2. Detén el servicio de Jellyfin.
3. Extrae la DLL en la carpeta de plugins de Jellyfin:
   - **Linux / Docker**:
     ```text
     /config/plugins/File Manager/Jellyfin.Plugin.FileManager.dll
     ```
   - **Windows**:
     ```text
     C:\ProgramData\Jellyfin\Server\plugins\File Manager\Jellyfin.Plugin.FileManager.dll
     ```
4. Inicia Jellyfin.
5. Accede como administrador a **Dashboard** ? **File Manager**.

---

## Explicación de Full File System Access

El plugin implementa de manera predeterminada el modo **Acceso completo al sistema de archivos**:

- En **Windows**, el selector de raíces enumera automáticamente todas las unidades montadas y listas (`C:\`, `D:\`, etc.).
- En **Linux**, el selector de raíces ofrece `/` como punto de partida.
- El plugin opera con las credenciales y permisos del usuario/proceso bajo el cual se ejecuta Jellyfin.
- Si prefieres limitar la interfaz a carpetas específicas, desactiva la opción en la configuración del plugin e introduce las rutas autorizadas (una por línea).

### Advertencia sobre Permisos (Linux / Windows)

El plugin **no puede eludir** los permisos del sistema operativo anfitrión:
- **Lectura**: Requerida para listar directorios y descargar archivos.
- **Escritura**: Requerida para crear carpetas, subir archivos, renombrar y copiar.
- **Eliminación**: Requerida para borrar y completar movimientos entre diferentes discos.

Si Jellyfin se ejecuta bajo un usuario específico (por ejemplo `jellyfin:jellyfin` o `PUID=1000/PGID=1000`), asegúrate de que dicho usuario cuente con los permisos POSIX o ACL necesarios sobre las carpetas que deseas gestionar.

---

## Explicación y Configuración con Docker

Cuando Jellyfin se ejecuta en un contenedor Docker, el plugin **solo puede ver las rutas montadas dentro del contenedor**. No tiene acceso al filesystem del anfitrión fuera de los volúmenes montados.

Para gestionar tus discos y carpetas de medios desde File Manager, debes mapearlos en tu `docker-compose.yml`:

```yaml
services:
  jellyfin:
    image: jellyfin/jellyfin:10.11.11
    container_name: jellyfin
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/Mexico_City
    volumes:
      - /opt/jellyfin/config:/config
      - /opt/jellyfin/cache:/cache
      - /mnt/storage/movies:/media/movies
      - /mnt/storage/series:/media/series
      - /mnt/downloads:/media/downloads
    ports:
      - 8096:8096
    restart: unless-stopped
```

Con esta configuración, File Manager podrá realizar operaciones directamente entre tus rutas montadas, por ejemplo:
```text
/media/downloads/Completados/Pelicula.mkv  ==>  /media/movies/Pelicula (2026)/Pelicula.mkv
```

---

## Compilación desde Código Fuente

Para compilar el proyecto manualmente:

```bash
dotnet restore
dotnet build -c Release
```

El binario resultante se generará en:
```text
bin/Release/net9.0/Jellyfin.Plugin.FileManager.dll
```

Para generar el paquete distribuible con su respectivo checksum MD5:
- **Windows**:
  ```powershell
  .\build.ps1
  ```
- **Linux**:
  ```bash
  ./build.sh
  ```

---

## Licencia

Este proyecto se distribuye bajo la licencia **GNU General Public License v3.0** ([GPL-3.0](LICENSE)), compatible con el ecosistema de Jellyfin.