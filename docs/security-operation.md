# Auth-Limits und Webhook-Betrieb

## Änderungen beim Rollout

- `POST /auth/login`: 10 Anfragen pro Minute und Client-IP.
- `POST /auth/forgot-password`: 5 Anfragen pro 5 Minuten und Client-IP.
- Beide Limits liefern HTTP 429 statt des bisherigen Default-Status HTTP 503.
  Abgewiesene Anfragen erreichen die Controller nicht.
- `POST /api/webhooks/instagram` erfordert `Instagram:AppSecret`, als
  Umgebungsvariable `Instagram__AppSecret`. Das Meta-App-Secret muss vor dem
  Rollout für einen aktiven Webhook in der Laufzeit konfiguriert werden.
  Es wird nicht im Repository gespeichert. GET-Verifizierung, Verify-Token
  und Graph-Access-Token erfüllen andere Aufgaben und bleiben unabhängig.
- Die Signatur ist HMAC-SHA256 über die exakten Body-Bytes mit dem App-Secret.
  Fehlende/ungültige Signaturen liefern 401; ein fehlendes App-Secret liefert
  503. Nur authentische Payloads werden gespeichert. Payloads über 1 MiB
  werden mit 413 abgelehnt, auch wenn kein Content-Length übermittelt wird.
- Token-Präfixe und Token-Längen werden nicht mehr zur Diagnose geloggt.

## Reverse Proxy und mehrere Instanzen

Die Rate-Limits verwenden `HttpContext.Connection.RemoteIpAddress` nach der
Forwarded-Headers-Middleware. Ein beliebiger `X-Forwarded-For`-Header wird nicht
als eigenständiger Schlüssel akzeptiert. ASP.NET Core vertraut standardmäßig
Loopback-Proxys. Bei anderen Proxy-Adressen muss die konkrete Topologie über
`ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` und ein passendes
`ForwardLimit` konfiguriert werden. Vertrauenslisten nicht pauschal leeren.
Der vorgeschaltete Proxy muss eingehende Forwarding-Header kontrolliert setzen.
Ohne passende Konfiguration können Clients hinter einem Proxy ein Limit teilen.
Clients hinter derselben NAT-Adresse teilen sich das IP-Limit ebenfalls.

Limits werden im jeweiligen API-Prozess gespeichert, setzen sich bei Neustart
zurück und sind bei mehreren Replikaten nicht global. Für eine spätere
horizontale Skalierung ein gemeinsames Limit am Gateway oder einen verteilten
Limiter vorsehen. Identity-Lockout bleibt zusätzlich aktiv.

## Verifikation

`dotnet test EventLauscherApi.sln -c Release` prüft unter anderem:

- Erlaubte Anfragen bis zum Limit, danach HTTP 429.
- Separate Kontingente für verschiedene Verbindungs-IP-Adressen.
- Keine Umgehung durch gefälschte Forwarding-Header eines untrusted Clients.
- Gültige Meta-Signaturen mit Unicode und Whitespace im unveränderten Body.
- Ablehnung fehlender, fehlerhafter und falscher Signaturen sowie manipulierter
  Payloads; keine Persistenz abgelehnter Anfragen.
- Fehlendes App-Secret und übergroße Payloads.

Diese Checks simulieren den Meta-Absender. Ein echter Meta-End-to-End-Test und
Prüfungen der produktiven Proxy-Konfiguration sind separate Betriebsprüfungen.
