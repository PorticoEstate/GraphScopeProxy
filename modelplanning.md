# GraphScope Proxy

Dette prosjektet er en **drop-in proxy** foran Microsoft Graph API, bygget med **PHP Slim 4**.  
Den speiler Microsoft Graph-paths 1:1, men legger på **kontrollert ressurs-scope pr. gruppe** slik at klienter kun kan se og operere på et begrenset sett av rom / ressursmailbokser.  

Hovedidé: Klient oppgir en Azure AD / Entra ID *groupId* ved innlogging. Medlemmer i denne gruppen (filtrert til rom / ressurser) blir «tillatelses-listen». Et tilgangstoken (JWT) representerer dette scopet og alle senere kall filtreres eller avvises mot denne listen.

---

## 📦 Teknologistack

- PHP 8.4
- Slim 4
- GuzzleHTTP (for å kalle Graph)
- Monolog (logging)
- Slim Middleware (for auth & error handling)
- Docker & Docker Compose

---

## 🔐 Ressurs-scope per gruppe

1. Klient kaller `/auth/login` med `apiKey` + `groupId` (evt. `groupAlias`).  
2. Proxy validerer `apiKey` og bruker app credentials til Graph.  
3. Proxy henter gruppens medlemmer (paginering håndteres) og valgfritt supplerer med `/places` data.  
4. Medlemmer klassifiseres (room / workspace / equipment / generic resource) etter konfigurerte regler.  
5. Det bygges en intern liste `allowedResources` (id, mail/upn, type).  
6. Full liste caches server-side (f.eks. Redis / APCu) med en kort TTL (10–15 min).  
7. JWT til klient inneholder kun en `tid` (tokenId), `gid` (groupId) og antall ressurser, ikke hele listen.  
8. Ved alle proxiede kall slår middleware opp `tid` → allowedResources og validerer/filtrerer.

Middleware-kontroller:

- Blokkerer kalenderkall / events-endepunkter mot rom utenfor scope.
- Filtrerer lister (`/rooms`, `/places/microsoft.graph.room`, evt. flere typer) til kun tillatte ressurser.
- (Valgfritt) Filtrerer også søk / query-varianten hvis `$filter` eller `$search` er brukt (post-filter på resultatsettet).

Fordeler:

- Ingen behov for å gi klient bred Graph-tilgang – alt skjer via app-en.  
- Tilgang endres ved kun å justere gruppemedlemskap (enkelt for IT).  
- Skalerer med mange klienter fordi caching + minimal JWT payload.

Edge cases & håndtering:

- Rom fjernet fra gruppe: Etter cache-expire → 403 på videre kall.  
- Nytt rom: Blir tilgjengelig etter cache-expire eller manuelt `/admin/refresh/{groupId}`.  
- Stor gruppe: Batch/paginering loop; ved >N (konfig) kan fallback være «hash-only» listing for store payloads.  
- Ukjent rom i request: Ingen prefetch – gir direkte 403 uten Graph-roundtrip.

Sikkerhetsnotater:

- JWT signeres med sterk nøkkel (HS256 eller RS256).  
- `tid` ikke forutsigbar (`random_bytes`).  
- Ingen sensitive romdata i selve tokenet.  
- Rate limiting anbefales per apiKey (se videre plan).

Konfig (miljøvariabler forslag):

```bash
GS_ALLOWED_PLACE_TYPES=room,workspace,equipment
GS_ALLOW_GENERIC_RESOURCES=true
GS_SCOPE_CACHE_TTL=900
GS_MAX_SCOPE_SIZE=500
GS_REQUIRE_GROUP_ALIAS=false
```

---

## 🧱 Arkitektur (høy-nivå)

Komponenter:

- AuthRoute: Login-endpoint som etablerer scope og utsteder JWT.
- GraphToken helper: Henter / cacher app access token mot Graph (client credentials).
- ResourceClassifier: Klassifiserer group members / places.
- ScopeCache: Abstrakt cache-lag (interface + implementasjon, f.eks. in-memory + Redis adapter).
- AuthMiddleware: Validerer JWT / apiKey fallback.
- ResourceScopeMiddleware: Håndhever og filtrerer.
- ProxyRoute: Fanger alle «/v1.0/*» (og ev. beta) og videresender til Graph via Guzzle.
- LoggingMiddleware: Strukturert logging (korrelasjons-ID, latency, scopeId, groupId).
- ErrorMiddleware: Standardiserte JSON-feil.

Sekvens – typisk request (kalenderkall):
`Client → AuthMiddleware (JWT ok?) → ResourceScopeMiddleware (rom i scope?) → ProxyRoute (Guzzle → Graph) → ResponseFilter (i scope middleware) → Client`

## 🔧 Prosjektstruktur

```text
graphscope-proxy/
├─ src/
│  ├─ Middleware/
│  │  ├─ AuthMiddleware.php
│  │  ├─ ResourceScopeMiddleware.php
│  │  ├─ LoggingMiddleware.php
│  │  └─ ErrorMiddleware.php
│  ├─ Routes/
│  │  ├─ AuthRoute.php
│  │  └─ ProxyRoute.php
│  ├─ Helpers/
│  │  ├─ GraphToken.php
│  │  ├─ ResourceClassifier.php
│  │  ├─ ScopeCacheInterface.php
│  │  └─ ScopeCacheApcu.php (ev. ScopeCacheRedis.php)
│  ├─ Domain/
│  │  └─ Models/ (RoomResource.php etc.)
│  └─ Config/
│     └─ settings.php
├─ tests/
│  ├─ Middleware/
│  ├─ Helpers/
│  └─ Integration/
├─ logs/
│  └─ proxy.log
├─ public/
│  └─ index.php
├─ composer.json
├─ Dockerfile
├─ docker-compose.yml
├─ README.md
└─ modelplanning.md
```

## 🗂️ Datastrukturer (skisse)

```text
allowedResources: [
  mail|upn(lower) => {
    id: string,
    mail: string,
    type: room|workspace|equipment|generic,
    // optional: capacity, displayName (kun i cache)
  }, ...
]

JWT claims: {
  tid: string,   // tokenId
  gid: string,   // groupId
  rc:  number,   // resource count
  iat, exp
}
```

## 🔐 Sikkerhet & Policy

| Område | Tiltak |
|--------|--------|
| Transport | Kun HTTPS (proxy bak ingress / TLS termination) |
| Auth | API-key → Login → JWT (scoped). JWT signatur valideres hver request |
| Autorisasjon | ResourceScopeMiddleware mot cache; ingen direkte Graph-kall for validering |
| Least privilege | App-registrering gir bare nødvendige app-permissions (GroupMember.Read.All, Place.Read.All, Calendars.Read/Write) |
| Rate limiting | (Fremtid) per apiKey + global burst limit |
| Logging | Strukturert JSON, ingen persondata, inkluder korrelasjons-ID |
| Key rotation | Støtte flere aktive `API_KEY_x` nøkler samtidig |
| Cache poisoning | `tid` random, ingen klientstøtte for å påvirke innhold |

## 🛠️ Admin-operasjoner

- Legg til rom: Opprett/konfigurer resource mailbox → legg i gruppen.
- Fjern rom: Fjern fra gruppen → blir utilgjengelig etter TTL / refresh.
- Manuell refresh: `POST /admin/refresh/{groupId}` (krever admin API-key) → invalidér cache.
- Helse: `GET /admin/health` (status + versjon) / `GET /admin/scope/{groupId}` (kun count + hash).
- Rotasjon API-nøkler: Oppdater `.env` og (valg) reload container.

## 🧪 Teststrategi (kort)

Enhet:

- ResourceClassifier (input-varianter, heuristikk, places-match).
- ScopeCache (store/fetch/ttl).
- ResourceScopeMiddleware (tillat / avvis / filtrering).

Integrasjon:

- Login → token → kall liste-endepunkt (filtrert resultat).
- Login med stor gruppe (paginering) → alt tilgjengelig.

Feilscenarier:

- Ugyldig groupId (400).
- Tomt romsett (200 login men rc=0) → senere kalenderkall 403.
- Utgått JWT (401).

## 📈 Observability

- Structured log keys: `ts`, `level`, `msg`, `corrId`, `groupId`, `scopeId`, `resourceCount`, `latencyMs`, `status`.
- Metrikker (valgfritt): counters for `requests_total`, `scope_cache_hits`, `scope_cache_miss`, `forbidden_requests`.

## 🗺️ Gjennomføringsplan / Milepæler

Fase 0 – Grunnoppsett (1):

- Composer init, Slim skeleton, basic Dockerfile, health endpoint.

Fase 1 – Graph integrasjon (2):

- GraphToken helper (client credentials flow).
- Konfig via miljøvariabler (tenant, client_id, client_secret, scope URL).

Fase 2 – Auth & Login (3):

- API-key validering (statisk liste i .env initially).
- `/auth/login` route returnerer dummy JWT.

Fase 3 – Scope bygging (4):

- Hent group members (+ paginering).
- Klassifisering + caching (in-memory / APCu).
- JWT med `tid` claim.

Fase 4 – Proxy-baseline (5):

- Generic ProxyRoute → videresend til Graph (headers, query, body passthrough).
- Error wrapping.

Fase 5 – Scope Enforcement (6):

- ResourceScopeMiddleware (request-blokk + response-filtrering /rooms & /places/* ).
- Tester for avvisning / filtrering.

Fase 6 – Observability & Logging (7):

- LoggingMiddleware (struct logs).
- Korrelasjons-ID (request header `X-Correlation-ID` fallback generering).

Fase 7 – Admin & Hardening (8):

- `/admin/refresh/{groupId}` + tilgangskontroll.
- Rate limit (enkel per IP/apiKey – evt. senere).
- Configurable TTL.

Fase 8 – Utvidelser (9):

- Redis-basert ScopeCache.
- Metrikker endpoint (Prometheus /stats).
- Ekstra ressurs-typer.

## ⚠️ Risiko & Mitigering

| Risiko | Konsekvens | Mitigering |
|--------|-----------|-----------|
| Stor gruppe (mange ressurser) | Lenger login-tid | Asynk preload / øvre grense / hashing |
| Feilklassifisering | Manglende/falsk tilgang | Klar konfig + tests + places cross-check |
| Cache-stale | Forsinket tilgangsoppdatering | Kort TTL + manual refresh |
| Token leakage | Uautorisert bruk | Kort exp, rotér signing key, minst data i JWT |
| Graph rate limits | 429 / latency | Caching, backoff, minimere antall kall |

## ✅ Ferdig‑kriterier (MVP)

- Login med `groupId` gir JWT (`tid`,`gid`,`rc`).
- Kall mot rom i scope lykkes; utenfor scope → 403.
- `/rooms` / `/places/microsoft.graph.room` filtreres korrekt.
- Logging har scopeId + groupId.
- Cache refresh fungerer.
- Dokumentasjon (README) beskriver konfig og flyt.

---

Neste revisjon: Implementere faktisk kode iht. denne planen og skrive enhetstester før produksjonssetting.
