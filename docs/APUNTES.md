# Apuntes: ASP.NET Core Identity paso a paso

Notas del laboratorio **IdentityLab**: Identity agregado a mano en una app MVC con .NET 10, EF Core y PostgreSQL.

---

## Índice

1. [El proyecto](#1-el-proyecto)
2. [Paquetes NuGet](#2-paquetes-nuget)
3. [Herramienta dotnet-ef](#3-herramienta-dotnet-ef)
4. [Configuración y cadena de conexión](#4-configuración-y-cadena-de-conexión)
5. [ApplicationUser](#5-applicationuser)
6. [ApplicationDbContext](#6-applicationdbcontext)
7. [Program.cs: servicios y pipeline](#7-programcs-servicios-y-pipeline)
8. [Migraciones](#8-migraciones)
9. [Registro de usuarios](#9-registro-de-usuarios)
10. [Vistas y tag helpers](#10-vistas-y-tag-helpers)
11. [Login](#11-login)
12. [Configuración, secretos y variables de entorno](#12-configuración-secretos-y-variables-de-entorno)
13. [Confirmación de correo](#13-confirmación-de-correo)
14. [Git y commits](#14-git-y-commits)
15. [Comandos útiles](#15-comandos-útiles)
16. [Pendientes](#16-pendientes)

---

## 1. El proyecto

| Etapa | Proyecto | Autenticación |
|---|---|---|
| 1 y 2 | `IdentityLab.Mvc` | Cookies |
| 3 | `IdentityLab.Api` | JWT |

- Identity se agrega **a mano**: sin scaffolding y sin el paquete `Identity.UI`.
- Para crear controladores, usar **Add → Class** (clase vacía). La plantilla de controlador de Rider agrega el paquete `Microsoft.VisualStudio.Web.CodeGeneration.Design`, que no se necesita.

---

## 2. Paquetes NuGet

| Paquete | Para qué |
|---|---|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Conecta Identity con EF Core: `IdentityDbContext`, `UserStore`, `RoleStore` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **Proveedor** de EF Core para PostgreSQL. Traduce LINQ a SQL de PostgreSQL. Lo mantiene el equipo de Npgsql, no Microsoft |
| `Microsoft.EntityFrameworkCore.Design` | Solo en **tiempo de diseño**: lo necesita `dotnet ef` para crear migraciones. Lleva `<PrivateAssets>all</PrivateAssets>` |

**Ideas clave:**

- **Tener PostgreSQL instalado ≠ tener el proveedor.** El servidor guarda los datos; el proveedor es la librería .NET que habla con él.
- **La versión mayor debe coincidir** (todas 10.x). Npgsql numera sus parches por su cuenta (10.0.3 frente a 10.0.12 de Microsoft), y eso es normal.
- **`UserManager`, `SignInManager` y `RoleManager` no se instalan**: vienen en el framework compartido `Microsoft.AspNetCore.App`, incluido por `Sdk.Web`.
- **No instalar:**
  - `Microsoft.AspNetCore.Identity.UI`: trae páginas ya hechas, y aquí las construimos nosotros.
  - `Microsoft.EntityFrameworkCore.Tools`: solo sirve para la consola de paquetes de Visual Studio.

---

## 3. Herramienta dotnet-ef

Se instaló como **herramienta local**:

```bash
dotnet new tool-manifest      # crea dotnet-tools.json (si no existe)
dotnet tool install dotnet-ef
```

- Queda registrada en `dotnet-tools.json`, en la raíz del repo, con la versión fijada.
- Quien clone el repo ejecuta `dotnet tool restore` y obtiene la misma versión.
- Se usa como `dotnet ef ...` desde cualquier carpeta dentro del repo.
- `dotnet-tools.json` **se sube a Git**.

---

## 4. Configuración y cadena de conexión

En `appsettings.Development.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=identitylab;Username=postgres;Password=..."
}
```

| Parte | Significado |
|---|---|
| `Host=localhost` | Servidor en la máquina local |
| `Port=5432` | Puerto por defecto de PostgreSQL |
| `Database=identitylab` | La base **no hay que crearla a mano**: la crea `database update` |
| `Username` / `Password` | Credenciales locales |

**Orden de carga de la configuración:** primero `appsettings.json` y luego `appsettings.{Environment}.json`, que sobrescribe al anterior. El entorno `Development` viene de `launchSettings.json`.

La contraseña de PostgreSQL se dejó en `appsettings.Development.json` porque es solo local. Los secretos que sí importan (SMTP) van en User Secrets: ver la [sección 12](#12-configuración-secretos-y-variables-de-entorno).

---

## 5. ApplicationUser

```csharp
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}
```

**¿Por qué una clase propia?** Para poder agregar campos. Conviene crearla desde el inicio, porque cambiarla después obliga a tocar muchos sitios.

**Propiedades que trae `IdentityUser`:**

| Propiedad | Para qué |
|---|---|
| `Id` | GUID guardado como `string` |
| `UserName` / `Email` | Datos de login |
| `NormalizedUserName` / `NormalizedEmail` | En MAYÚSCULAS, para búsquedas sin distinguir mayúsculas |
| `PasswordHash` | Contraseña **hasheada**. Nunca se guarda en texto plano |
| `SecurityStamp` | Cambia al cambiar las credenciales y sirve para invalidar sesiones viejas |
| `ConcurrencyStamp` | Control de concurrencia |
| `EmailConfirmed`, `PhoneNumberConfirmed` | Confirmaciones |
| `LockoutEnabled`, `LockoutEnd`, `AccessFailedCount` | Bloqueo por intentos fallidos |
| `TwoFactorEnabled` | 2FA |

---

## 6. ApplicationDbContext

```csharp
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
}
```

- El **`DbContext`** representa la base de datos.
- Hereda de **`IdentityDbContext<ApplicationUser>`**, que ya trae las tablas de Identity. El `<ApplicationUser>` indica: "usa mi clase de usuario".
- Las `options` (proveedor y cadena de conexión) **no se definen aquí**: llegan desde `Program.cs` por inyección de dependencias.
- Equivalente con **constructor primario** (C# 12+):
  ```csharp
  public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
      : IdentityDbContext<ApplicationUser>(options) { }
  ```
- ⚠️ Si se sobrescribe `OnModelCreating`, **siempre llamar primero a `base.OnModelCreating(builder)`**. Si no, se pierde la configuración de las tablas de Identity.

---

## 7. Program.cs: servicios y pipeline

```
var builder = WebApplication.CreateBuilder(args);
// ─── ZONA 1: SERVICIOS ───  "qué piezas existen" (inyección de dependencias)
var app = builder.Build();
// ─── ZONA 2: PIPELINE ────  "por dónde pasa cada petición" (middlewares)
app.Run();
```

### Zona 1: servicios

```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
```

| Parte | Qué hace |
|---|---|
| `?? throw` | Si falta la cadena, la app falla al arrancar con un mensaje claro |
| `AddDbContext` + `UseNpgsql` | Registra el `DbContext` con PostgreSQL. Es **Scoped**: una instancia por petición |
| `AddIdentity<ApplicationUser, IdentityRole>()` | Registra `UserManager`, `SignInManager`, `RoleManager`, el hasher, los validadores **y la autenticación por cookies** |
| `.AddEntityFrameworkStores<ApplicationDbContext>()` | Usuarios y roles se guardan con EF Core en ese `DbContext` |
| `.AddDefaultTokenProviders()` | Tokens para resetear contraseña, confirmar email y 2FA |

**Variantes de registro:**

| Método | Cuándo usarlo |
|---|---|
| `AddDefaultIdentity` | Necesita `Identity.UI` (páginas ya hechas). Descartado en este laboratorio |
| `AddIdentity` | **App MVC con cookies** ✅ |
| `AddIdentityCore` | Mínimo, sin cookies. Útil para APIs con JWT (etapa 3) |

Por defecto, `AddIdentity` redirige a los usuarios no autenticados a **`/Account/Login`**.

### ¿Importa el orden?

| Zona | ¿Importa? | Por qué |
|---|---|---|
| Servicios | Casi nunca | Solo se **anotan** en una lista ("recetas"). Se crean después, cuando alguien los pide |
| Pipeline | **Sí, mucho** | Cada middleware se ejecuta en ese orden en cada petición |

**Excepciones en la zona de servicios:**
- Si registras el mismo servicio dos veces, **gana el último**.
- Los `Configure...` van **después** de lo que ajustan. Por ejemplo, `ConfigureApplicationCookie` va después de `AddIdentity`.

> Regla práctica: los `Add...` independientes, en cualquier orden. Los `Configure...`, después de lo que ajustan.

### Zona 2: pipeline

```csharp
app.UseRouting();          // 1. ¿Qué controlador/acción atiende la petición?
app.UseAuthentication();   // 2. ¿Quién eres? Lee la cookie y llena HttpContext.User
app.UseAuthorization();    // 3. ¿Puedes entrar? Revisa [Authorize]
```

⚠️ Si `UseAuthorization` va antes que `UseAuthentication`, todos los usuarios se ven como anónimos.

---

## 8. Migraciones

### Crear

```bash
dotnet ef migrations add InitialIdentity --project src/IdentityLab.Mvc --output-dir Data/Migrations
```

- `--output-dir Data/Migrations` deja las migraciones junto al `DbContext`.
- `migrations add` **no toca la base de datos**: solo genera código C#.

**Cómo nombrarlas:** en PascalCase y describiendo qué cambia, como un commit de la base de datos.
- ✅ `InitialIdentity`, `InitialCreate`, `AddFullNameToUser`
- ❌ `Migration1`, `Cambios`, `Fix`
- No hace falta poner la fecha: EF agrega un *timestamp* (`20261010223739_InitialIdentity`).

**Archivos generados** (los tres se suben a Git):

| Archivo | Qué es |
|---|---|
| `<timestamp>_InitialIdentity.cs` | `Up()` crea los cambios y `Down()` los deshace |
| `<timestamp>_InitialIdentity.Designer.cs` | Metadatos de EF. No se toca |
| `ApplicationDbContextModelSnapshot.cs` | "Foto" del modelo actual. En la próxima migración se compara con el código para saber qué cambió |

### Aplicar

```bash
dotnet ef migrations script --project src/IdentityLab.Mvc   # opcional: ver el SQL
dotnet ef database update --project src/IdentityLab.Mvc
```

`database update` se conecta con `DefaultConnection`, crea la base si no existe, crea la tabla `__EFMigrationsHistory` (donde anota las migraciones aplicadas) y ejecuta `Up()`.

### Tablas de Identity

| Tabla | Clave primaria | Para qué |
|---|---|---|
| `AspNetUsers` | `Id` | Usuarios (`ApplicationUser`) |
| `AspNetRoles` | `Id` | Roles |
| `AspNetUserRoles` | `UserId` + `RoleId` | Relación muchos a muchos usuario ↔ rol |
| `AspNetUserClaims` | `Id` (autoincremental) | Claims de cada usuario |
| `AspNetRoleClaims` | `Id` (autoincremental) | Claims de cada rol |
| `AspNetUserLogins` | `LoginProvider` + `ProviderKey` | Logins externos (Google, etc.) |
| `AspNetUserTokens` | `UserId` + `LoginProvider` + `Name` | Tokens (2FA, etc.) |
| `__EFMigrationsHistory` | `MigrationId` | La crea EF para saber qué migraciones se aplicaron |

### Tipos C# → PostgreSQL (los traduce Npgsql)

| C# | PostgreSQL |
|---|---|
| `string` | `text` |
| `string` + `MaxLength(256)` | `character varying(256)` |
| `bool` | `boolean` |
| `int` | `integer` |
| `DateTimeOffset?` | `timestamp with time zone` |
| `string?` | Columna `nullable: true` |

### Observaciones del esquema

- **Los IDs de usuario y rol son GUIDs en `text`**: no se pueden adivinar y se generan sin consultar la base.
- **Las claves foráneas usan `Cascade`**: al borrar un usuario, se borran sus claims, roles asignados, logins y tokens.
- **Las búsquedas usan las columnas `Normalized*`** (mayúsculas), así que no distinguen mayúsculas.
- **`UserName` es único y `Email` no lo es por defecto.** Se activa con `options.User.RequireUniqueEmail = true`, y lo valida Identity en el código, no la base de datos.
- **`Up()` crea primero las tablas padre; `Down()` borra primero las hijas.** Lo exigen las claves foráneas.

### Consultar en PostgreSQL

```sql
SELECT "Id", "UserName", "FullName", "PasswordHash" FROM "AspNetUsers";
SELECT "Email", "AccessFailedCount", "LockoutEnd" FROM "AspNetUsers";
```

⚠️ **Las comillas dobles son obligatorias.** PostgreSQL pasa a minúsculas los nombres sin comillas, y las tablas están en PascalCase.

---

## 9. Registro de usuarios

### Flujo

```
GET  /Account/Register  → formulario vacío
POST /Account/Register  → valida → crea usuario → inicia sesión → redirige a Home
                                 ↘ si falla → muestra el formulario con los errores
```

### ¿Por qué un ViewModel y no `ApplicationUser`?

- `ConfirmPassword` no existe en `ApplicationUser`.
- La contraseña nunca se guarda en texto plano.
- **Overposting:** con `ApplicationUser`, alguien podría enviar `EmailConfirmed=true` y el model binding lo aceptaría. Con un ViewModel **solo entran los campos definidos**.

### Data Annotations

| Atributo | Qué hace |
|---|---|
| `[Required]` | Campo obligatorio |
| `[EmailAddress]` | Formato de email. Genera `type="email"` |
| `[StringLength(100)]` | Longitud máxima |
| `[Compare(nameof(Password))]` | Debe ser igual a otra propiedad. `nameof` es más seguro que escribir el nombre como texto |
| `[DataType(DataType.Password)]` | Genera `type="password"` |
| `[Display(Name = "...")]` | Texto de la etiqueta |

Se validan **dos veces**: en el navegador (jQuery Validation, solo por comodidad) y **siempre en el servidor** (la que protege).

### Controlador

```csharp
public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
```

| Servicio | Responsabilidad |
|---|---|
| `UserManager` | **Gestionar usuarios**: crear, buscar, cambiar contraseña, roles… |
| `SignInManager` | **Gestionar sesiones**: iniciar sesión, cerrarla, verificar contraseña… |

Los entrega ASP.NET Core por inyección de dependencias, gracias a `AddIdentity`.

### Piezas del `POST Register`

- **`[HttpPost]`**: el método solo responde a envíos de formulario.
- **`[ValidateAntiForgeryToken]`**: protege contra **CSRF** (otro sitio enviando formularios con la sesión del usuario). El token lo agrega el tag helper `<form>`.
- **`if (!ModelState.IsValid) return View(model);`**: se devuelve el `model` para que el usuario no pierda lo que escribió.
- **`UserName = model.Email`**: Identity exige `UserName`, y usar el email simplifica el login.
- **`_userManager.CreateAsync(user, password)`**:
  1. Valida el usuario (`UserName` único y caracteres permitidos).
  2. Valida la contraseña. Por defecto: mínimo 6 caracteres, mayúscula, minúscula, dígito y símbolo.
  3. Hashea la contraseña con **PBKDF2 + sal aleatoria**.
  4. Rellena `Normalized*` y `SecurityStamp`.
  5. Inserta la fila en `AspNetUsers`.
  6. No lanza excepciones: devuelve un **`IdentityResult`** con `Succeeded` y `Errors`.
- **`_signInManager.SignInAsync(user, isPersistent: false)`**: crea la cookie `.AspNetCore.Identity.Application`.
- **`RedirectToAction("Index", "Home")`**: patrón **Post-Redirect-Get**. Si el usuario pulsa F5, no reenvía el formulario.
- **`ModelState.AddModelError(string.Empty, error.Description)`**: la clave vacía indica un error general. Se muestra en `asp-validation-summary="ModelOnly"`.

---

## 10. Vistas y tag helpers

**Convención:** `AccountController.Register()` + `return View()` busca `Views/Account/Register.cshtml`.

- **`@model RegisterViewModel`**: define el tipo del modelo. No necesita el namespace gracias a `_ViewImports.cshtml`.
- **`ViewData["Title"]`**: lo usa `_Layout.cshtml` en el `<title>`.

| Tag helper | Genera |
|---|---|
| `<form asp-action="Register" method="post">` | `action="/Account/Register"` + **`<input hidden name="__RequestVerificationToken">` automático** |
| `<form asp-route-returnUrl="...">` | Agrega `?returnUrl=...` a la URL del formulario (vale para cualquier `asp-route-xxx`) |
| `<label asp-for="X">` | `<label for="X">`, con el texto de `[Display]` |
| `<input asp-for="X">` | `id`, `name` (clave del *model binding*), `type` y los `data-val-*` de validación |
| `<input asp-for="UnBool">` | Checkbox **más un `<input hidden value="false">`**, porque un checkbox desmarcado no envía nada |
| `<span asp-validation-for="X">` | Error de ese campo |
| `<div asp-validation-summary="ModelOnly">` | Errores generales (clave `string.Empty`) |

| `asp-validation-summary` | Muestra |
|---|---|
| `ModelOnly` | Solo los errores generales ✅ |
| `All` | Todos (los de campo saldrían repetidos) |
| `None` | Ninguno |

**Otros detalles:**
- El `div` del resumen **siempre se renderiza**, aunque esté vacío. Por eso se usa `text-danger` y no `alert alert-danger`, que mostraría una caja roja vacía.
- **Los campos `type="password"` nunca se devuelven rellenos**, por seguridad.
- **`autocomplete`** ayuda a los gestores de contraseñas: `username`, `new-password` (registro) y `current-password` (login).
- **`@section Scripts { <partial name="_ValidationScriptsPartial" /> }`** carga jQuery Validation solo en las páginas con formularios. El layout lo inserta con `RenderSectionAsync("Scripts")`.
- **Las reglas de contraseña de Identity no las conoce el navegador** (no son Data Annotations): esos errores llegan desde el servidor.

---

## 11. Login

### ViewModel

`Email`, `Password` y `RememberMe` (un `bool` que se convierte en checkbox).

### Acciones

```csharp
[HttpGet]
public IActionResult Login(string? returnUrl = null)
{
    ViewData["ReturnUrl"] = returnUrl;
    return View();
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
{
    ViewData["ReturnUrl"] = returnUrl;
    if (!ModelState.IsValid) return View(model);

    var result = await _signInManager.PasswordSignInAsync(
        model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

    if (result.Succeeded)
    {
        if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    if (result.IsLockedOut) { /* "This account is locked out..." */ }

    ModelState.AddModelError(string.Empty, "Invalid login attempt.");   // ver sección 13: ahora incluye una pista
    return View(model);
}
```

### `PasswordSignInAsync`

| Parámetro | Significado |
|---|---|
| 1º | Es el **`UserName`**. Funciona con el email porque `UserName = Email` |
| 2º | Contraseña. Identity la hashea con la misma sal y **compara los hashes**. No descifra nada |
| 3º | `isPersistent` (`RememberMe`) |
| `lockoutOnFailure` | Cuenta los fallos y bloquea la cuenta |

Busca el usuario, verifica la contraseña, comprueba el bloqueo y crea la cookie.

### `SignInResult`

| Propiedad | Cuándo es `true` |
|---|---|
| `Succeeded` | Login correcto |
| `IsLockedOut` | La cuenta está bloqueada |
| `IsNotAllowed` | No puede iniciar sesión todavía (p. ej., falta confirmar el email) |
| `RequiresTwoFactor` | Contraseña correcta, pero falta el 2FA |

### RememberMe

| Valor | Cookie | Al cerrar el navegador |
|---|---|---|
| `false` | De sesión (sin `Expires`) | Se borra |
| `true` | Persistente (`Expires` ≈ 14 días) | Se mantiene |

### Lockout (fuerza bruta)

Por defecto, **5 intentos fallidos bloquean la cuenta 5 minutos**. `AccessFailedCount` sube con cada fallo; al llegar a 5 se rellena `LockoutEnd` y el contador vuelve a 0.

### Seguridad

- **Mensaje genérico** "Invalid login attempt.", tanto si el email no existe como si la contraseña es incorrecta. Evita la **enumeración de usuarios**. Con la confirmación de correo, este mismo mensaje cubre también `IsNotAllowed` ([sección 13](#13-confirmación-de-correo)).
- **`Url.IsLocalUrl(returnUrl)`** evita el **open redirect**. Sin esta comprobación, `?returnUrl=https://sitio-falso.com` mandaría al usuario a un sitio falso justo después de iniciar sesión en el real.
- `IsLocalUrl` tiene `[NotNullWhen(true)]`, por eso `Redirect(returnUrl)` no da advertencia de *nullable*.

### El recorrido de `returnUrl`

```
URL ?returnUrl=... → GET Login(returnUrl) → ViewData["ReturnUrl"]
   → <form asp-route-returnUrl> → POST Login(returnUrl) → Redirect
```

⚠️ **`ViewData` no persiste entre peticiones.** Si el `GET` no recibe `returnUrl` y no lo guarda en `ViewData`, la cadena se rompe y el `POST` recibe `null`.

---

## 12. Configuración, secretos y variables de entorno

### Fuentes de configuración

El código nunca pregunta de qué archivo viene un valor: pide una clave (`Smtp:Password`) y .NET la busca en varias fuentes. **Cada fuente sobrescribe solo las claves que define**, en este orden:

```
appsettings.json                   → siempre
appsettings.{Environment}.json     → solo en ese entorno
User Secrets                       → solo en Development
Variables de entorno               → siempre, de forma nativa
```

El entorno lo define **`ASPNETCORE_ENVIRONMENT`**. En local vale `Development` (por `launchSettings.json`). **Si no está definida, .NET asume `Production`**, y entonces no carga ni `appsettings.Development.json` ni User Secrets.

### Dónde va cada dato

| Tipo de dato | Dónde | Ejemplo |
|---|---|---|
| Igual en todos los entornos | `appsettings.json` | `Smtp:Host`, `Smtp:Port`, `Smtp:FromName` |
| Cambia según el entorno | `appsettings.{Entorno}.json` | Cadena de conexión local |
| Secreto o dato personal (desarrollo) | User Secrets | `Smtp:Password`, `Smtp:UserName`, `Smtp:FromAddress` |
| Secreto (Docker o producción) | Variables de entorno | `SMTP__PASSWORD` |

⚠️ Si una sección solo está en `appsettings.Development.json`, **no existirá en producción**.

### Secciones y la sintaxis `:`

El `:` crea una **jerarquía**: `Smtp:Password` es la propiedad `Password` dentro de la sección `Smtp`. Agrupar las claves en secciones permite mapear la sección entera a una clase C#.

```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "FromName": "IdentityLab"
}
```

- En JSON, **el último elemento de un objeto no lleva coma**. El lector de .NET tolera la coma final, pero otras herramientas no.

### User Secrets

Es la herramienta **propia de .NET** (viene con el SDK) para guardar secretos de desarrollo **fuera de la carpeta del proyecto**.

```bash
dotnet user-secrets init --project src/IdentityLab.Mvc                     # una sola vez
dotnet user-secrets set "Smtp:Password" "valor" --project src/IdentityLab.Mvc
dotnet user-secrets list --project src/IdentityLab.Mvc
dotnet user-secrets remove "Smtp:Password" --project src/IdentityLab.Mvc
```

- `init` agrega `<UserSecretsId>` al `.csproj`. **Es solo un identificador, no un secreto**: se sube a Git.
- Los valores se guardan en `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`.
- En Rider: clic derecho en el proyecto → **Tools → .NET User Secrets**. Hace el `init` y abre el `secrets.json`.
- `CreateBuilder` los carga automáticamente, **solo en `Development`**.
- **No están cifrados**: la protección consiste en que están fuera del repo.
- El `secrets.json` acepta formato plano (`"Smtp:Password": "..."`) o anidado (`"Smtp": { "Password": "..." }`). Son equivalentes.

### Compartir secretos con el equipo

User Secrets **no se comparte, a propósito**. De la mejor a la peor opción:

1. **Cada desarrollador usa sus propias credenciales.** En el README se documentan los **nombres** de las claves, nunca los valores.
2. **Si la credencial debe ser compartida**, se pasa con un **gestor de contraseñas** de equipo (Bitwarden, 1Password). ❌ Nunca por chat, correo ni en el repo.
3. **Gestor de secretos centralizado** (Azure Key Vault, AWS Secrets Manager, Doppler, Infisical). Útil en equipos grandes y en producción.
4. **Que en desarrollo no haga falta el secreto**, por ejemplo con un SMTP falso como Mailpit.

### Archivos `.env`

- .NET **no lee `.env` por sí solo**. Lo lee **`docker compose`**, que convierte cada línea en una variable de entorno del contenedor, y .NET lee esas variables de forma nativa.
- Van en la **raíz de la solución**, junto al futuro `docker-compose.yml`, porque compose los busca en su misma carpeta.
- **`.env`**: valores reales. Está en `.gitignore` y **no se sube**.
- **`.env.example`**: mismos nombres, sin valores reales. **Sí se sube**, para que el equipo sepa qué llenar.
- Formato: `NOMBRE=valor`, una por línea, **sin espacios** alrededor del `=` y sin comillas (salvo que el valor tenga espacios).

```
SMTP__USERNAME=
SMTP__PASSWORD=
SMTP__FROMADDRESS=
```

### Nombres de variables de entorno en .NET

1. **El `:` se escribe como doble guion bajo `__`.** El `__` no es estilo: es sintaxis y significa "entrar en una sección".
2. **Las claves no distinguen mayúsculas de minúsculas.** `Smtp__Password`, `SMTP__PASSWORD` y `smtp__password` son la misma clave. Por eso se puede seguir la convención clásica en MAYÚSCULAS.
3. **Un guion bajo simple es parte del nombre.** No separa palabras ni crea secciones.

| Variable | ¿Llega a `Smtp:FromAddress`? |
|---|---|
| `Smtp__FromAddress` | ✅ |
| `SMTP__FROMADDRESS` | ✅ Las mayúsculas no importan |
| `SMTP__FROM_ADDRESS` | ❌ Busca una propiedad `FROM_ADDRESS` |
| `SMTP_FROMADDRESS` | ❌ Un solo `_` no crea sección |

⚠️ Un nombre mal escrito **no da error**: el valor simplemente se ignora.

### Producción

"Producción" es el entorno donde la app corre para los usuarios reales, sea en Azure, otra nube, un VPS o un servidor interno. Allí no hay User Secrets: los secretos se configuran como **variables de entorno**, por ejemplo en la sección de configuración de Azure App Service. **El código no cambia.**

---

## 13. Confirmación de correo

### El mapa completo

```
┌─ 1. INFRAESTRUCTURA DE CORREO ─────────── "saber enviar correos"
│   Contraseña de aplicación de Gmail
│   User Secrets + appsettings.json  →  sección Smtp
│   SmtpSettings                     →  configuración como clase (Options pattern)
│   IEmailSender                     →  el contrato
│   SmtpEmailSender (MailKit)        →  la implementación
│   Program.cs                       →  Configure<SmtpSettings> + AddTransient
│
├─ 2. EL FLUJO DE CONFIRMACIÓN ─────────── "usar el correo para confirmar"
│   Register:  GenerateEmailConfirmationTokenAsync
│              → Base64UrlEncode → Url.Action → SendEmailAsync
│              → RedirectToAction("RegisterConfirmation")
│   RegisterConfirmation + vista     →  "Revisa tu correo"
│   ConfirmEmail:  FindByIdAsync → Base64UrlDecode → ConfirmEmailAsync
│   ConfirmEmail.cshtml              →  éxito o error
│
└─ 3. LA REGLA ─────────────────────────── "exigirlo"
    options.SignIn.RequireConfirmedEmail = true
    Mensaje genérico en Login (sin revelar IsNotAllowed)
```

El bloque 1 se hace **una sola vez**: el reset de contraseña reutilizará el mismo `IEmailSender`.

### Bloque 1: infraestructura de correo

#### SMTP con Gmail

- **Cualquier proveedor SMTP sirve** (Gmail, Outlook, Brevo, SendGrid…). El código es el mismo y solo cambia la configuración. Outlook personal puede exigir OAuth, así que Gmail es lo más simple para aprender.
- Gmail **no acepta la contraseña normal**. Necesita una **contraseña de aplicación**: activar la verificación en 2 pasos → myaccount.google.com/apppasswords → 16 caracteres, **sin espacios**.

| Dato | Valor |
|---|---|
| Host | `smtp.gmail.com` |
| Puerto | `587` con **STARTTLS** (empieza sin cifrar y luego activa TLS) |
| Usuario | El Gmail completo |
| Contraseña | La contraseña de aplicación |

- **`FromAddress` debe ser la misma cuenta que `UserName`.** Gmail no permite enviar como otra persona (protección contra suplantación) y reemplaza el remitente. Lo que el destinatario ve primero es el **`FromName`**.
- A diferencia de la contraseña local de PostgreSQL, una contraseña de aplicación filtrada permite **enviar correos en tu nombre desde cualquier lugar**. Por eso va en User Secrets ([sección 12](#12-configuración-secretos-y-variables-de-entorno)).

#### MailKit

- Librería de .NET para enviar (SMTP) y leer (IMAP/POP3) correos. Incluye **MimeKit**, que sirve para construir el mensaje.
- `System.Net.Mail.SmtpClient` existe, pero **Microsoft desaconseja usarlo** en código nuevo y recomienda MailKit.
- ⚠️ Hay **dos `SmtpClient`**. El correcto es **`MailKit.Net.Smtp.SmtpClient`**.

```bash
dotnet add src/IdentityLab.Mvc package MailKit
```

#### Options pattern: `SmtpSettings`

En lugar de leer `configuration["Smtp:Host"]` como texto en cada sitio (repetitivo, sin conversión de tipos y un error de escritura devuelve `null` en silencio), se mapea **la sección entera a una clase**:

```csharp
public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
}
```

```csharp
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
```

- Los nombres de las propiedades deben coincidir con las claves de la sección (sin importar mayúsculas).
- `GetSection("Smtp")` toma la sección **ya combinada** (appsettings + User Secrets + variables de entorno).
- Se recibe en el constructor como **`IOptions<SmtpSettings>`**. `.Value` devuelve el objeto lleno.
- `Port` no lleva `= string.Empty`: los números ya tienen valor por defecto (`0`).

#### La interfaz `IEmailSender`

```csharp
public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}
```

- Una interfaz es un **contrato**: dice **qué** se hace, no **cómo**.
- El controlador depende de `IEmailSender`, no de Gmail. Para cambiar de proveedor se crea otra clase y se cambia **una línea** en `Program.cs`. Es la **inversión de dependencias** (la "D" de SOLID).
- Es la misma idea que `UserManager` con los *stores*: no sabe que la base es PostgreSQL.

**En Clean Architecture:**

| Archivo | Capa | Por qué |
|---|---|---|
| `IEmailSender` | **Application** | Quien tiene la necesidad define el contrato |
| `SmtpEmailSender` | **Infrastructure** | Conoce los detalles técnicos (MailKit, SMTP) |
| `SmtpSettings` | **Infrastructure** | Es un detalle de conexión |

Las dependencias apuntan **hacia adentro**: Infrastructure conoce a Application, nunca al revés. Si la interfaz estuviera en Infrastructure, la flecha apuntaría hacia afuera.

#### La implementación `SmtpEmailSender`

```csharp
public async Task SendEmailAsync(string to, string subject, string htmlBody)
{
    var message = new MimeMessage();
    message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
    message.To.Add(MailboxAddress.Parse(to));
    message.Subject = subject;
    message.Body = new TextPart("html") { Text = htmlBody };

    using var client = new SmtpClient();
    await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
    await client.AuthenticateAsync(_settings.UserName, _settings.Password);
    await client.SendAsync(message);
    await client.DisconnectAsync(true);
}
```

| Paso | Qué hace |
|---|---|
| `MimeMessage` | El correo: remitente, destinatarios (son listas), asunto y cuerpo |
| `TextPart("html")` | Cuerpo en HTML, para que el enlace se pueda pulsar |
| `ConnectAsync(..., StartTls)` | Conecta al puerto 587 y activa el cifrado |
| `AuthenticateAsync` | Inicia sesión con la contraseña de aplicación |
| `SendAsync` | Entrega el correo |
| `DisconnectAsync(true)` | Cierra educadamente (envía `QUIT`) |
| `using var client` | Libera la conexión aunque haya un error |

#### Registro y ciclos de vida

```csharp
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
```

"Cuando alguien pida un `IEmailSender`, dale un `SmtpEmailSender`."

| Ciclo de vida | Instancias | Ejemplo |
|---|---|---|
| `Transient` | Una nueva **cada vez** que se pide | `SmtpEmailSender`: no guarda estado |
| `Scoped` | Una **por petición HTTP** | `DbContext` |
| `Singleton` | **Una** para toda la app | Cachés |

El orden de los registros no importa: Identity y el SMTP no dependen uno del otro, y aunque dependieran, las dependencias se resuelven al **crear** los objetos, no al anotarlos.

#### Probar cada pieza por separado

Antes de mezclar el correo con Identity, se probó **solo el envío** con una acción temporal:

```csharp
public async Task<IActionResult> TestEmail([FromServices] IEmailSender emailSender)
{
    await emailSender.SendEmailAsync("...", "IdentityLab test", "<h1>It works!</h1>");
    return Content("Email sent.");
}
```

- **`[FromServices]`** pide un servicio en el parámetro de una acción, en lugar de en el constructor.
- **`Content("...")`** devuelve texto plano sin vista.
- Si falla, se sabe que el problema es el SMTP: `AuthenticationException`/`535` indica credenciales, y un *timeout* indica host o puerto.
- ⚠️ **Se borra después**: cualquiera podría usar esa URL para enviar correos.

### Bloque 2: el flujo de confirmación

#### En `POST Register`

```csharp
if (result.Succeeded)
{
    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
    var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    var confirmationLink = Url.Action(
        "ConfirmEmail",
        "Account",
        new { userId = user.Id, code = encodedToken },
        Request.Scheme)
        ?? throw new InvalidOperationException("Could not generate the email confirmation link.");

    await _emailSender.SendEmailAsync(
        model.Email,
        "Confirm your email",
        $"<p>Please confirm your account by <a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">clicking here</a>.</p>");

    return RedirectToAction("RegisterConfirmation");
}
```

**El token** (`GenerateEmailConfirmationTokenAsync`):
- Lo genera el proveedor registrado con **`AddDefaultTokenProviders()`**.
- Contiene, **cifrado y firmado** con Data Protection: para quién es, para qué sirve, cuándo se creó y el `SecurityStamp`.
- **No se guarda en la base de datos.** Identity lo verifica descifrándolo; si se modifica una sola letra, la firma deja de coincidir.
- **Caduca en 1 día.** Si cambia el `SecurityStamp` (por ejemplo, al cambiar la contraseña), los tokens anteriores dejan de valer.

**Base64Url** (`WebEncoders.Base64UrlEncode`, en `Microsoft.AspNetCore.WebUtilities`):

| Carácter | Problema en una URL | En Base64Url |
|---|---|---|
| `+` | Se lee como espacio | `-` |
| `/` | Separa rutas | `_` |
| `=` | Separa clave y valor | Se elimina |

Al recibirlo hay que hacer la operación inversa (`Base64UrlDecode`).

**El enlace** (`Url.Action`):
- El 4º argumento, **`Request.Scheme`**, genera una URL **absoluta** (`https://dominio/...`). Sin él, la URL es relativa y **no sirve en un correo**, porque ahí no hay "sitio actual".
- Lleva `userId` y `code` porque `ConfirmEmailAsync` necesita el usuario y el token.
- **Devuelve `null` si la acción no existe**, sin dar error. Por eso se agrega el `?? throw`.

**El correo:**
- El controlador recibe `IEmailSender` por el constructor, sin saber que detrás hay Gmail.
- **`HtmlEncoder.Default.Encode(...)`** (en `System.Text.Encodings.Web`) evita que caracteres como `&` o `"` rompan el HTML. Es una buena costumbre siempre que se mete un valor en HTML construido a mano, y protege contra XSS.
- `\"` es una comilla escapada dentro del string.

**Ya no se inicia sesión al registrarse.** `SignInAsync` **no comprueba** la confirmación (solo lo hace `PasswordSignInAsync`), así que se reemplaza por una redirección a "Revisa tu correo". Se mantiene **Post-Redirect-Get**: si el usuario pulsa F5, no se registra ni se reenvía el correo.

#### `RegisterConfirmation`

Acción `GET` que solo devuelve una vista sin `@model`, con el mensaje "Check your email".

#### `ConfirmEmail`

```csharp
[HttpGet]
public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
{
    if (userId is null || code is null) return View(false);

    var user = await _userManager.FindByIdAsync(userId);
    if (user is null) return View(false);

    var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
    var result = await _userManager.ConfirmEmailAsync(user, token);

    return View(result.Succeeded);
}
```

- Los parámetros se llaman **igual** que en el `Url.Action`, para que el *model binding* los tome de la URL.
- **`ConfirmEmailAsync`** comprueba la firma, que el token sea para este usuario y para este propósito, la caducidad y el `SecurityStamp`. Si todo está bien, pone **`EmailConfirmed = true`**.
- **Todos los fallos muestran lo mismo**, para no dar pistas.
- Limitación: si el `code` no es Base64Url válido, `Base64UrlDecode` lanza una excepción y se ve una página de error. No es un problema de seguridad, porque no se confirma nada.

**¿Un `GET` que cambia datos?** Es una excepción aceptada:
- Un enlace de correo **solo puede hacer `GET`**.
- El riesgo del Logout (que otro sitio lo dispare con `<img src>`) no existe: hace falta el **token**, que solo llegó al correo.
- **Repetirlo no hace daño**: confirmar dos veces deja el mismo resultado.

#### La vista `ConfirmEmail.cshtml`

- **`@model bool`**: una vista puede recibir cualquier tipo, no solo un ViewModel.
- `@if (Model)` muestra el éxito (con enlace a Login) o un error genérico.

### Bloque 3: la regla

#### Opciones de Identity

```csharp
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
```

`options =>` es una **lambda** que recibe todas las opciones de Identity:

| Grupo | Ejemplos |
|---|---|
| `options.SignIn` | Exigir el email o el teléfono confirmados |
| `options.Password` | Longitud mínima, mayúsculas, símbolos… |
| `options.Lockout` | Intentos fallidos y duración del bloqueo |
| `options.User` | `RequireUniqueEmail`, caracteres permitidos |

`RequireConfirmedAccount` hace lo mismo con la configuración por defecto, pero es más genérica: permite definir qué significa "cuenta confirmada". `RequireConfirmedEmail` dice exactamente lo que queremos.

#### Dos estrategias para limitar a los no confirmados

| Estrategia | Cómo | Complejidad |
|---|---|---|
| **No puede iniciar sesión** hasta confirmar ✅ (la elegida) | `RequireConfirmedEmail` + `IsNotAllowed` | Baja |
| **Puede entrar, pero algunas acciones están bloqueadas** | Políticas de autorización + claims | Más avanzada |

#### El orden interno de `PasswordSignInAsync` y la enumeración

```
1. ¿Existe el usuario?          → si no: Failed
2. ¿Puede iniciar sesión?       → si no confirmó: IsNotAllowed   ← ANTES de mirar la contraseña
3. ¿Está bloqueado?             → IsLockedOut                     ← también antes
4. ¿La contraseña es correcta?  → si no: Failed
```

Como **`IsNotAllowed` se devuelve aunque la contraseña sea incorrecta**, un mensaje específico ("confirma tu correo") revelaría qué cuentas existen probando con contraseñas inventadas.

❌ **Error cometido:** agregar `if (result.IsNotAllowed)` con un mensaje distinto **y mantener** el genérico al final. Aunque los textos se parezcan, **que haya dos mensajes distintos ya filtra información**: basta con compararlos.

✅ **Solución:** un único mensaje para todos los fallos, que oriente sin revelar:

```csharp
ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
```

> **Regla:** si dos situaciones no deben poder distinguirse, tienen que producir **exactamente la misma respuesta**: mismo texto, misma página y mismo código HTTP.

El mensaje de `IsLockedOut` tiene el mismo problema. Es un compromiso que muchas apps aceptan, porque avisar del bloqueo es útil.

### Cómo probarlo

1. Registrarse → aparece "Check your email" y **no** hay sesión iniciada.
2. Intentar iniciar sesión antes de confirmar → mensaje genérico con la pista.
3. Pulsar el enlace del correo → "Thank you for confirming your email".
4. Comprobar en la base:
   ```sql
   SELECT "Email", "EmailConfirmed" FROM "AspNetUsers";
   ```
5. Abrir el enlace con una letra del `code` cambiada → no confirma nada.
6. Iniciar sesión después de confirmar → ✅

---

## 14. Git y commits

### Conventional Commits

| Prefijo | Uso |
|---|---|
| `chore:` | Preparar el entorno: plantilla, paquetes, configuración |
| `feat:` | Funcionalidad o código propio del proyecto |
| `docs:` | Cambios que solo afectan a la documentación |

- **Primera línea**: corta (unos 50 caracteres), en imperativo ("add", no "added").
- **Cuerpo**: lista de qué incluye el commit.

### Estados de `git status --short`

| Estado | Significado |
|---|---|
| `??` | Archivo sin seguimiento |
| `A` | Añadido al índice |
| `M` | Modificado |
| `AM` | Añadido al índice **y modificado después**. Hay que hacer `git add` de nuevo o se guarda la versión vieja |

### Commits del laboratorio

1. `chore: scaffold MVC project and solution`
2. `chore: add Identity and PostgreSQL packages`
3. `feat: add ApplicationUser and ApplicationDbContext`
4. `feat: register DbContext and Identity services`
5. `feat: add initial Identity migration`
6. `feat: add user registration`
7. `feat: add user login`
8. `feat: add logout and fix login returnUrl`
9. `chore: configure SMTP settings and secrets`
10. `feat: add SMTP email sender with MailKit`
11. `feat: add email confirmation`

---

## 15. Comandos útiles

```bash
# Paquetes
dotnet add package <Nombre>
dotnet remove <proyecto> package <Nombre>

# Herramientas locales
dotnet tool restore
dotnet ef --version

# Migraciones
dotnet ef migrations add <Nombre> --project src/IdentityLab.Mvc --output-dir Data/Migrations
dotnet ef migrations script --project src/IdentityLab.Mvc
dotnet ef database update --project src/IdentityLab.Mvc

# User Secrets
dotnet user-secrets set "<Seccion:Clave>" "<valor>" --project src/IdentityLab.Mvc
dotnet user-secrets list --project src/IdentityLab.Mvc

# Compilar y ejecutar
dotnet build
dotnet run --project src/IdentityLab.Mvc
```

**Rider:** `Ctrl+Alt+L` reformatea el código (espacios y líneas vacías sobrantes).

---

## 16. Pendientes

- [x] Corregir `GET Login` para que reciba `returnUrl` y lo guarde en `ViewData`.
- [x] **Logout**: acción `POST` con `SignOutAsync()`.
- [x] Mostrar en el navbar si hay sesión iniciada (directamente en `_Layout.cshtml`, con `User.Identity?.IsAuthenticated`).
- [x] **Confirmación de correo**: SMTP con Gmail, MailKit, `IEmailSender`, token de confirmación y `RequireConfirmedEmail`.
- [ ] Resolver los usuarios antiguos con `EmailConfirmed = false` (ya no pueden iniciar sesión).
- [ ] (Opcional) Reenviar el correo de confirmación.
- [ ] (Opcional) Manejar un `code` que no sea Base64Url válido en `ConfirmEmail`.
- [ ] Reset de contraseña (reutiliza `IEmailSender`, con `GeneratePasswordResetTokenAsync`).
- [ ] Ajustar las opciones de Identity: reglas de contraseña y `RequireUniqueEmail`.
- [ ] `ConfigureApplicationCookie`, después de `AddIdentity`.
- [ ] Proteger páginas con `[Authorize]`.
- [ ] Etapa 3: `IdentityLab.Api` con `AddIdentityCore` + JWT.
- [x] Crear `.env` y `.env.example` en la raíz.
- [ ] Dockerizar: `Dockerfile` y `docker-compose.yml` que lean el `.env`.
