# IdentityLab

Hands-on lab for learning ASP.NET Core Identity, from cookie-based auth in MVC to JWT in a Web API.

Identity is added **manually** (no template scaffolding, no default UI) so every piece is written and understood step by step.

## Tech stack

- .NET 10
- ASP.NET Core MVC
- ASP.NET Core Identity
- Entity Framework Core
- PostgreSQL (via Npgsql)

## Project structure

```
IdentityLab/
├── src/
│   ├── IdentityLab.Mvc/   # Stages 1 and 2: cookie-based auth
│   └── IdentityLab.Api/   # Stage 3: JWT auth
├── .env.example           # Environment variables template (copy to .env)
├── dotnet-tools.json      # Local tools (dotnet-ef)
├── IdentityLab.slnx
└── README.md
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL running locally

### Setup

1. Restore the local tools (installs `dotnet-ef` from `dotnet-tools.json`):

   ```bash
   dotnet tool restore
   ```

2. Adjust the connection string in `src/IdentityLab.Mvc/appsettings.Development.json` to match your local PostgreSQL user and password.

3. Configure the SMTP secrets with [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (each developer uses their own account; for Gmail, `Smtp:Password` is an [app password](https://myaccount.google.com/apppasswords)):

   ```bash
   dotnet user-secrets set "Smtp:UserName" "<your-email>" --project src/IdentityLab.Mvc
   dotnet user-secrets set "Smtp:Password" "<your-app-password>" --project src/IdentityLab.Mvc
   dotnet user-secrets set "Smtp:FromAddress" "<your-email>" --project src/IdentityLab.Mvc
   ```

   Non-secret SMTP settings (`Host`, `Port`, `FromName`) live in `appsettings.json`.

4. Configure the Google sign-in secrets. Each developer creates their own OAuth client in [Google Cloud Console](https://console.cloud.google.com/):

   1. Create a project and configure the OAuth consent screen (audience: **External**). While the app is in **Testing** mode, add your Google account as a **test user**, or Google will deny access.
   2. Create an **OAuth client ID** of type **Web application** with these **authorized redirect URIs**:

      ```
      https://localhost:7083/signin-google
      http://localhost:5097/signin-google
      ```

   3. Copy the client secret right away: Google only shows it once.

   ```bash
   dotnet user-secrets set "Authentication:Google:ClientId" "<your-client-id>" --project src/IdentityLab.Mvc
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<your-client-secret>" --project src/IdentityLab.Mvc
   ```

   For Docker, copy `.env.example` to `.env` and fill in the same values as environment variables (`SMTP__USERNAME`, `AUTHENTICATION__GOOGLE__CLIENTID`, ...). In .NET, `__` maps to the `:` section separator and keys are case-insensitive.

5. Apply the migrations (creates the `identitylab` database and its tables):

   ```bash
   dotnet ef database update --project src/IdentityLab.Mvc
   ```

6. Run the MVC app:

   ```bash
   dotnet run --project src/IdentityLab.Mvc
   ```

## Database schema

`ApplicationDbContext` inherits from `IdentityDbContext<ApplicationUser>`, which brings the Identity tables below. Migrations live in `src/IdentityLab.Mvc/Data/Migrations`.

| Table | Primary key | Purpose |
|---|---|---|
| `AspNetUsers` | `Id` | Users (`ApplicationUser`): credentials, email, lockout and 2FA state, plus custom fields like `FullName` |
| `AspNetRoles` | `Id` | Roles (e.g. `Admin`, `User`) |
| `AspNetUserRoles` | `UserId` + `RoleId` | Many-to-many link between users and roles |
| `AspNetUserClaims` | `Id` (identity) | Claims assigned directly to a user |
| `AspNetRoleClaims` | `Id` (identity) | Claims assigned to a role, inherited by its users |
| `AspNetUserLogins` | `LoginProvider` + `ProviderKey` | External logins (Google, Microsoft, ...) linked to a user |
| `AspNetUserTokens` | `UserId` + `LoginProvider` + `Name` | Tokens stored for a user (e.g. 2FA authenticator key) |
| `__EFMigrationsHistory` | `MigrationId` | Created by EF Core to track which migrations have been applied |

Notes:

- **User and role IDs are GUIDs stored as `text`**, not auto-incrementing numbers, so they can't be guessed and are generated without hitting the database.
- **Foreign keys cascade on delete**: deleting a user also deletes their claims, role assignments, logins and tokens.
- **Lookups use the `Normalized*` columns** (uppercase), so searches are case-insensitive.
- **`UserName` is unique, `Email` is not** by default. Unique emails can be enabled with `options.User.RequireUniqueEmail = true`, which Identity validates in code, not the database.