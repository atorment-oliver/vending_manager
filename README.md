# Vending Manager

Aplicación de escritorio Windows con C# y .NET 8 para consultar registros de varias máquinas expendedoras.

## Funciones

- Configuración de varias máquinas: nombre, ubicación, ruta al archivo de log y credenciales independientes.
- Acceso a cada vending con usuario y contraseña. Se guarda un hash PBKDF2 con sal; la contraseña no se conserva en texto.
- Importación del log en una base local SQLite.
- Reportes por día y por rango: créditos registrados, exitosos, con error, pendientes y registros antiguos sin resultado.
- Exportación del resumen del rango a CSV.
- La importación es repetible: el ID de transacción actualiza la misma fila cuando pasa de PENDIENTE a OK o ERROR.
- La acción **Importar y limpiar día** consolida primero todas las filas válidas del log en SQLite y luego retira del `.log` las filas procesadas de la fecha elegida. Las filas `PENDIENTE` quedan en el log.
- Antes de depurar se guarda una copia íntegra del `.log` en `%LOCALAPPDATA%\\VendingManager\\LogBackups`. El archivo se vuelve a verificar antes de modificarlo para evitar perder datos si cambió durante la importación.
- La eliminación se sincroniza mediante Google Drive y también se reflejará en el equipo que comparte ese archivo. Ejecuta la depuración después de confirmar que el archivo terminó de sincronizarse.

## Formato soportado

Formato actual de la API, una fila por transacción:

    transactionId|fecha|banca|usuario|monto|estado|fechaActualización|detalle

Estados: PENDIENTE, OK, ERROR.

También lee el formato antiguo fecha|banca|usuario|monto. Como esas líneas no guardan resultado, se muestran en **Sin estado** y no se cuentan como éxito o error.

La aplicación maneja filas antiguas donde una versión anterior pudo concatenar el registro pendiente a la línea previa.

## Abrir y ejecutar

Requiere Windows 10/11 y Visual Studio 2022 con la carga de trabajo **Desarrollo de escritorio de .NET**.

1. Clona el repositorio y abre VendingManager/VendingManager.csproj en Visual Studio.
2. Restaura NuGet y ejecuta con F5.
3. Pulsa **Configurar vending** y agrega el nombre (por ejemplo, Vending1), ubicación, ruta al log, usuario y contraseña.
4. Selecciona la vending e inicia sesión con sus credenciales.
5. Elige una fecha o un rango y pulsa **Consultar**. Para consolidar todo el log y limpiar únicamente un día, elige esa fecha en **Desde** y pulsa **Importar y limpiar día**.

La configuración y la base consolidada se guardan en:

    %LOCALAPPDATA%\VendingManager\vending-config.json
    %LOCALAPPDATA%\VendingManager\consolidated.db

La base y los respaldos se mantienen locales para cada usuario de Windows.

## Publicar para instalar

En Visual Studio, clic derecho en el proyecto → **Publicar** → **Carpeta** → destino win-x64. Selecciona **Independiente / Self-contained** para no requerir .NET Runtime instalado. La carpeta de publicación se copia al equipo donde se instalará Vending Manager.

También puede publicarse desde una terminal con el SDK .NET 8:

    dotnet publish .\VendingManager\VendingManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

## Notas sobre sincronización

Google Drive puede estar escribiendo o reemplazando el archivo mientras se consulta. El lector abre el archivo permitiendo lectura concurrente y vuelve a intentar ante bloqueos breves. Si la sincronización aún no terminó, pulsa **Consultar** de nuevo cuando Drive indique que está actualizado.

Al leer cada vez el log completo y hacer upsert por ID, el consolidado puede reconstruirse aunque se borre la base local. En logs muy grandes se puede cambiar luego a importación incremental con detección de reemplazos de Google Drive.
