# Tests

Das vorhandene Projekt `EventLauscherApi.Tests` verwendet xUnit und enthält
Controller-/Service-Tests sowie HTTP-Integrationstests mit `WebApplicationFactory`.
Voraussetzung ist das .NET-8-SDK. PostgreSQL, SMTP-Zugang und Instagram-Zugangsdaten
werden für diese Tests nicht benötigt.

## Ausführen

Im Repository-Verzeichnis:

```powershell
dotnet test EventLauscherApi.sln --configuration Release
```

`Release` ermöglicht einen eigenen Build, während lokal eventuell die
Debug-Version der API läuft. In Visual Studio sind die Tests auch im
Test-Explorer verfügbar.

Einzelne Bereiche lassen sich über den vollständigen Klassennamen auswählen:

```powershell
dotnet test EventLauscherApi.sln -c Release --filter FullyQualifiedName~AuthTests
dotnet test EventLauscherApi.sln -c Release --filter FullyQualifiedName~HttpApiTests
dotnet test EventLauscherApi.sln -c Release --filter FullyQualifiedName~Instagram
```

## Abgedeckte Abläufe

| Bereich | Prüfungen |
| --- | --- |
| Authentifizierung | Registrierung, Eingabevalidierung, doppelte Nutzer/E-Mails, Passwortregeln, Rollen, Login, Lockout, Nutzerprofil |
| Bestätigung und Passwort-Reset | HTML-/JSON-Bestätigung, ungültige und bereits verwendete Tokens, Reset-Links, geänderte Zugangsdaten |
| JWT | Signatur, Claims, Rollen, Gültigkeit, zufällige Refresh-Tokens, Rotation, Replay-Ablehnung und Logout |
| Events | Anlegen, Feldübernahme, eigene Uploads, Veröffentlichung je Rolle, öffentliche Sichtbarkeit, Duplikat-Korpus |
| Favoriten und öffentliche Profile | Idempotentes Speichern/Entfernen, Nutzertrennung, Reihenfolge, veröffentlichte Einträge, öffentliche Zähler |
| Moderation | Queue, Detailansicht, Bearbeitung, erste Review-Zuordnung, Publish/Unpublish/Reject, Sperren/Entsperren |
| Meldungen | Anonyme und authentifizierte Meldungen, Reporter aus echtem JWT, Validierung, Auflistung und Löschen |
| Medien | Multipart-Upload, Bytes und SHA-256, Duplikaterkennung, Download, MIME-Typ, Cache, Batch- und Hash-Abfragen |
| Teilen und Kalender | HTML-Escaping, Vorschau-Links/Bilder, ICS-Datumsformate, Standardzeit, Dauer, Text-Escaping und Dateiname |
| Instagram-Webhooks | Verifizierung, Inbox-Speicherung, Mention-Erkennung, Deduplizierung auch innerhalb eines Batches, Batch-Grenzen, Fehlerbehandlung |
| Instagram-Abruf | Graph-URL und Bearer-Header, HTTP-/Transportfehler, Antwortformate, Metadaten, Kommentare, Batch-Grenzen, Fehlerstatus |
| Hintergrunddienste | Aktivierung, Prozessorausführung, Fehlerprotokollierung und Beenden |
| SMTP | Nachrichteninhalt, Empfänger/Absender, STARTTLS, Anmeldung, Senden, Fehlerweitergabe und Freigabe des Clients |
| Seed | Migrationsaufruf, Rollen, bestätigter Admin und wiederholbares Seeding mit echten Identity-Stores |
| HTTP-Pipeline | Routing, JSON-/Form-Binding, echte JWT-Authentifizierung, 401/403, Rollen-/Policy-Prüfung, Passwort-Reset-Rate-Limit |
| Event-Quelllinks | Bestehende Regressionstests für Validierung, Persistenz, Projektionen und PostgreSQL-Migrations-SQL |

Jeder Test verwendet eine eigene EF-InMemory-Datenbank. Identity, Passwort-Hashing,
Bestätigungs-/Reset-Tokens und JWTs werden mit den echten Implementierungen geprüft.
Data-Protection-Schlüssel bleiben im Arbeitsspeicher. SMTP-Clients und
Instagram-HTTP-Antworten werden simuliert. In HTTP-Tests sind die Instagram-Worker
deaktiviert; separate Tests prüfen die Worker.

## Coverage

```powershell
dotnet test EventLauscherApi.sln -c Release --collect "XPlat Code Coverage" --settings Tests/coverage.runsettings --results-directory TestResults
```

Der Cobertura-Bericht liegt anschließend unter
`TestResults/<Lauf-ID>/coverage.cobertura.xml`. Die Konfiguration erfasst den
Anwendungscode einschließlich der asynchronen Methoden. EF-Migrationsdateien und
der generierte Snapshot sind von der Prozentberechnung ausgenommen; der bestehende
Migrationstest bleibt Bestandteil des Testlaufs.

Der GitHub-Workflow `.github/workflows/tests.yml` führt bei Pushes und Pull Requests
dieselben Tests aus und stellt TRX-Ergebnisse und Coverage als Artefakt bereit.

## Grenzen

Die InMemory-Datenbank prüft keine PostgreSQL-Fremdschlüssel, eindeutigen Indizes,
Transaktionen oder die SQL-Übersetzung von Abfragen. Der Seed-Test ersetzt den
Migrationsausführer und prüft keine Migration auf einem echten Server.
Die Tests für Migrations-SQL/Modell ersetzen keinen vollständigen Datenbank-Rollout.
Für diese Aspekte ist zusätzlich ein Integrationstest gegen eine separate
PostgreSQL-Testinstanz erforderlich.

Die historische Migrationskette beginnt mit Änderungen an bereits vorhandenen
Tabellen; die Migration `Initial` ist leer. Eine leere Datenbank lässt sich deshalb
nicht allein mit dieser Kette initialisieren. Ein solcher Datenbanktest benötigt
eine geklärte Baseline. Die hier ergänzten Tests ändern die Migrationshistorie nicht.

SMTP-/STARTTLS-Verbindungen und die tatsächliche Meta-API werden nicht kontaktiert.
Die lokale Zeitzone wird beim Kalenderexport wie im Anwendungscode verwendet;
die Tests berechnen die UTC-Erwartung aus derselben lokalen Zeitzone.

Hohe Zeilenabdeckung ist kein Nachweis für sämtliche möglichen Eingaben,
Nebenläufigkeit oder das Verhalten externer Systeme.
