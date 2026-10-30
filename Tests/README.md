# Tests

Das vorhandene Projekt `EventLauscherApi.Tests` verwendet xUnit und enthält
Controller-/Service-Tests sowie HTTP-Integrationstests mit `WebApplicationFactory`.
Voraussetzung ist das .NET-8-SDK. Die schnellen Tests benötigen weder PostgreSQL
noch SMTP-/Instagram-Zugangsdaten. Zusätzlich gibt es echte PostgreSQL-Tests,
die ohne explizite Testverbindung als übersprungen angezeigt werden.

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
| Eingabegrenzen | Maximale Textlängen beim Anlegen/Bearbeiten, Koordinatengrenzen einschließlich Nullpunkt, Meldungslänge, fehlende Mediendateien |
| Token-Trennung | Nutzerbindung, Trennung von Bestätigungs-/Reset-Tokens, Entwertung alter Reset-Tokens, Refresh-Ablehnung für gesperrte/unbestätigte Nutzer |
| PostgreSQL | Echte Fremdschlüssel/Unique-Indizes, Löschregeln, atomarer Rollback, SQL-Sortierung, Inbox-Deduplizierung und Ausführung der Quelllink-Migration |
| PostgreSQL mit HTTP | Mehrnutzer-Ablauf von Draft bis Ablehnung, Favoriten nach Unpublish/Republish, öffentliche Profile, tatsächlich versendete Testlinks, Validierung vor Datenbankzugriff |
| Event-Quelllinks | Bestehende Regressionstests für Validierung, Persistenz, Projektionen und PostgreSQL-Migrations-SQL |

Die schnellen Tests verwenden jeweils eine eigene EF-InMemory-Datenbank. Identity, Passwort-Hashing,
Bestätigungs-/Reset-Tokens und JWTs werden mit den echten Implementierungen geprüft.
Data-Protection-Schlüssel bleiben im Arbeitsspeicher. SMTP-Clients und
Instagram-HTTP-Antworten werden simuliert. In HTTP-Tests sind die Instagram-Worker
deaktiviert; separate Tests prüfen die Worker.

## Echte PostgreSQL-Tests

Unter Windows kann das Hilfsskript einen eigenen kurzlebigen PostgreSQL-Server
starten. Es verwendet die installierten Binärdateien, einen freien Loopback-Port
und ein neues Datenverzeichnis unter `TestResults/postgresql-<ID>/`.
Der Server wird auch bei fehlgeschlagenen Tests wieder beendet. Das Skript
verändert keinen vorhandenen PostgreSQL-Dienst. Datenverzeichnis und Logs
bleiben für die Fehlersuche unter `TestResults/` erhalten.

Nur die Datenbanktests, mit dem Installationspfad dieses Rechners:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-PostgreSql.ps1 -PostgreSqlBin "H:\Postgresql\bin"
```

Alle Tests einschließlich PostgreSQL und Coverage:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-PostgreSql.ps1 -PostgreSqlBin "H:\Postgresql\bin" -AllTests -Coverage
```

Der Binärpfad ist auf anderen Rechnern entsprechend anzupassen. `ExecutionPolicy`
gilt hier nur für den gestarteten PowerShell-Prozess.

Alternativ eine explizite Verbindung zu einer **separaten Testinstanz** setzen:

```powershell
$env:EVENTLAUSCHER_TEST_POSTGRES = "Host=127.0.0.1;Port=5432;Database=postgres;Username=test_admin;Password=test-only-password;Pooling=false"
dotnet test EventLauscherApi.sln -c Release --filter Category=PostgreSQL
```

Der Testnutzer benötigt `CREATEDB`. Jeder PostgreSQL-Test erstellt eine eigene
Datenbank mit dem Namen `eventlauscher_test_<zufällige ID>` und entfernt nur diese
Datenbank beim Beenden. Die Verbindung wird ausschließlich aus dieser
Umgebungsvariablen gelesen, niemals aus `appsettings.json` oder User-Secrets.
Eine gesetzte, aber nicht erreichbare Testverbindung lässt die Tests fehlschlagen;
sie werden in diesem Fall nicht stillschweigend übersprungen.

## Coverage

```powershell
dotnet test EventLauscherApi.sln -c Release --collect "XPlat Code Coverage" --settings Tests/coverage.runsettings --results-directory TestResults
```

Der Cobertura-Bericht liegt anschließend unter
`TestResults/<Lauf-ID>/coverage.cobertura.xml`. Die Konfiguration erfasst den
Anwendungscode einschließlich der asynchronen Methoden. EF-Migrationsdateien und
der generierte Snapshot sind von der Prozentberechnung ausgenommen; der bestehende
Migrationstest bleibt Bestandteil des Testlaufs.

Der GitHub-Workflow `.github/workflows/tests.yml` startet einen separaten
PostgreSQL-17-Service und führt bei Pushes und Pull Requests auch die echten
Datenbanktests aus. TRX-Ergebnisse und Coverage werden als Artefakt bereitgestellt.

## Grenzen

Die InMemory-Datenbank prüft keine PostgreSQL-Fremdschlüssel, eindeutigen Indizes,
Transaktionen oder SQL-Übersetzung. Dafür sind die oben beschriebenen PostgreSQL-Tests
vorgesehen. Sie initialisieren ihren Ausgangszustand aus dem aktuellen EF-Modell
mit `EnsureCreated`. Der Seed-Test ersetzt weiterhin den Migrationsausführer.

Die historische Migrationskette beginnt mit Änderungen an bereits vorhandenen
Tabellen; die Migration `Initial` ist leer. Eine leere Datenbank lässt sich deshalb
nicht allein mit dieser Kette initialisieren. Der neue Migrationstest rekonstruiert
deshalb die unmittelbar vorherige Schema-Version und prüft gezielt
`AddEventSourceLinks` vorwärts, rückwärts und erneut vorwärts. Der vollständige
historische Rollout ab einer leeren Datenbank bleibt ungeprüft, bis eine Baseline
geklärt ist. Die Tests ändern die Migrationshistorie nicht.

SMTP-/STARTTLS-Verbindungen und die tatsächliche Meta-API werden nicht kontaktiert.
Die lokale Zeitzone wird beim Kalenderexport wie im Anwendungscode verwendet;
die Tests berechnen die UTC-Erwartung aus derselben lokalen Zeitzone.

Hohe Zeilenabdeckung ist kein Nachweis für sämtliche möglichen Eingaben,
Nebenläufigkeit oder das Verhalten externer Systeme. Gleichzeitige Refresh-Aufrufe
und konkurrierende Schreibzugriffe mehrerer Worker sind noch nicht abgesichert.
Die Refresh-Prüfung verhindert neue Tokens für gesperrte Nutzer; bereits ausgestellte
Access-Tokens behalten derzeit bis zu ihrem Ablauf ihre Gültigkeit.
