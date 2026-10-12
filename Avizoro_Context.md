# Contexto del proyecto: IntelligenceKit

SDK/librería open source para .NET llamado IntelligenceKit.

**Objetivo:** Plataforma de observabilidad para aplicaciones .NET/MAUI, conceptualmente similar a Sentry, App Center y Firebase Crashlytics, pero orientada al ecosistema .NET. Backend auto-hospedable.

> Documento de intención y roadmap. Para comandos y detalle de arquitectura del estado actual, ver `CLAUDE.md`.

## Solución actual

`IntelligenceKit.slnx` (formato de solución XML nuevo, no `.sln`). Todo apunta a **.NET 10**.

Proyectos:

- **IntelligenceKit.Core** — librería `net10.0`, independiente de MAUI. Modelo de dominio, enums, capa de servicios por interfaces.
- **IntelligenceKit.Maui** — SDK de plataforma (`net10.0-android;net10.0-ios`). Captura de crashes, contexto, almacenamiento offline.
- **IntelligenceKit.Server** — backend minimal-API de ingesta/consulta.
- **IntelligenceKit.Server.Contracts** — DTOs compartidos entre Server y Dashboard.
- **IntelligenceKit.Server.Data** — EF Core `DbContext` y entidades (`StoredEvent`, `StoredScreenshot`).
- **IntelligenceKit.Server.Migrations.{Sqlite,PostgreSql,SqlServer}** — una assembly de migraciones por proveedor.
- **IntelligenceKit.Dashboard** — SPA Blazor WebAssembly.
- **Sample.Maui** — app de demostración que consume el SDK.

## Arquitectura

Dirección de dependencias (nada depende de MAUI salvo el SDK de MAUI):

```
Sample.Maui -> IntelligenceKit.Maui -> IntelligenceKit.Core
                                            ^
IntelligenceKit.Server    -> Core, Server.Contracts, Server.Data, Server.Migrations.*
IntelligenceKit.Dashboard -> Server.Contracts
```

Flujo de datos:

```
Fuente de crash / log
 -> IntelligenceKitService.Enrich() (contexto app/device/runtime/scope)
 -> IEventStore (SQLite offline, durable)
 -> IEventUploader.FlushAsync() -> HttpClient -> POST /events
 -> IntelligenceKit.Server -> EF Core -> BD
 -> SignalR (/hubs/events) -> Dashboard en vivo
```

## Core

Permanece independiente de MAUI.

Contiene: Models, Enums, Interfaces, servicios base.

- `IntelligenceKitService` es el embudo único: todo evento pasa por `Enrich()` y luego **store-and-forward** (persistir primero, luego intentar subir).
- En crash fatal, `CaptureCrashAsync` solo persiste (sin flush); el siguiente arranque drena la cola.
- Captura de contexto siempre best-effort: nunca puede tumbar al evento real.

Interfaces principales: `IIntelligenceKit`, `IIntelligenceClient`, `IEventStore`, `IEventUploader`, `ICrashReporter`, `IDeviceContextProvider`, `IRuntimeContextProvider`, `ILastScreenProvider`, `IScreenshotStore`, `IBreadcrumbBuffer`.

## MAUI SDK

API pública mínima — una sola línea:

```csharp
builder
    .UseMauiApp<App>()
    .UseIntelligenceKit("http://{projectKey}@{host}:{port}/{projectId}");
```

`UseIntelligenceKit(dsn)` parsea el DSN, auto-detecta nombre/versión de la app y registra todos los servicios. `IntelligenceKitStartup : IMauiInitializeService` activa en tiempo de construcción: handlers de crash, tracking de navegación/breadcrumbs, captura proactiva de pantalla, y flush de la cola al recuperar conectividad. **Cero código en la app host.**

Captura de crash por plataforma en `Platforms/Android` y `Platforms/iOS`. Metadata vía `DeviceInfo.Current`.

## Server

Minimal-API en un solo `Program.cs`. Endpoints: `POST /events` (idempotente por Id, difunde por SignalR), `GET /events`, `GET /events/{id}`, `POST|GET /events/{id}/screenshot` (blob separado del JSON, tope 2 MB), `GET /projects`, `GET /stats/events-per-hour`. Hub SignalR en `/hubs/events`.

### Persistencia multi-proveedor

**SQLite (por defecto), PostgreSQL, SQL Server**, elegido en runtime por `Database:Provider`. Cada proveedor tiene su propia assembly de migraciones (las migraciones EF son específicas del proveedor). `Program.cs` aplica `db.Database.Migrate()` al arrancar. Un cambio de esquema implica regenerar la migración en los **tres** proyectos de migraciones.

## Estado actual

Comunicación MAUI -> Server funcionando y probada end-to-end (emulador Android vía `10.0.2.2:7099`).

Completado del roadmap original:

1. ✅ Crash reporting refactorizado con `ICrashReporter`.
2. ✅ Parser/reporter Android.
3. ✅ Soporte iOS (target `net10.0-ios`, `CrashReporter.iOS`).
4. ✅ Almacenamiento offline SQLite (`SqliteEventStore`, `SqliteScreenshotStore`).
5. ✅ Background uploader (`EventUploader`, flush en arranque y al reconectar).
6. ✅ Backend persistente (EF Core, multi-proveedor).
7. ✅ Dashboard Blazor (WASM + feed en vivo por SignalR).

Pendiente:

8. ⏳ **API keys y proyectos.** Hoy la config es un DSN donde `projectKey` es un identificador **público** de ruteo (viaja en el cliente), **no** un secreto — no hay autenticación. Meta: `builder.UseIntelligenceKit("ik_live_xxxxx")`.
9. ⏳ Publicar en NuGet (`dotnet add package IntelligenceKit.Maui`).

## Objetivo final

Instalación y uso mínimos para el consumidor:

```csharp
builder
    .UseMauiApp<App>()
    .UseIntelligenceKit("ik_live_xxxxx");
```

Automáticamente: captura crashes, obtiene metadata, envía eventos, soporta plataformas.

Prioridades de diseño: arquitectura limpia, extensibilidad, y experiencia simple para los consumidores del SDK.
