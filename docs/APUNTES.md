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
12. [Git y commits](#12-git-y-commits)
13. [Comandos útiles](#13-comandos-útiles)
14. [Pendientes](#14-pendientes)

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

**User Secrets** (`dotnet user-secrets`) es la forma de guardar secretos fuera del repositorio. En este laboratorio no se usa por ahora, porque la contraseña es solo local.

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

    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
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

- **Mensaje genérico** "Invalid login attempt.", tanto si el email no existe como si la contraseña es incorrecta. Evita la **enumeración de usuarios**.
- **`Url.IsLocalUrl(returnUrl)`** evita el **open redirect**. Sin esta comprobación, `?returnUrl=https://sitio-falso.com` mandaría al usuario a un sitio falso justo después de iniciar sesión en el real.
- `IsLocalUrl` tiene `[NotNullWhen(true)]`, por eso `Redirect(returnUrl)` no da advertencia de *nullable*.

### El recorrido de `returnUrl`

```
URL ?returnUrl=... → GET Login(returnUrl) → ViewData["ReturnUrl"]
   → <form asp-route-returnUrl> → POST Login(returnUrl) → Redirect
```

⚠️ **`ViewData` no persiste entre peticiones.** Si el `GET` no recibe `returnUrl` y no lo guarda en `ViewData`, la cadena se rompe y el `POST` recibe `null`.

---

## 12. Git y commits

### Conventional Commits

| Prefijo | Uso |
|---|---|
| `chore:` | Preparar el entorno: plantilla, paquetes, configuración |
| `feat:` | Funcionalidad o código propio del proyecto |

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

---

## 13. Comandos útiles

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

# Compilar y ejecutar
dotnet build
dotnet run --project src/IdentityLab.Mvc
```

**Rider:** `Ctrl+Alt+L` reformatea el código (espacios y líneas vacías sobrantes).

---

## 14. Pendientes

- [ ] Corregir `GET Login` para que reciba `returnUrl` y lo guarde en `ViewData`.
- [ ] **Logout**: acción `POST` con `SignOutAsync()`.
- [ ] (Opcional) Mostrar en el navbar si hay sesión iniciada (`_LoginPartial`, `@inject SignInManager`).
- [ ] Ajustar las opciones de Identity: reglas de contraseña y `RequireUniqueEmail`.
- [ ] `ConfigureApplicationCookie`, después de `AddIdentity`.
- [ ] Proteger páginas con `[Authorize]`.
- [ ] Etapa 3: `IdentityLab.Api` con `AddIdentityCore` + JWT.
