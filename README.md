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

## Auth-Limits und Instagram-Webhook

Login ist auf 10 Anfragen pro Minute und Passwort-Reset auf 5 Anfragen pro
5 Minuten begrenzt, jeweils pro Client-IP und API-Prozess. Bei Überschreitung
antwortet die API mit HTTP 429. Hinter einem Reverse Proxy muss dessen Adresse
gezielt als vertrauenswürdig konfiguriert sein, damit die tatsächliche Client-IP
verwendet wird. Details und Betriebsgrenzen: [Security-Betrieb](docs/security-operation.md).

Webhook-POSTs benötigen das Meta-App-Secret als `Instagram:AppSecret`
(Umgebungsvariable `Instagram__AppSecret`). Das ist das App-Secret aus der
Meta-App, **nicht** das Webhook-Verify-Token oder der Graph-Access-Token.
Werte nur über die Secret-Verwaltung der jeweiligen Laufzeit bereitstellen.
Die API validiert `X-Hub-Signature-256` über die unveränderten Request-Bytes.
Ohne App-Secret antwortet der POST-Endpunkt mit HTTP 503, bei ungültiger oder
fehlender Signatur mit HTTP 401. Die maximale Payload beträgt 1 MiB (HTTP 413).
Die GET-Verifizierung verwendet weiterhin `Instagram:WebhookVerifyToken`.
