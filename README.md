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

3. Run the MVC app:

   ```bash
   dotnet run --project src/IdentityLab.Mvc
   ```