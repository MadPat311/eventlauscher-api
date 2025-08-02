# 📦 Datenbank – Eventlauscher API

In diesem Ordner liegt die Struktur der PostgreSQL-Datenbank für die Eventlauscher API-Anwendung.

## 📁 Struktur

```
database/
├── schema.sql           # Struktur der Datenbank (Tabellen, Constraints etc.)
├── seed.sql             # Beispiel-/Initialdaten für Entwicklungszwecke
├── changelog/
│   ├── 001_add_users_table.sql
│   ├── 002_add_events_table.sql
│   └── ...
└── README.md            # Diese Anleitung
```

---

## ⚙️ Verwendung

### 🏗️ 1. Datenbank erstellen

Falls noch nicht geschehen, erstelle eine leere PostgreSQL-Datenbank:

```bash
createdb eventlauscher_db
```

---

### 🧱 2. Struktur einspielen (schema.sql)

```bash
psql -U deinuser -d eventlauscher_db -f schema.sql
```

---

### 🌱 3. Seed-Daten einfügen (nur für Entwicklung/Test)

```bash
psql -U deinuser -d eventlauscher_db -f seed.sql
```

---

### 🪤 4. Migrationen anwenden (optional)

Manuelle Migrationen sind im `changelog/`-Verzeichnis abgelegt. Diese können in der angegebenen Reihenfolge ausgeführt werden:

```bash
# Beispiel:
psql -U deinuser -d eventlauscher_db -f changelog/001_add_users_table.sql
psql -U deinuser -d eventlauscher_db -f changelog/002_add_events_table.sql
```

> 🔁 **Tipp**: Du kannst dir ein Skript schreiben, das alle Migrationen automatisch in der richtigen Reihenfolge ausführt.

---

## 💡 Hinweise

* `schema.sql` repräsentiert immer den aktuellen, vollständigen Zustand der Datenbank.
* `changelog/` enthält einzelne Änderungen (Migrationen), die zwischen Versionen nachvollziehbar sind.
* `seed.sql` enthält beispielhafte Daten, um die Anwendung lokal testen zu können.

---

## ✅ Vorbereitung für Deployment

* In produktiven Umgebungen wird in der Regel **nur** `schema.sql` verwendet.
* `seed.sql` ist **nicht** für die Produktion gedacht (außer für initiale Demodaten).

---

## 🔐 Sicherheit

Speichere keine sensiblen Zugangsdaten (z. B. Passwörter, API-Keys) in `seed.sql` oder `changelog/`.

---

Wenn du Unterstützung bei Automatisierung oder Datenbanktests brauchst, melde dich gern.
