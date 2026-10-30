# Optionale Event-Quellen / Übergabe an Flutter

Quellen gehören zu einem Event. `sourceUrl` bezeichnet den dauerhaften Link zum
ursprünglichen Instagram-Beitrag (Post, Karussell oder Reel), Facebook-Beitrag oder
Facebook-Event. Eine temporäre Bild-/Video-Download-URL gehört nicht in dieses Feld.
Die App übergibt die Quelle erst beim bisherigen, manuell bestätigten Erstellen des
Events. Diese Erweiterung führt keinen neuen Importer ein.

## JSON-Vertrag

| JSON-Feld | JSON-Datentyp / Dart-Typ | Erlaubte Werte |
| --- | --- | --- |
| `sourceUrl` | String oder `null` / `String?` | Absolute HTTP-/HTTPS-URL, nach Trimmen maximal 2000 Zeichen |
| `sourcePlatform` | String oder `null` / `String?` | Exakt `"instagram"` oder `"facebook"` (kleingeschrieben) |

Beide Angaben sind unabhängig voneinander optional. Es gibt keine automatische
Plattformableitung und keine Prüfung, ob URL-Host und Plattform übereinstimmen.
Äußere Leerzeichen werden entfernt. Die URL wird ansonsten unverändert gespeichert,
einschließlich Pfad und Query-Parametern. Der Server ruft die URL nicht ab.

Die Felder werden in Event-Responses mit genau diesen Namen ausgegeben. Ohne Quelle
lauten die Werte `"sourceUrl": null` und `"sourcePlatform": null`. Flutter sollte
auch fehlende Felder als `null` behandeln, etwa bei Antworten älterer Backends.
Bestehende Medienfelder `SourceURL` und `SourcePlattform` sind kein Ersatz für die
Event-Quelle. Auch bei derselben `mediaId` kann jedes Event eine andere Quelle haben.
Eine Migration übernimmt alte Medienquellen daher nicht automatisch in Events.

## Endpunkte

| Methode und Pfad | Quellen in Request / Response |
| --- | --- |
| `POST /api/Events` | Optionale Quellen im JSON-Request; Response `201` enthält das erzeugte Event samt Quelle |
| `GET /api/Events` | Veröffentlichte Events, Quelle in jedem Listenelement |
| `GET /api/Events/{id}` | Öffentliches Event-Detail, Quelle auf oberster Ebene; weiterhin nur Published, sonst `404` |
| `GET /api/Events/my` | Eigene Uploads, Quelle in jedem Listenelement |
| `GET /api/Events/saved` | Eigene gespeicherte veröffentlichte Events, Quelle in jedem Listenelement |
| `GET /api/Events/dup-corpus` | Quelle in jedem Listenelement |
| `GET /api/reviewer/events/unpublished` | Unveröffentlichte Events, Quelle in jedem Listenelement |
| `GET /api/reviewer/events/{id}` | Reviewer-Detail: Quelle innerhalb von `event` |
| `PUT /api/reviewer/events/{id}/review` | Optionale Quellen zum Bearbeiten; Erfolg `204` ohne Body, danach erneut laden |
| `GET /api/public/users/{username}/favorites` | Öffentliche Favoriten, Quelle in jedem Listenelement |

Die bisherigen Authentifizierungs- und Rollenanforderungen bleiben bestehen:
Erstellen und eigene Listen erfordern Login, Reviewer-Endpunkte Reviewer/Admin.
Diese Aufgabe ergänzt keinen allgemeinen Bearbeitungsendpunkt für Uploader.
Status-/Speicheraktionen und HTML-/Kalender-Endpunkte haben keinen neuen JSON-Vertrag.

### Erstellen

`POST /api/Events`, mit dem bisherigen Bearer-Token:

```json
{
  "title": "Konzert am Freitag",
  "date": "2026-10-16",
  "time": "20:00",
  "mediaId": 42,
  "sourceUrl": "https://www.instagram.com/p/ABC123/",
  "sourcePlatform": "instagram"
}
```

Response `201`, Auszug des Event-Objekts (weitere bisherige Event-Felder bleiben):

```json
{
  "id": 123,
  "title": "Konzert am Freitag",
  "mediaId": 42,
  "status": 0,
  "sourceUrl": "https://www.instagram.com/p/ABC123/",
  "sourcePlatform": "instagram"
}
```

Normale Uploader erzeugen weiterhin Drafts (`status: 0`); Organizer, Reviewer und
Admin behalten ihre bestehende Berechtigung zur sofortigen Veröffentlichung
(`status: 1`). Quellen ändern diese Regeln nicht. Für Drafts verwendet die App ihre
Uploadliste oder das Reviewer-Detail; der öffentliche Detailendpunkt gibt sie
weiterhin nicht zurück. Ein Import allein erzeugt oder veröffentlicht kein Event.

### Erneut laden

`GET /api/Events/123` liefert nach Veröffentlichung die Quelle wieder auf oberster
Ebene, wie im obigen Response-Auszug. Das Reviewer-Detail
`GET /api/reviewer/events/123` hat die bisherige Verschachtelung (Auszug):

```json
{
  "event": {
    "id": 123,
    "title": "Konzert am Freitag",
    "sourceUrl": "https://www.instagram.com/p/ABC123/",
    "sourcePlatform": "instagram"
  },
  "uploader": {
    "uploadUserId": "11111111-1111-1111-1111-111111111111",
    "email": "uploader@example.com"
  },
  "reviewer": null
}
```

### Bearbeiten, Beibehalten, Entfernen

Es gilt die bestehende partielle Update-Konvention bei
`PUT /api/reviewer/events/123/review`:

| Request-Wert pro Quellenfeld | Beim Erstellen | Beim Bearbeiten |
| --- | --- | --- |
| Feld ausgelassen | `null` speichern | Vorhandenen Wert beibehalten |
| `null` | `null` speichern | Vorhandenen Wert beibehalten |
| `""` oder ausschließlich Whitespace | `null` speichern | Diesen Wert ausdrücklich entfernen (auf `null` setzen) |
| Gültiger nichtleerer String | Getrimmten Wert speichern | Diesen Wert ersetzen |

Ein bisheriger Bearbeitungsrequest wie `{"title":"Neuer Titel"}` verändert die
Quelle nicht. Um eine Quelle durch ein Facebook-Event zu ersetzen:

```json
{
  "sourceUrl": "https://www.facebook.com/events/987654321/",
  "sourcePlatform": "facebook"
}
```

Um beide Angaben ausdrücklich zu entfernen:

```json
{
  "sourceUrl": "",
  "sourcePlatform": ""
}
```

Beide Requests erhalten bei Erfolg `204` ohne Body. Anschließend zeigt ein erneutes
GET die geänderten Werte. Für eine vollständige Entfernung muss Flutter **beide**
leeren Strings senden; `null` bewahrt die Quelle. Einzelne Felder können unabhängig
bearbeitet oder entfernt werden; ausgelassene Gegenstücke bleiben erhalten.
Freigabe, Veröffentlichung, Zurückziehen und Ablehnung behalten die Quelle.

## Validierung

Nichtleere URLs müssen absolut sein, `http` oder `https` als Schema und einen Host
haben und dürfen nach dem Trimmen maximal 2000 Zeichen enthalten. Relative URLs,
reine Hostnamen, `ftp:`, `file:`, `javascript:` und syntaktisch ungültige URLs werden
abgewiesen. Nichtleere Plattformen müssen exakt `instagram` oder `facebook` sein;
`Instagram` und unbekannte Plattformen sind ungültig. Leere Angaben sind zulässig.

Create und Review-Update liefern bei solchen Fehlern HTTP `400` mit einer
Fehlermeldung als String nach der bestehenden Controller-Konvention. Bei
`Accept: application/json` zum Beispiel:

```json
"sourceUrl muss eine absolute HTTP-/HTTPS-URL mit maximal 2000 Zeichen sein."
```

oder:

```json
"sourcePlatform muss instagram oder facebook sein."
```

Ein ungültiger Request speichert weder das Event noch Teiländerungen.
Falsche JSON-Datentypen werden wie bisher durch ASP.NET Core mit `400` abgewiesen.

## Migration und Rollout

Die EF-Core-Migration `20261010233001_AddEventSourceLinks` ergänzt ausschließlich
die nullable Spalten `Events.SourceUrl` (`character varying(2000)`) und
`Events.SourcePlatform` (`character varying(20)`). Vorhandene Events erhalten `NULL`.
Der Model-Snapshot ist aktualisiert. Die Migration verändert keine Medien und
enthält keinen Quellen-Backfill. Ein Downgrade entfernt die beiden Quellen-Spalten
und damit ihre Werte.

Die Migration wurde nicht auf eine produktive oder unbekannte Datenbank angewendet.
Sie wurde anschließend vom Benutzer mit EF Core auf einer ausdrücklich ausgewählten
lokalen PostgreSQL-Testkopie angewendet und dort über die echte API geprüft.
Für eine bereits bis `20260513203454_AddInstagramSources` migrierte Datenbank lässt
sich das SQL ohne Datenbankzugriff zur Prüfung erzeugen:

```bash
dotnet ef migrations script 20260513203454_AddInstagramSources 20261010233001_AddEventSourceLinks --project EventLauscherApi.csproj --output sql/AddEventSourceLinks.sql
```

Rollout-Reihenfolge:

1. Ziel-Datenbank und vorhandene Migrationshistorie prüfen. Die neue Migration auf
   der ausdrücklich ausgewählten Datenbank im üblichen Migrationsprozess anwenden.
   Die alten Schema-/Init-SQL-Dateien im Repository ersetzen die aktuelle
   EF-Migrationshistorie nicht.
2. Das neue Backend ausrollen. Es benötigt die beiden neuen Spalten; bisherige
   App-Versionen funktionieren weiter. Alte Bearbeitungsrequests erhalten Quellen.
3. Flutter ausrollen: nullable Felder lesen, beim bestätigten Erstellen den
   Permalink und die Plattform senden, beim Löschen beide leeren Strings senden.

Keinen neuen App-Request vor dem Backend-Rollout aktivieren: ältere Backends können
unbekannte JSON-Felder ignorieren und die Quelle dadurch unbemerkt verlieren.

## Prüfungen

Für einen Test der echten lokalen Anmeldung das Backend auf
`https://localhost:7145` mit der Testdatenbank starten. Anschließend in einem
zweiten PowerShell-Terminal im Repository-Verzeichnis ausführen:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-LocalAuthentication.ps1 -CopyTokenToClipboard
```

Das Skript fragt die E-Mail-Adresse und das Passwort des Eventlauscher-Kontos
lokal ab, übernimmt `accessToken` direkt aus der Login-Antwort und prüft
`GET /auth/me` sowie `GET /api/Events/my` mit demselben Token. Es legt keine Events
an und zeigt weder Zugangsdaten noch Tokens an. Die optionale Angabe
`-CopyTokenToClipboard` kopiert den geprüften Token für Swagger in die lokale
Zwischenablage. Ohne diesen Schalter wird die Zwischenablage nicht verändert.
`ExecutionPolicy Bypass` gilt nur für den gestarteten PowerShell-Prozess.

```bash
dotnet build EventLauscherApi.sln
dotnet test EventLauscherApi.sln --no-build
```

Die Regressionstests verwenden isolierte EF-InMemory-Datenbanken und frische
DbContexts zum erneuten Laden. Sie prüfen die Event-Controller, JSON-Feldnamen,
alle Event-Projektionen, gemeinsame Medien, URL-/Plattformvalidierung und
Rückwärtskompatibilität einschließlich Freigabe. Ein zusätzlicher Offline-Test
prüft die PostgreSQL-Migrationsoperationen, erzeugtes SQL und Übereinstimmung des
Model-Snapshots. Zusätzlich wurden nach dem Migrationslauf auf der lokalen
PostgreSQL-Testkopie Erstellen und erneutes Laden sowie mit einem kurzlebigen lokalen
Reviewer-Testtoken Beibehalten bei fehlenden/`null`-Angaben, Ersetzen, URL-/Plattform-
Validierung ohne Teiländerungen und ausdrückliches Entfernen per HTTP geprüft.
Die bestehenden Kontorollen wurden dafür nicht geändert.
