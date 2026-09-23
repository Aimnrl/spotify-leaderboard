# Spotify Leaderboard

Friends sign in with Spotify, join a group with an invite code, and get ranked on:

- **Minutes**: total listening time
- **Artist diversity**: distinct artists played
- **Streaks**: consecutive days with a play (in each person's own time zone)
- **Superfans**: who listens to each artist the most

ASP.NET Core (.NET 10) API + SQLite + React (Vite).

## How the data gets in

Spotify's API only returns the **last 50 plays**, so the backend polls every connected user every 30 minutes (`Ingestion/PollingService.cs`) and stores new plays, de-duplicated on `(user, played_at)`. Older history comes from the Spotify data export, which users upload on the Import page (`Ingestion/ExportImporter.cs`, handles both the "Account data" and "Extended streaming history" formats, zip or json).

## Setup

1. Create an app at <https://developer.spotify.com/dashboard>:
   - Redirect URI: `http://127.0.0.1:5173/auth/callback`
   - API: Web API
   - While the app is in **development mode**, add each friend's Spotify email under *User Management*; others can't sign in.
2. Store the credentials (never commit the secret):
   ```sh
   dotnet user-secrets set Spotify:ClientId <client id> --project src/Api
   dotnet user-secrets set Spotify:ClientSecret <client secret> --project src/Api
   ```
3. Run the API and the dashboard:
   ```sh
   dotnet run --project src/Api          # http://127.0.0.1:5000, creates leaderboard.db
   cd client && npm install && npm run dev
   ```
4. Open **http://127.0.0.1:5173** (Spotify requires `127.0.0.1`, not `localhost`).

## Tests

```sh
dotnet test
```

## Deploying

- `cd client && npm run build` writes the dashboard into `src/Api/wwwroot`; the API serves it, so one process on one origin.
- Set `Spotify__ClientId`, `Spotify__ClientSecret`, `Spotify__RedirectUri=https://<your-host>/auth/callback` and `ConnectionStrings__Db` as environment variables, and add that redirect URI in the Spotify dashboard.
- Serve over HTTPS (session cookies are `Secure` outside Development).
- Persist the Data Protection key ring (e.g. `AddDataProtection().PersistKeysToFileSystem(...)`), or stored refresh tokens become unreadable after a redeploy and everyone has to sign in again.

## Security notes

- OAuth runs server-side; the client secret and Spotify tokens never reach the browser.
- Refresh tokens are encrypted at rest with ASP.NET Data Protection; access tokens are never stored.
- Session is an HttpOnly, SameSite=Lax cookie (which is also the CSRF defense), and the `state` parameter is checked on the OAuth callback.
- Group data is only visible to members; everyone else gets 404.
