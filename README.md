# Shameen Investments

Angular 19 frontend with an ASP.NET Core 8 API. The API can use local SQL Server during development or PostgreSQL in hosted environments. Swagger is available at `/swagger`.

## Local development (SQL Server)

1. Install Node.js and the .NET 8 SDK.
2. Configure `ConnectionStrings:Transfers` in `backend/Shameen.Api/appsettings.Development.json` for your local SQL Server. Do not put production credentials in source control.
3. Run the API from `backend/Shameen.Api` with `dotnet run` (Swagger: `http://localhost:5050/swagger` when using the existing launch profile).
4. In the repository root, run `npm install` and `npm start` (Angular: `http://localhost:4201`).

## Deploy

- **Frontend:** Import this Git repository into Vercel. Framework preset: Angular (or Other); build command `npm run build`; output directory `dist/shameen/browser`; root directory is the repository root.
- **API:** Create the Render web service from `render.yaml` (Docker). Set `DATABASE_URL` to a PostgreSQL connection URI from Supabase or Render Postgres. Set `Cors__AllowedOrigins` to the exact deployed Vercel origin(s), comma-separated, for example `https://your-app.vercel.app`. The API uses the URI with TLS and runs the EF Core Postgres migrations on startup.
- **Vercel API routing:** `vercel.json` sends `/api/*` to `https://shameen-investments-api.onrender.com`. If Render assigns a different hostname, update the destination and redeploy Vercel. Keeping `/api` same-origin in the browser also supports the authentication cookie.
- `render.yaml` uses `sync:false` for secrets and allowed origins; enter these values in Render's dashboard. Never commit production database credentials.

## Database and existing local records

Production starts with the EF Core Postgres schema migration and seeds the configured destination cities and one branch for each city. No example employee accounts or default passwords are created in the hosted database. Create the first administrator through the application's first-run setup after deployment.

The current working database remains on SQL Server. Hosting the API on Postgres does not automatically copy SQL Server records. Before switching staff to the hosted service, perform and verify a migration of branches, employees, password hashes, operating days, transfers, and float adjustments to PostgreSQL, then confirm counts and balances. Keep the SQL Server database as a backup until reconciliation is complete. Do not copy operational records into a live database without an explicit, tested migration plan.

## Git safety

`.gitignore` excludes local environment files, build output, and .NET artifacts. Review `git status` before committing; never commit real database URLs, passwords, signing keys, or customer data.

### Copy the current SQL Server records to PostgreSQL

Do this once against an empty PostgreSQL database, after deploying the API so it has created the schema. From PowerShell in the repository root, set these process-only variables with your local SQL Server connection and the PostgreSQL URI from your host:

```powershell
$env:SHAMEEN_SQLSERVER_CONNECTION = 'Server=YOUR-SQL-SERVER;Database=ShameenInvestments;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;'
$env:DATABASE_URL = 'postgresql://USER:PASSWORD@HOST:5432/DATABASE?sslmode=require'
dotnet run --project backend/Shameen.DataMigration/Shameen.DataMigration.csproj
```

The migration tool prints source row counts, refuses a target that already has employees or operational records, and requires typing `IMPORT`. It replaces only the freshly seeded target branch rows and copies source branch, employee, operating day, transfer, and float adjustment IDs and values. Destination cities stay seeded by the API. The import runs in one PostgreSQL transaction. Verify counts and balances before pointing staff at production. Use a database backup and reconcile the import before live operation; this tool is intended for a one-time migration, not continuous synchronization.
