# Mirage Backend

ASP.NET Core API for the Mirage relationship platform.

## Current scope

This is a foundation/MVP API shaped around the BRD:

- Platform overview
- Profile discovery
- Open date requests
- Counsellor directory
- Organisation listing
- Counselling session booking
- Marriage companion check-ins

The current implementation uses an in-memory seed store so the frontend can be built and reviewed quickly. The next production step is replacing `MirageSeedStore` with EF Core/PostgreSQL repositories and adding ASP.NET Core Identity/JWT.

## Run

```powershell
.\scripts\start-dev.ps1
```

Default API base URL:

```text
http://127.0.0.1:5088/api
```

If your local ASP.NET profile chooses a different port, use the URL printed by `dotnet run`.

## API endpoints

- `GET /api/health`
- `GET /api/platform/overview`
- `GET /api/profiles?intent=Dating&city=Lagos`
- `GET /api/date-requests`
- `POST /api/date-requests`
- `GET /api/counsellors?specialisation=communication&freeOnly=true`
- `GET /api/organisations`
- `GET /api/sessions`
- `POST /api/sessions`
- `GET /api/companion/check-ins`

## Production architecture path

- Add EF Core with PostgreSQL migrations.
- Add ASP.NET Core Identity, JWT access tokens, and role policies for user, church admin, counsellor, mentor, and platform admin.
- Add SignalR hubs for chat, notifications, and session presence.
- Add Outbox + message broker for async notifications.
- Add encrypted message storage and API-level anonymity enforcement.
- Add CI with build, tests, formatting, and container image publishing.
