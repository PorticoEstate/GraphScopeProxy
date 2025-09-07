# GraphScope Proxy

En lettvekts, sikker **drop‑in HTTP proxy** foran **Microsoft Graph API** som speiler Graph-endepunkter (`/v1.0/*` – og valgfritt `/beta`) 1:1, men innfører **gruppestyrt ressurs‑scope** (rom / resource mailbokser) slik at klienter kun får tilgang til et begrenset sett av ressurser.

> Kort fortalt: Klient logger inn med `apiKey` + `groupId` → proxy bygger en *tillatelsesliste* over rom i gruppen → utsteder et kompakt JWT (inneholder ikke selve listen) → alle videre Graph-kall filtreres / avvises i sanntid basert på denne listen.

Se detaljert konsept- og løsningsplan i: [modelplanning.md](modelplanning.md)

---

## ✨ Hovedegenskaper

- 1:1 proxying av Microsoft Graph (transparent for klient)
- Scope-begrensning pr. Azure AD / Entra ID gruppe
- Minimal JWT (kun tokenId, groupId, count)
- Server-side cache av ressursliste (APCu / Redis)
- Responsfiltrering for rom-/places‑endepunkter
- Strukturerte logger med korrelasjons‑ID
- Klar utvidelsesvei for rate limiting og metrikker

## 🧱 Teknologistack

| Område | Valg |
|--------|------|
| Runtime | PHP 8.4 |
| Web Framework | Slim 4 |
| HTTP klient | GuzzleHTTP |
| Logging | Monolog (JSON) |
| Auth | API-key + JWT (HS256/RS256) |
| Cache | APCu (MVP) / Redis (opsjon) |
| Container | Docker + Compose |

## 🔐 Flyt (login → beskyttet kall)

1. Klient kaller `/auth/login` med `apiKey` + `groupId` (eller alias).
2. Proxy validerer key og henter app access token mot Graph (client credentials).
3. Henter gruppemedlemmer (paginering) + evt. supplerende `/places` data.
4. Klassifiserer relevante ressurser (room / workspace / equipment / generic).
5. Lagrer full liste i cache og genererer en unik `tid` (tokenId).
6. Utsteder JWT: `{ tid, gid, rc, iat, exp }`.
7. Senere kall: Middleware slår opp `tid` → allowedResources, filtrerer / validerer.

## 📂 Foreslått mappestruktur

```text
src/
  Middleware/
  Routes/
  Helpers/
  Domain/Models/
  Config/
public/index.php
composer.json
Dockerfile
docker-compose.yml
```

(Blir etablert fortløpende – se plan i `modelplanning.md`.)

## ⚙️ Miljøvariabler (utvalg)

| Variabel | Beskrivelse | Eksempel | Default |
|----------|-------------|----------|---------|
| `MS_TENANT_ID` | Entra ID tenant | `xxxxxxxx-...` | – |
| `MS_CLIENT_ID` | App (client) ID | `yyyyyyyy-...` | – |
| `MS_CLIENT_SECRET` | App secret | (hemmelig) | – |
| `GS_ALLOWED_PLACE_TYPES` | Komma-separert typer | `room,workspace,equipment` | `room,workspace` |
| `GS_ALLOW_GENERIC_RESOURCES` | Ta med generics | `true/false` | `false` |
| `GS_SCOPE_CACHE_TTL` | TTL sekunder | `900` | `900` |
| `GS_MAX_SCOPE_SIZE` | Øvre grense ressurser | `500` | `500` |
| `GS_REQUIRE_GROUP_ALIAS` | Krev alias | `true/false` | `false` |
| `API_KEYS` | Komma-separert API-nøkler | `key1,key2` | – |
| `JWT_SIGNING_KEY` / `JWT_PRIVATE_KEY` | Nøkkel for signering | (streng / PEM) | – |

(Endelig navnkonvensjon kan justeres under implementasjon.)

## 🚀 Hurtigstart (Docker – planlagt)

```bash
# 1. Klon repo
 git clone <repo-url> && cd GraphScopeProxy

# 2. Opprett .env (basert på eksempelfil når den foreligger)
 cp .env.example .env && edit .env

# 3. Bygg & start
 docker compose up --build

# 4. Health check
 curl -s http://localhost:8080/admin/health
```

## 🔗 Viktige endepunkter (MVP)

| Metode | Path | Formål |
|--------|------|--------|
| `POST` | `/auth/login` | Opprett scope + få JWT |
| `GET` | `/admin/health` | Liveness / versjon |
| `POST` | `/admin/refresh/{groupId}` | Invalider cache for gruppe |
| `ANY` | `/v1.0/*` | Proxy mot Microsoft Graph |

(Flere admin-/observability-endepunkter kan komme.)

### Eksempel: Login respons (skisse)

```json
{
  "token": "<jwt>",
  "groupId": "<gid>",
  "resourceCount": 37,
  "expiresIn": 900
}
```
Authorization header for videre kall:

```text
Authorization: Bearer {JWT}
```


## 🛡️ Sikkerhet (kort)

- Kun nødvendige Graph app-permissions (Least Privilege)
- Kortlevende JWT + rotérbar signing key
- Ingen full ressursliste i token (reduksjon av lekkasjerisiko)
- Cache-inndirekte via `tid` (u-forutsigbar)
- Planlagt rate limiting per apiKey / globalt

Detaljer i [modelplanning.md](modelplanning.md#🔐-sikkerhet--policy).

## 🧪 Teststrategi (oversikt)

- Enhetstester for klassifisering, cache og scope‑enforcement
- Integrasjon: login → filtrert liste → autorisert/forbudt kall
- Feilhåndtering: utløpt token, ukjent ressurs, tom gruppe

## 🧭 Videre arbeid / Roadmap

Faser (kort):

1. Grunnoppsett & health
2. Graph tokenhåndtering
3. Auth & login (dummy JWT → ekte)
4. Scopebygging + caching
5. Proxy-baseline
6. Enforcement & filtrering
7. Logging / observability
8. Admin & hardening
9. Utvidelser (Redis, metrikker, osv.)

Se full milepælbeskrivelse i `modelplanning.md`.

## 🧰 Lokal utvikling (planlagt)

```bash
composer install
php -S 0.0.0.0:8080 -t public
```

Eller via Docker (se Hurtigstart). Slim `index.php` vil registrere middleware og routes.

## 🗃️ Logging & Observability

Strukturerte JSON-logger med nøklene: `ts`, `level`, `msg`, `corrId`, `groupId`, `scopeId`, `resourceCount`, `latencyMs`, `status`.

Mulig utvidelse: Prometheus `/stats` med teller for cache hits/miss og forbudd.

## 📄 Lisens

Se `LICENSE`.

## 🤝 Bidrag

Åpne gjerne issues / forslag. Pull requests bør inkludere:

- Kort beskrivelse av endring
- Relaterte tester (om relevant)
- Oppdatert dokumentasjon

---

MVP-status: Kodeimplementasjon pågår. For detaljert arkitektur, edge cases og risikovurdering – se [modelplanning.md](modelplanning.md).
