MI REPRODUCTOR - .NET MAUI (Android)

REQUISITOS
- Visual Studio 2026 con la carga de trabajo ".NET MAUI" y Android SDK, o
  .NET 10 SDK con las cargas de trabajo MAUI/Android instaladas.
- Este proyecto está configurado para net10.0-android y es solo para Android.
- Conexión a Internet la primera vez para restaurar paquetes del SDK.

ABRIR Y COMPILAR
1. Extrae el ZIP.
2. Abre MiReproductorMAUI.csproj en Visual Studio.
3. Espera a que se restauren las dependencias.
4. Selecciona un emulador Android o conecta el teléfono con depuración USB.
5. Ejecuta para probar.

GENERAR APK
Desde una terminal en la carpeta donde está el .csproj:

    dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormats=apk

El APK normalmente queda dentro de:
    bin/Release/net10.0-android/publish/

NOTAS
- La interfaz está hecha con .NET MAUI.
- La reproducción usa Android.Media.MediaPlayer mediante un servicio nativo de Android.
- Para mantener el ejemplo sencillo y evitar permisos generales de almacenamiento,
  el usuario selecciona archivos de audio mediante el selector del sistema Android.
- En esta versión se seleccionan varios archivos, no una carpeta completa.
- La reproducción en segundo plano con notificación/controles de pantalla bloqueada
  todavía no está implementada.
- Si tu instalación de Visual Studio no tiene .NET 10 MAUI, instala esa carga de trabajo
  o adapta el TargetFramework a la versión MAUI que tengas instalada.
