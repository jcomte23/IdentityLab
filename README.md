# IdentityLab

Hands-on lab for learning ASP.NET Core Identity, from cookie-based auth in MVC to JWT in a Web API.

Identity is added **manually** (no template scaffolding, no default UI) so every piece is written and understood step by step.

## Tech stack

- .NET 10
- ASP.NET Core MVC
- ASP.NET Core Identity
- Entity Framework Core
- SQLite

## Project structure

```
IdentityLab/
├── src/
│   ├── IdentityLab.Mvc/   # Stages 1 and 2: cookie-based auth
│   └── IdentityLab.Api/   # Stage 3: JWT auth
├── IdentityLab.slnx
└── README.md
```