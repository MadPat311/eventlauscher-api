# Eventlauscher API

.NET Core Web API für das Eventlauscher-Projekt.  
Enthält Entity Framework Core + PostgreSQL + REST-API.

## Setup

1. `dotnet restore`
2. `dotnet ef database update`
3. `dotnet run`

## RUN!

To start the server localy on for example on port 5000 just use in Terminal

```bash
dotnet run --urls "http://0.0.0.0:5000"
```

## Umgebung

- ASP.NET Core
- PostgreSQL
- Entity Framework Core

## Optionale Event-Quellen

Der API-Vertrag, Beispiele für Flutter und die Rollout-/Migrationsschritte sind in
[docs/event-source-links.md](docs/event-source-links.md) beschrieben.

Backend und gezielte Regressionstests prüfen:

```bash
dotnet build EventLauscherApi.sln
dotnet test EventLauscherApi.sln --no-build
```

## Tests

Das xUnit-Testprojekt deckt Controller, Services und die HTTP-Pipeline ab.
Es läuft ohne PostgreSQL, SMTP-Server oder Instagram-Zugangsdaten:

```bash
dotnet test EventLauscherApi.sln --configuration Release
```

Testbereiche, Coverage, GitHub-Automatisierung und die Grenzen der isolierten Tests
sind in [Tests/README.md](Tests/README.md) dokumentiert.
