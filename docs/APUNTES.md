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
14. [Recuperación de contraseña](#14-recuperación-de-contraseña)
15. [Login con terceros (Google)](#15-login-con-terceros-google)
16. [Panel de cuenta y `[Authorize]`](#16-panel-de-cuenta-y-authorize)
17. [Autenticación en dos pasos (2FA)](#17-autenticación-en-dos-pasos-2fa)
18. [Git y commits](#18-git-y-commits)
19. [Comandos útiles](#19-comandos-útiles)
20. [Pendientes](#20-pendientes)

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
public sealed class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string? FullName { get; set; }

    public ApplicationUser(string? fullName, string userName, string email) : base(userName)
    {
        Email = email;
        FullName = fullName;
    }
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

### ¿Qué exige Identity para crear un usuario?

**Solo el `UserName`**: obligatorio, único y con caracteres permitidos (letras y números ASCII y `-._@+`).

| Dato | ¿Obligatorio? |
|---|---|
| `UserName` | ✅ Siempre |
| Contraseña | Depende del método: `CreateAsync(user, password)` la exige y la valida; **`CreateAsync(user)` crea el usuario sin contraseña** (`PasswordHash = null`) |
| `Email` | ❌ Opcional por defecto. Con `options.User.RequireUniqueEmail = true` pasa a ser **obligatorio, válido y único** |
| `Id`, `Normalized*`, `SecurityStamp`, `ConcurrencyStamp` | Los genera Identity |
| `FullName` | Es tuyo: obligatorio u opcional según lo declares (`string` o `string?`) |

**Usuarios sin contraseña:** no pueden entrar con `PasswordSignInAsync` (devuelve `Failed`). Sirven para el login con terceros, para invitaciones (el usuario elige la contraseña después) y para el login sin contraseña.

| Método | Para qué |
|---|---|
| `HasPasswordAsync(user)` | ¿Tiene contraseña? |
| `AddPasswordAsync(user, password)` | Ponerle una contraseña a quien no tiene (falla si ya tiene) |
| `ChangePasswordAsync(user, actual, nueva)` | Cambiarla conociendo la actual |
| `RemovePasswordAsync(user)` | Quitarla |

### El constructor

**¿Por qué el email no va en `base(...)`?** `base(...)` no sirve para "pasarle datos al padre" en general: **llama a un constructor que ya existe en `IdentityUser`**, y solo hay dos:

```csharp
public IdentityUser() { }
public IdentityUser(string userName) { }
```

`: base(userName, email)` **no compila** (`CS1729: does not contain a constructor that takes 2 arguments`).

| Dato | ¿Existe la propiedad en `IdentityUser`? | ¿Hay un constructor del padre que la reciba? | Cómo se asigna |
|---|---|---|---|
| `UserName` | ✅ | ✅ | `: base(userName)` |
| `Email` | ✅ (heredada) | ❌ | `Email = email;` en el cuerpo |
| `FullName` | ❌ (es tuya) | ❌ | Se declara **y** se asigna |

**Heredar la propiedad ≠ recibir el valor.** La herencia da "la caja vacía"; el valor de cada usuario viene del formulario y alguien tiene que meterlo.

**¿Constructor o inicializador `{ }`?** Es una decisión de diseño:

| | Constructor | Inicializador `{ }` |
|---|---|---|
| ¿Se puede olvidar un dato? | ❌ El compilador lo exige | ✅ Sí, y no avisa |
| Flexibilidad | Menos | Más |

Se pueden **combinar**: `new ApplicationUser(...) { EmailConfirmed = true }`.

**EF Core y los constructores con parámetros:** EF puede usarlos si **los nombres de los parámetros coinciden con propiedades** (`userName` → `UserName`). Se comprueba con `dotnet ef migrations has-pending-model-changes`, que construye el modelo.

⚠️ **Cuidado con varios parámetros del mismo tipo.** Al cambiar el orden del constructor a `(fullName, userName, email)`, la llamada vieja `new ApplicationUser(model.UserName, model.Email, model.FullName)` **siguió compilando** (los tres son `string`), pero mezclaba los datos. La defensa son los **argumentos con nombre**:

```csharp
new ApplicationUser(fullName: model.FullName, userName: model.UserName, email: model.Email);
```

### Avisos de Rider

**"Possible performance issues caused by unlimited string length"** (en `FullName`):
- Un `string` sin límite se convierte en una columna sin tamaño máximo (`text`).
- El aviso piensa en **SQL Server**, donde `nvarchar(max)` es más lento y no se puede indexar. **En PostgreSQL, `text` y `varchar(n)` rinden igual.**
- Aun así conviene limitarlo, por **integridad de datos**: el formulario limita a 100, pero la base aceptaba cualquier longitud.
- **`[MaxLength(100)]`** en la entidad (EF lo usa para el tamaño de la columna); **`[StringLength]`** en los ViewModels (valida formularios).
- Cambia el tipo de la columna, así que **necesita una migración** (`AlterColumn` en lugar de `CreateTable`).

**"Virtual member call in constructor"** (en `Email = email;`):
- `Email` es **`virtual`** en `IdentityUser`: una clase hija podría redefinirla con `override`.
- El constructor del padre se ejecuta **antes** que el de la hija. Si una hija redefiniera `Email` usando sus propios campos, se ejecutaría su código **antes de que la hija esté inicializada** (por ejemplo, un `_log` que todavía es `null`).
- **Solución: `sealed`**, para que nadie pueda heredar de `ApplicationUser`.
- ⚠️ `sealed` es incompatible con el **lazy loading con proxies** de EF Core, que crea clases hijas de las entidades. No se usa en este proyecto.

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
| `[RegularExpression(@"^[a-zA-Z0-9._-]+$")]` | Debe cumplir una expresión regular: de principio (`^`) a fin (`$`), uno o más (`+`) caracteres de la lista |
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
- **El username lo elige el usuario.** Al principio se usaba `UserName = model.Email`; después se agregó un campo `UserName` al formulario:
  ```csharp
  [Required]
  [StringLength(50, MinimumLength = 3)]
  [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "...")]
  [Display(Name = "Username")]
  public string UserName { get; set; } = string.Empty;
  ```
  - **Sin `@`**: como el login acepta email o username, un username con `@` se buscaría como email y nunca podría entrar.
  - Son **los mismos caracteres** que usa `GenerateUniqueUserNameAsync` para los usuarios de Google.
  - ⚠️ Al separar `UserName` de `Email` **se rompió el login con email**: ver [sección 11](#11-login).
  - `autocomplete="username"` en el campo de username y `autocomplete="email"` en el de email.
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

`EmailOrUserName`, `Password` y `RememberMe` (un `bool` que se convierte en checkbox).

```csharp
[Required]
[Display(Name = "Email or username")]
public string EmailOrUserName { get; set; } = string.Empty;
```

Sin `[EmailAddress]`, porque un username no tiene formato de email. Al principio el campo se llamaba `Email`.

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

    var user = model.EmailOrUserName.Contains('@')
        ? await _userManager.FindByEmailAsync(model.EmailOrUserName)
        : await _userManager.FindByNameAsync(model.EmailOrUserName);

    if (user is null)
    {
        ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
        return View(model);
    }

    var result = await _signInManager.PasswordSignInAsync(
        user, model.Password, model.RememberMe, lockoutOnFailure: true);

    if (result.Succeeded)
    {
        if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    if (result.IsLockedOut) { /* "This account is locked out..." */ }

    ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
    return View(model);
}
```

### Iniciar sesión con email **o** username

**El problema:** la sobrecarga `PasswordSignInAsync(string userName, ...)` busca por **`UserName`**. Funcionaba con el email solo porque al principio `UserName = Email`. Al pedir un username en el registro, **los usuarios nuevos ya no podían entrar con su email**.

**La solución:** buscar primero al usuario y usar la sobrecarga que recibe **el usuario**:

```
"ana@gmail.com" → tiene @ → FindByEmailAsync ┐
"ana23"         → sin @   → FindByNameAsync  ┴→ PasswordSignInAsync(user, password, ...)
```

- **`? :`** es el **operador ternario**: `condición ? siEsVerdad : siEsFalso`.
- **Si el usuario no existe, el mensaje es exactamente el mismo** que el de una contraseña incorrecta (regla de la misma respuesta).
- El username **no puede tener `@`** (`[RegularExpression]` en el registro). Si no, se buscaría como email.
- **`options.User.RequireUniqueEmail = true`**: si se entra con el email, no puede haber dos usuarios con el mismo. Además, **`FindByEmailAsync` lanza una excepción si encuentra más de uno**. Antes de activarlo se comprueba que no haya duplicados:
  ```sql
  SELECT "NormalizedEmail", COUNT(*) FROM "AspNetUsers" GROUP BY "NormalizedEmail" HAVING COUNT(*) > 1;
  ```
- El commit fue un **`fix:`**: corrige algo que estaba roto.

### `PasswordSignInAsync`

| Sobrecarga | 1.er parámetro |
|---|---|
| `PasswordSignInAsync(string userName, ...)` | Un texto que se busca como **`UserName`** |
| `PasswordSignInAsync(TUser user, ...)` ✅ (la que se usa) | El **usuario** ya encontrado |

| Parámetro | Significado |
|---|---|
| `password` | Identity la hashea con la misma sal y **compara los hashes**. No descifra nada |
| `isPersistent` | `RememberMe` |
| `lockoutOnFailure` | Cuenta los fallos y bloquea la cuenta |

Verifica la contraseña, comprueba la confirmación y el bloqueo, y crea la cookie.

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

- **Mensaje genérico** "Invalid login attempt…", tanto si el usuario no existe como si la contraseña es incorrecta. Evita la **enumeración de usuarios**. Con la confirmación de correo, este mismo mensaje cubre también `IsNotAllowed` ([sección 13](#13-confirmación-de-correo)).
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

## 14. Recuperación de contraseña

Reutiliza **toda la infraestructura de correo** de la sección 13. Solo cambian el tipo de token y los formularios.

### El flujo

```
1. Login → "Forgot your password?"
2. GET  ForgotPassword  → formulario con el email
3. POST ForgotPassword  → si existe y confirmó: token + enlace + correo
                        → SIEMPRE redirige a ForgotPasswordConfirmation
4. GET  ResetPassword?code=...  → formulario: email + contraseña nueva + Code oculto
5. POST ResetPassword   → ResetPasswordAsync(user, token, newPassword)
                        → ResetPasswordConfirmation → Login
```

### Piezas

| Pieza | Qué es |
|---|---|
| `ForgotPasswordViewModel` | Solo `Email` |
| `ResetPasswordViewModel` | `Email`, `Password`, `ConfirmPassword` y **`Code`** |
| `ForgotPassword` (GET/POST) | Pedir el enlace |
| `ForgotPasswordConfirmation` | "If an account with that email exists, we've sent you a link…" |
| `ResetPassword` (GET/POST) | Poner la contraseña nueva |
| `ResetPasswordConfirmation` | "Your password has been reset." + enlace a Login |

### `POST ForgotPassword`

```csharp
var user = await _userManager.FindByEmailAsync(model.Email);
if (user is null || !await _userManager.IsEmailConfirmedAsync(user))
{
    return RedirectToAction("ForgotPasswordConfirmation");   // sin correo
}

var token = await _userManager.GeneratePasswordResetTokenAsync(user);
// Base64UrlEncode + Url.Action("ResetPassword", ..., new { code }, Request.Scheme) ?? throw
// + SendEmailAsync con HtmlEncoder (igual que en la confirmación)

return RedirectToAction("ForgotPasswordConfirmation");       // con correo
```

- **Las dos salidas llevan a la misma página**: exista o no la cuenta, la respuesta es idéntica. Por eso la vista `ForgotPassword` **no tiene `asp-validation-summary`**: nunca hay errores del servidor que mostrar.
- **Solo se envía a correos confirmados**: direcciones que el usuario ya demostró que son suyas.
- **El enlace solo lleva `code`**, no el `userId`. El email se pide en el formulario, así que la URL no expone datos del usuario.
- Mientras `ResetPassword` no existía, `Url.Action` devolvía `null` y saltaba el `?? throw`. **Para eso está.**
- ⚠️ **Fuga por tiempo**: si la cuenta existe, la respuesta tarda más (se conecta a Gmail). En apps reales se envían los correos **en segundo plano**, con una cola.

### `GET ResetPassword`

```csharp
public IActionResult ResetPassword(string? code)
{
    if (code is null) return BadRequest("A code must be supplied for password reset.");
    return View(new ResetPasswordViewModel { Code = code });
}
```

- **`BadRequest`** devuelve un **400** (petición mal formada).
- Se pasa un **modelo ya relleno** a un formulario vacío, para guardar el `Code` en la página.
- En la vista, **`<input asp-for="Code" type="hidden" />`** lleva el token hasta el `POST`. Es la misma idea que el `returnUrl`: cada petición empieza de cero.

```
Enlace ?code=...  →  GET: Code en el modelo  →  <input hidden>  →  POST: model.Code
```

### `POST ResetPassword`

```csharp
var user = await _userManager.FindByEmailAsync(model.Email);
if (user is null)
{
    ModelState.AddModelError(string.Empty, _userManager.ErrorDescriber.InvalidToken().Description);
    return View(model);
}

var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
var result = await _userManager.ResetPasswordAsync(user, token, model.Password);

if (result.Succeeded) return RedirectToAction("ResetPasswordConfirmation");

foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
return View(model);
```

**`ResetPasswordAsync`, en orden:**
1. Valida el token (firma, usuario, propósito, caducidad y `SecurityStamp`). Si falla: **"Invalid token."**
2. Valida la contraseña nueva con las reglas de Identity.
3. Hashea y guarda la contraseña.
4. **Cambia el `SecurityStamp`**, y con ello:
   - **El enlace solo funciona una vez**, porque el token contenía el stamp viejo.
   - **Se invalidan las sesiones en otros dispositivos.** Identity revisa el stamp de la cookie cada 30 minutos por defecto.

**El caso `user is null` y la regla de la misma respuesta:**

Muchos ejemplos (incluida la plantilla de Microsoft) redirigen a la página de éxito cuando el usuario no existe. Pero eso filtra información:

| Prueba del atacante | Redirigir a éxito | Nuestra versión |
|---|---|---|
| Email que **no existe** + código inventado | "Contraseña cambiada" | "Invalid token." |
| Email que **existe** + código inventado | "Invalid token." | "Invalid token." |

- **`_userManager.ErrorDescriber.InvalidToken().Description`** usa el **mismo texto** que Identity, en lugar de escribirlo a mano. Si Identity lo cambia o lo traduces, los dos casos siguen siendo idénticos.
- **Los errores de las reglas de contraseña no filtran nada**: solo aparecen si el token ya era válido (el paso 1 va antes que el 2). Por eso esta vista **sí tiene `asp-validation-summary`**.

### Cómo probarlo

| # | Prueba | Resultado esperado |
|---|---|---|
| 1 | Pedir el reset, abrir el enlace y poner una contraseña válida | "Your password has been reset." |
| 2 | Iniciar sesión con la contraseña vieja | ❌ |
| 3 | Iniciar sesión con la nueva | ✅ |
| 4 | Reutilizar el mismo enlace | "Invalid token." (solo sirve una vez) |
| 5 | Enlace nuevo con contraseña débil | Errores de las reglas de contraseña |
| 6 | Enlace nuevo con **otro** email | "Invalid token." |
| 7 | Pedir el reset con un email que no existe | La misma página "Check your email" |

---

## 15. Login con terceros (Google)

### La idea

Con el login con terceros, **tu app no verifica la contraseña**: se lo delega al proveedor (Google, Microsoft, GitHub, Apple…), que confirma quién es el usuario y le devuelve sus datos. Detrás hay dos estándares:

- **OAuth 2.0**: el protocolo para que una app reciba permisos de otra.
- **OpenID Connect**: una capa sobre OAuth que añade "quién es el usuario".

### El orden lógico (para cualquier proveedor)

```
1. Registrar la app en el proveedor   → Client ID + Client Secret + redirect URIs
2. Guardar las llaves                 → User Secrets / .env
3. Paquete NuGet + configuración      → AddGoogle / AddMicrosoftAccount / ...
4. Las acciones                       → ExternalLogin + ExternalLoginCallback
5. La interfaz                        → el botón
```

El botón es solo interfaz: **no funciona hasta que existen los pasos 1 a 4**. Mientras tanto, se dejó desactivado (`disabled`).

### Los "drivers" de cada proveedor

Igual que con las bases de datos (un paquete para PostgreSQL y otro para MySQL), **cada proveedor tiene su propio paquete**, y tu código cambia muy poco entre uno y otro.

| | Laravel | .NET |
|---|---|---|
| **Oficiales** | Socialite | Microsoft: **Google, Microsoft, Facebook y X**, un paquete cada uno |
| **Comunidad** | Socialite Providers | **`AspNet.Security.OAuth.Providers`**: GitHub, Discord, Apple, LinkedIn… (un paquete por proveedor) |
| **Genérico** | — | **`AddOAuth`** y **`AddOpenIdConnect`**: para cualquier proveedor que siga el estándar y no tenga paquete |

**Dónde se registra la app en cada proveedor:**

| Proveedor | Plataforma | Detalle |
|---|---|---|
| Google | Google Cloud Console | — |
| Microsoft | Microsoft Entra → *App registrations* | Gratis |
| GitHub | *Settings → Developer settings → OAuth Apps* | El más sencillo |
| Apple | Apple Developer | ⚠️ Requiere la membresía de pago, y el "secret" es un token firmado con una llave privada |

### Paso 1: Google Cloud Console

1. **Crear un proyecto** (`IdentityLab`).
2. **Pantalla de consentimiento** (*OAuth consent screen* o *Google Auth Platform*): nombre de la app, email de soporte y audiencia **External**.
   - ⚠️ En modo **Testing**, **solo pueden entrar los *test users***. Hay que agregar la propia cuenta; si no, Google devuelve "access denied".
   - Email, nombre e id vienen por defecto y **no requieren que Google revise la app**.
3. **Crear un OAuth client ID** de tipo **Web application**, con las **redirect URIs**:
   ```
   https://localhost:7083/signin-google
   http://localhost:5097/signin-google
   ```
4. ⚠️ **Copiar el Client Secret en ese momento**: Google ya no lo vuelve a mostrar.

**La redirect URI:**
- Es **a dónde devuelve Google al usuario** después del login.
- `/signin-google` es la ruta que el paquete de Google **atiende por defecto**. No hay que crear ninguna acción con ese nombre.
- Google **solo** redirige a las URIs registradas. Es una protección: nadie puede hacer que los datos del usuario vayan a otro sitio.
- Debe coincidir **exactamente** (protocolo, puerto y ruta). Si no, aparece **`redirect_uri_mismatch`**, el error más común.

| Dato | ¿Es secreto? |
|---|---|
| Client ID | No del todo: viaja visible en la URL hacia Google |
| **Client Secret** | **Sí**: con él, otro podría hacerse pasar por tu app |

### Paso 2: las llaves

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "..." --project src/IdentityLab.Mvc
dotnet user-secrets set "Authentication:Google:ClientSecret" "..." --project src/IdentityLab.Mvc
```

- `Authentication:<Proveedor>:...` es la convención de Microsoft.
- En `.env` / `.env.example`: **`AUTHENTICATION__GOOGLE__CLIENTID`** y **`AUTHENTICATION__GOOGLE__CLIENTSECRET`** (un `__` por cada `:`, sin `_` en medio de `CLIENTID`).
- Están documentadas en el README: cada colaborador crea su propio cliente de OAuth.

### Paso 3: el paquete y `AddGoogle`

```bash
dotnet add src/IdentityLab.Mvc package Microsoft.AspNetCore.Authentication.Google
```

Es un paquete oficial de Microsoft, así que su versión sigue la de ASP.NET Core (10.x).

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException("Google ClientId not found.");
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException("Google ClientSecret not found.");
    });
```

- **`AddIdentity` ya llama a `AddAuthentication` por dentro.** Llamarlo otra vez sin argumentos solo devuelve el constructor para **agregar esquemas**, sin cambiar lo que configuró Identity.
- Va **debajo de `AddIdentity`**, porque se apoya en su configuración.
- `AddGoogle` trae **su propia clase de opciones** (el `options` de la lambda), así que no hace falta una clase como `SmtpSettings`.

**Los esquemas de autenticación:**

| Esquema | Lo registra | Para qué |
|---|---|---|
| `Identity.Application` | `AddIdentity` | La cookie de **la sesión** |
| `Identity.External` | `AddIdentity` | Una cookie **temporal** con los datos que devuelve el proveedor |
| `Google` | `AddGoogle` | Redirigir a Google, atender `/signin-google`, canjear el código por los datos y guardarlos en `Identity.External` |

**⚠️ El `?? throw` dentro de una lambda se ejecuta más tarde.** Se comprobó en ejecución:

| `?? throw` | ¿Cuándo se ejecuta? | Si falta el valor |
|---|---|---|
| Cadena de conexión (fuera de una lambda) | Al arrancar | La app **no arranca** |
| Llaves de Google (dentro de `AddGoogle(options => ...)`) | En la primera petición | La app arranca, pero **todas las páginas dan 500** |

Pasa en **todas** las páginas porque el esquema de Google revisa cada petición para saber si es la vuelta desde Google.

### Paso 4a: `ExternalLogin`, enviar al usuario al proveedor

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult ExternalLogin(string provider, string? returnUrl = null)
{
    var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
    var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    return Challenge(properties, provider);
}
```

- **Es genérico: sirve para cualquier proveedor.** La palabra "Google" no aparece: `provider` es el **nombre del esquema** y llega desde el botón. Por eso se llama `ExternalLogin` y no `ExternalLoginGoogle`.
- `ConfigureExternalAuthenticationProperties` prepara un "paquete" con la URL de vuelta, que viaja **cifrado** en el parámetro **`state`**. Ese `state` también protege contra CSRF.
- `Challenge` le pide al esquema que autentique, y el esquema responde con un **302 hacia Google**.
- Es **`POST`** por lo mismo que el Logout: nadie puede dispararlo desde otro sitio con un enlace.
- Si alguien envía un `provider` que no existe, `Challenge` da **500**. Se puede validar antes con `GetExternalAuthenticationSchemesAsync()`.

**El recorrido completo:**

```
1. POST /Account/ExternalLogin  (provider=Google)                     ← tu código
2. 302 → accounts.google.com/...?client_id=...&redirect_uri=...&state=...
3.       El usuario inicia sesión en Google y acepta
4. 302 → /signin-google?code=...&state=...
5.       El paquete canjea el "code" por los datos → cookie Identity.External
6. 302 → /Account/ExternalLoginCallback?returnUrl=...                 ← tu código
```

**Lo único específico de cada proveedor:** la línea `AddGoogle` / `AddMicrosoftAccount` en `Program.cs` y el `asp-route-provider` del botón. Las acciones no cambian. Lo que sí varía son **los datos que envía cada uno**: GitHub, por ejemplo, puede no enviar el email.

### El botón

```cshtml
<form asp-action="ExternalLogin" asp-route-provider="Google" asp-route-returnUrl="@ViewData["ReturnUrl"]" method="post">
    <button type="submit" class="...">
        <svg>...logo de Google...</svg>
        <span>Continue with Google</span>
    </button>
</form>
```

- ❌ **Error cometido:** dejar `type="button"`. Con `type="button"`, **el botón no hace nada**. Tiene que ser **`type="submit"`** para enviar el formulario.
- Es un formulario **aparte** del de login o registro: en HTML no se pueden anidar formularios.
- Está en **Login y en Register**. En Login también envía el `returnUrl`.
- En apps con varios proveedores, los botones se generan con `SignInManager.GetExternalAuthenticationSchemesAsync()` en lugar de escribirlos a mano.

### Lo que devuelve Google

`GetExternalLoginInfoAsync()` lee la cookie temporal `Identity.External`:

| Dato | Qué es |
|---|---|
| `info.LoginProvider` | `"Google"` |
| `info.ProviderKey` | **El id del usuario en Google** (el `sub` de OpenID Connect). **Nunca cambia**, aunque cambie el email. No es secreto |
| `info.Principal` | Los **claims**: `ClaimTypes.Email`, `ClaimTypes.Name`… (se leen con `FindFirstValue`) |

**La app identifica a los usuarios externos por `LoginProvider` + `ProviderKey`, no por el email.** Es la clave primaria de **`AspNetUserLogins`**:

```
LoginProvider | ProviderKey            | UserId
Google        | 112987718249908652938  | (el usuario de la app)
```

Al volver de Google **todavía no hay sesión**: los datos solo están en la cookie temporal, que dura unos minutos.

### Paso 4b: `ExternalLoginCallback`

```
¿Hay en AspNetUserLogins una fila (Google, ProviderKey)?
├─ Sí → ExternalLoginSignInAsync → sesión iniciada ✅
└─ No → ¿El proveedor dio el email?                     no → error
        ¿Ya existe una cuenta local con ese email?        sí → "Log in with your password"
        Crear el usuario (sin contraseña, EmailConfirmed = true, username generado)
        → AddLoginAsync (vincular) → ExternalLoginSignInAsync ✅
```

**1. Leer los datos:** si `GetExternalLoginInfoAsync()` devuelve `null` (alguien abrió la URL directamente o la cookie caducó), se vuelve al Login con un mensaje. **`View("Login", new LoginViewModel())`** muestra la vista **de otra acción**, así que no hace falta crear una vista de error; también hay que poner `ViewData["ReturnUrl"]`.

**2. `ExternalLoginSignInAsync(provider, providerKey, isPersistent, bypassTwoFactor)`** es el equivalente de `PasswordSignInAsync`:

| `PasswordSignInAsync` | `ExternalLoginSignInAsync` |
|---|---|
| Busca por `UserName` | Busca en **`AspNetUserLogins`** |
| Verifica la contraseña | No hay contraseña: el proveedor ya lo hizo |
| Comprueba confirmación y bloqueo | **Igual** (`IsNotAllowed`, `IsLockedOut`) |
| Crea la cookie de sesión | **Igual**, y además **borra la cookie temporal** |

- **`RequireConfirmedEmail` también se aplica aquí.** Un usuario externo con `EmailConfirmed = false` recibe `IsNotAllowed`.
- `bypassTwoFactor: true`: no pide el 2FA de la app al entrar con el proveedor.
- **`Failed` significa "no hay ningún usuario vinculado"**: es el caso en que se crea el usuario.
- Aquí la enumeración de usuarios no es un problema: la persona **ya demostró ante Google** que es dueña de la cuenta.

**3. ¿Hay email?** Google siempre lo envía, pero el método es genérico y otros proveedores podrían no hacerlo.

**4. No vincular automáticamente por email.** Si ya existe una cuenta local con ese email, se le pide entrar con su contraseña (el campo aparece relleno con `new LoginViewModel { Email = email }`).

> Por qué: un atacante podría registrarse **antes** con el email de la víctima (sin confirmarlo). Si al entrar con Google se vinculara automáticamente, **el Google de la víctima quedaría conectado a la cuenta del atacante**, que seguiría entrando con su contraseña. La vinculación segura se hace **desde "Mi cuenta", con la sesión iniciada**.

**5. Crear el usuario:**

```csharp
var user = new ApplicationUser(
    fullName: info.Principal.FindFirstValue(ClaimTypes.Name),
    userName: await GenerateUniqueUserNameAsync(email),
    email: email)
{
    EmailConfirmed = true
};

var createResult = await _userManager.CreateAsync(user);   // sin contraseña
```

- Se combinan el **constructor** (lo obligatorio) y el **inicializador** (`EmailConfirmed`), con **argumentos con nombre** para no depender del orden.
- **`EmailConfirmed = true`**: el proveedor ya verificó el email. ⚠️ Solo si el proveedor **lo garantiza**: Google sí, GitHub no siempre. Si no, se envía el correo de confirmación como en un registro normal.
- **`CreateAsync(user)` sin contraseña** deja `PasswordHash = null`: solo podrá entrar con el proveedor.

**6. `AddLoginAsync(user, info)`** inserta la fila en `AspNetUserLogins`. A partir de ahí, `ExternalLoginSignInAsync` devuelve `Succeeded`.

**7. Si la vinculación falla, se borra el usuario** (`DeleteAsync`). Si no, quedaría un usuario sin contraseña y sin proveedor que nadie podría usar. Con `createResult = loginResult;`, un solo `foreach` muestra el error de cualquiera de los dos pasos.

❌ **Error cometido:** no copiar el bloque final (`foreach` de errores + `return View("Login", ...)`). El compilador dio **`CS0161: no todas las rutas de acceso de código devuelven un valor`**: un método que devuelve `IActionResult` debe terminar en un `return` **en todos sus caminos**.

### Generar un username único

Google no da un username, y la app lo exige. Se decidió **generarlo** (más adelante el usuario podrá cambiarlo):

```csharp
private async Task<string> GenerateUniqueUserNameAsync(string email)
{
    var localPart = email.Split('@')[0];
    var baseName = new string(localPart
        .Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
        .ToArray());

    if (baseName.Length < 3) baseName = "user" + baseName;

    var userName = baseName;
    var suffix = 1;
    while (await _userManager.FindByNameAsync(userName) is not null)
    {
        userName = $"{baseName}{suffix}";
        suffix++;
    }

    return userName;
}
```

```
javiercombita2014@gmail.com → javiercombita2014 → ¿existe? → javiercombita20141 ✅
```

- Identity solo permite en el `UserName` letras y números **ASCII** y `-._@+`.
- ⚠️ **`char.IsAsciiLetterOrDigit`**, no `char.IsLetterOrDigit`: el segundo acepta `á` o `ñ`, que Identity **rechaza**.
- Mínimo 3 caracteres, como en el `RegisterViewModel`.
- **`private`**: todo método `public` de un controlador es una **acción** accesible por URL. Los métodos de apoyo deben ser privados.

### Probarlo y limpiar

```sql
SELECT u."Id", u."UserName", u."Email", u."PasswordHash" IS NULL AS "SinPassword", l."LoginProvider"
FROM "AspNetUsers" u
LEFT JOIN "AspNetUserLogins" l ON l."UserId" = u."Id"
ORDER BY u."Email";
```

- Para repetir la prueba hay que **borrar el usuario de la base**: `DELETE FROM "AspNetUsers" WHERE "Id" = '...';`. Hay que **cerrar sesión antes**.
- **No hace falta borrar nada más**: por el `onDelete: Cascade` de la migración, PostgreSQL borra solo su fila en `AspNetUserLogins`, sus roles, claims y tokens.
- ⚠️ Borrar por `Email` puede borrar **varios** usuarios, porque el email no es único por defecto. Más seguro por `Id`.
- Para volver a ver la pantalla de permisos de Google: *myaccount.google.com → Seguridad → Conexiones con apps de terceros → IdentityLab → Eliminar*.

### Qué se guarda de un usuario de Google

| Tabla | ¿Se guarda? | Qué |
|---|---|---|
| `AspNetUsers` | ✅ | El usuario: username generado, email y nombre de Google, `EmailConfirmed = true` y **`PasswordHash = NULL`** |
| `AspNetUserLogins` | ✅ | La vinculación (`Google`, `ProviderKey`, `UserId`) |
| `AspNetUserTokens` | ❌ | Google envía tokens de acceso, pero la app **los descarta**. Solo se guardarían con `options.SaveTokens = true` y `UpdateExternalAuthenticationTokensAsync`, que hace falta para **usar las APIs de Google** en nombre del usuario (calendario, Drive…) |
| `AspNetUserClaims` / `AspNetUserRoles` | ❌ | Los claims de Google se usan una vez para crear el usuario |
| La contraseña de Google | ❌ **Nunca** | La app no la ve en ningún momento |

### Registrarse con el formulario y luego entrar con Google (mismo email)

```
1. ExternalLoginSignInAsync → no hay fila en AspNetUserLogins → Failed
2. FindByEmailAsync → ¡existe la cuenta del formulario!
3. "An account with this email already exists. Log in with your password." → no crea nada
```

- **No se crean dos usuarios.** Hay dos protecciones: la comprobación del callback y `RequireUniqueEmail`.
- **Tampoco se puede entrar con Google**, porque no se vincula automáticamente.
- Las dos formas de permitirlo:

| Opción | Cómo | Seguridad |
|---|---|---|
| **A. Vincular desde "Mi cuenta"** | Con la sesión iniciada, "Conectar con Google" | La más segura: el usuario ya demostró quién es |
| **B. Vincular automáticamente si `EmailConfirmed = true`** | El riesgo venía de cuentas **sin confirmar**. Si la cuenta local confirmó el email, quien la creó controla ese correo, igual que Google | Compromiso entre comodidad y seguridad; muchas apps lo hacen |

---

## 16. Panel de cuenta y `[Authorize]`

### `[Authorize]`

Atributo que significa **"aquí solo entra quien haya iniciado sesión"**. Puesto sobre la **clase**, protege **todas** sus acciones (también las que se agreguen después).

```csharp
[Authorize]
public class ManageController : Controller { ... }
```

```
GET /Manage sin sesión
→ UseAuthorization() ve [Authorize] y el usuario es anónimo
→ 302 a /Account/Login?ReturnUrl=%2FManage     ← lo hace Identity solo
→ tras iniciar sesión, el Login devuelve al usuario a /Manage
```

- **Aquí se ve funcionar el `returnUrl` sin escribirlo a mano**: todo lo que se construyó en el Login era para esto.
- `UseAuthentication()` responde "¿quién eres?" y `UseAuthorization()` aplica el `[Authorize]`. Por eso van en ese orden.
- La ruta del Login (`/Account/Login`) es la que usa `AddIdentity` por defecto.

### Dos controladores

| Controlador | Para quién | Qué contiene |
|---|---|---|
| `AccountController` | Usuarios **sin** sesión | Registro, login, confirmación, reset, login con Google y segundo paso del 2FA |
| **`ManageController`** | Usuarios **con** sesión (`[Authorize]`) | Perfil, seguridad, 2FA, códigos de recuperación, eliminar la cuenta |

### El usuario actual

```csharp
var user = await _userManager.GetUserAsync(User);
if (user is null)
{
    // La cookie es válida, pero el usuario ya no existe (p. ej., se borró de la base).
    await _signInManager.SignOutAsync();
    return RedirectToAction("Login", "Account");
}
```

- `User` es el `ClaimsPrincipal` de la cookie. `GetUserAsync` busca **su fila completa** en la base a partir del `Id` guardado en la cookie.

| Método de `UserManager` | Qué devuelve |
|---|---|
| `GetUserAsync(User)` | La fila completa del usuario de la cookie |
| `HasPasswordAsync(user)` | Si tiene contraseña |
| `GetTwoFactorEnabledAsync(user)` | Si tiene el 2FA activado |
| `CountRecoveryCodesAsync(user)` | Cuántos códigos de recuperación le quedan |
| `GetLoginsAsync(user)` | Sus filas de `AspNetUserLogins` |

### La página "My account" (`/Manage`)

- **Encabezado**: inicial, nombre completo (o username) y `@username`.
- **Profile**: nombre, username y email con la etiqueta *Verified* o *Not verified*.
- **Security**: contraseña (*Set* / *Not set*), 2FA (*Set up* / *Disable*), códigos de recuperación (cuántos quedan y *Generate new*) y logins vinculados.
- **Delete account**, en una tarjeta roja aparte.
- Se usa un **ViewModel** (`ManageIndexViewModel`) y no `ApplicationUser`, para que la vista reciba **solo lo que muestra** y nunca el `PasswordHash` o el `SecurityStamp`.
- En el `_Layout.cshtml`, el menú del usuario tiene el enlace **"My account"**.

> En Razor, `@@` escribe una `@` literal: `@@@Model.UserName` muestra `@jcombita`.

### `TempData`: mensajes que sobreviven a una redirección

```csharp
TempData["StatusMessage"] = "Two-factor authentication is now enabled.";
return RedirectToAction("Index");
```

```cshtml
@if (TempData["StatusMessage"] is string statusMessage)
{
    <div class="alert alert-success alert-dismissible fade show">@statusMessage ...</div>
}
```

| | ¿Sobrevive a una redirección? |
|---|---|
| `ViewData` | ❌ Cada petición empieza de cero |
| **`TempData`** | ✅ Se guarda (por defecto, en una cookie) **hasta que alguien lo lee**, y después se borra |

Es ideal para Post-Redirect-Get: el `POST` deja el mensaje y la página siguiente lo muestra **una sola vez**. También sirve para pasar arrays de textos, como los códigos de recuperación.

### Eliminar la cuenta

```
Panel → "Delete account" → página de confirmación
  ├─ con contraseña   → hay que escribirla (CheckPasswordAsync)
  └─ sin contraseña (Google) → hay que escribir el username
→ DeleteAsync(user) → SignOutAsync() → Home
```

- **Pedir la contraseña** evita que alguien que encuentre la sesión abierta borre la cuenta.
- **Se vuelve a comprobar en el servidor** si la cuenta tiene contraseña (`HasPasswordAsync`), sin fiarse del campo `RequirePassword` del formulario, que podría manipularse.
- **`CheckPasswordAsync(user, password)`** verifica una contraseña **sin iniciar sesión** ni sumar fallos al bloqueo.
- **`DeleteAsync`** + **borrado en cascada**: se van también sus logins, la clave del 2FA y los códigos de recuperación.

### Depurar un login que no funciona

Caso real: el email estaba confirmado y la contraseña "era la correcta", pero no se podía entrar.

```sql
SELECT "UserName", "Email", "EmailConfirmed", "PasswordHash" IS NOT NULL AS "TienePassword",
       "LockoutEnd", "AccessFailedCount", "TwoFactorEnabled"
FROM "AspNetUsers";
```

- **`AccessFailedCount` solo sube cuando el usuario se encuentra y la contraseña no coincide.** Con `4`, el usuario se encontraba bien y fallaba **la contraseña** (en este caso, había cambiado en las pruebas del reset).
- ⚠️ Con **5 fallos** la cuenta se bloquea 5 minutos.
- Identity escribe en la consola: `warn: Microsoft.AspNetCore.Identity.SignInManager ... User failed to provide the correct password.`
- Si la app la ejecuta Rider, puede seguir corriendo **una versión vieja** del código: hay que detenerla y volver a ejecutarla.
- Para desbloquear a mano:
  ```sql
  UPDATE "AspNetUsers" SET "AccessFailedCount" = 0, "LockoutEnd" = NULL WHERE "UserName" = '...';
  ```

---

## 17. Autenticación en dos pasos (2FA)

### La idea

| Factor | Ejemplo | Si te lo roban… |
|---|---|---|
| Algo que **sabes** | La contraseña | …falta el otro factor |
| Algo que **tienes** | El celular con la app | …falta la contraseña |

### Cómo funcionan los códigos (TOTP)

```
ACTIVACIÓN (una vez): la app genera una CLAVE SECRETA → (QR) → el celular la guarda
CADA LOGIN:  celular  → código = f(clave, hora) → 482 913
             servidor → código = f(clave, hora) → 482 913   ¿Coinciden? ✅
```

- **TOTP** = *Time-based One-Time Password*. El código cambia **cada 30 segundos**.
- **El celular y el servidor nunca se comunican**: cada uno lo calcula con la misma clave y la misma hora. Por eso importa que el reloj del celular esté bien.

### ¿Cuándo se aplica?

**Al iniciar sesión**, pero antes el usuario tiene que **activarlo**, y **no al registrarse**:

```
FASE 1: ACTIVARLO (una vez, con la sesión iniciada)
  "My account" → "Set up" → escanear el QR → escribir un código → TwoFactorEnabled = true
FASE 2: USARLO (en cada login)
  contraseña ✅ → código de la app ✅ → sesión iniciada
```

- Al registrarse, el usuario aún no ha demostrado nada, y activarlo exige la sesión iniciada, porque la clave se asocia a **su** usuario.

| Modelo | Cómo |
|---|---|
| **Opcional** (el implementado) | Cada usuario lo activa si quiere |
| **Obligatorio** | Tras el primer login, se obliga a configurarlo antes de usar la app (bancos, paneles de administración) |

- **`bypassTwoFactor: true`** en el callback de Google: al entrar con Google **no se pide el código**, porque Google ya aplica su propia seguridad.

### Lo que Identity ya trae

| Pieza | Dónde |
|---|---|
| Generador y verificador TOTP | `AddDefaultTokenProviders()` |
| La clave secreta | `AspNetUserTokens` → `[AspNetUserStore]` / `AuthenticatorKey` |
| Los códigos de recuperación | `AspNetUserTokens` → `[AspNetUserStore]` / `RecoveryCodes` |
| `TwoFactorEnabled` | `AspNetUsers` |

### Paso 1: la clave secreta y el QR (`GET /Manage/EnableAuthenticator`)

```csharp
var key = await _userManager.GetAuthenticatorKeyAsync(user);
if (string.IsNullOrEmpty(key))
{
    await _userManager.ResetAuthenticatorKeyAsync(user);   // genera y guarda una clave nueva
    key = await _userManager.GetAuthenticatorKeyAsync(user);
}
```

- Si el usuario ya tiene clave, se **reutiliza**: recargar la página no la cambia.
- `FormatKey` la muestra en grupos de 4 y en minúsculas (`jbsw y3dp ehpk 3pxp`), más fácil de escribir a mano.
- `key!`: el `!` le dice al compilador "aquí no es `null`".

**El QR** es solo **un texto con formato estándar** convertido en imagen:

```
otpauth://totp/IdentityLab:javier%40gmail.com?secret=JBSWY3DP...&issuer=IdentityLab&digits=6
```

| Parte | Significado |
|---|---|
| `otpauth://totp/` | "Cuenta TOTP para una app autenticadora" |
| `IdentityLab:email` | El nombre que se ve en la app |
| `secret=` | **La clave secreta** |
| `issuer=` | Quién emite la cuenta |
| `digits=6` | Códigos de 6 dígitos |

```csharp
using var generator = new QRCodeGenerator();
using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
var png = new PngByteQRCode(data).GetGraphic(6);
return $"data:image/png;base64,{Convert.ToBase64String(png)}";
```

- Paquete **QRCoder** (MIT). `PngByteQRCode` funciona igual en Windows, Linux y Docker.
- **Data URI**: la imagen va **incrustada en el HTML** (`<img src="data:image/png;base64,...">`), sin guardar archivos.
- `Uri.EscapeDataString` convierte la `@` del email en `%40`.
- ⚠️ **Se genera en el servidor** para que **la clave nunca salga de la app**. Usar un servicio web de QR o una librería cargada desde internet significaría enviar la clave a un tercero.
- La clave escrita sigue disponible como alternativa ("Can't scan it?"), plegada en un `<details>`.

### Paso 2: verificar el primer código y activar (`POST /Manage/EnableAuthenticator`)

```csharp
var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
var isValid = await _userManager.VerifyTwoFactorTokenAsync(
    user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

await _userManager.SetTwoFactorEnabledAsync(user, true);
await _signInManager.RefreshSignInAsync(user);
```

- Se limpian espacios y guiones, porque algunas apps muestran `123 456`.
- `VerifyTwoFactorTokenAsync` acepta también el código **anterior y el siguiente** a los 30 segundos actuales, por si el reloj está algo desfasado.
- **Pedir un código antes de activarlo** confirma que la app quedó bien configurada. Si no, el usuario podría quedarse fuera.
- ⚠️ **`RefreshSignInAsync`**: activar el 2FA **cambia el `SecurityStamp`**, y la cookie lleva el viejo. Sin renovarla, Identity detectaría la diferencia (cada ~30 minutos) y **cerraría la sesión**.
- Si el código es incorrecto, hay que **volver a cargar la clave y el QR** (no se envían en el formulario).
- `inputmode="numeric"` muestra el teclado numérico en el celular, y `autocomplete="one-time-code"` permite que el teclado sugiera el código.

⚠️ **Lección:** al activar el 2FA **antes de que el Login supiera pedir el código**, cerrar sesión dejaba al usuario fuera (el Login caía en "Invalid login attempt"). Para salir del paso: `UPDATE "AspNetUsers" SET "TwoFactorEnabled" = false WHERE ...`.

### Paso 3: el login en dos pasos

```csharp
// En POST Login:
if (result.RequiresTwoFactor)
{
    return RedirectToAction("LoginWith2fa", new { returnUrl, rememberMe = model.RememberMe });
}
```

```
1. Contraseña ✅ → PasswordSignInAsync → RequiresTwoFactor
   → NO crea la cookie de sesión: crea una cookie TEMPORAL (Identity.TwoFactorUserId)
     que dice "este usuario ya pasó la contraseña"
2. /Account/LoginWith2fa → código ✅
3. TwoFactorAuthenticatorSignInAsync → cookie de sesión de verdad
```

| Método | Qué hace |
|---|---|
| `GetTwoFactorAuthenticationUserAsync()` | Lee la cookie temporal. `null` si no existe o caducó → volver al Login |
| `TwoFactorAuthenticatorSignInAsync(code, rememberMe, rememberMachine)` | Verifica el código, **cuenta los fallos para el bloqueo** y crea la cookie de sesión |

- Se pasan el `returnUrl` y el *Remember me* del primer paso para no perderlos.
- Los códigos incorrectos **también suman al bloqueo**: así nadie puede probar el millón de códigos posibles.
- `RequiresTwoFactor` solo aparece **después** de comprobar la contraseña.

| Opción | Qué recuerda |
|---|---|
| **Remember me** (paso 1) | **La sesión**: no hay que volver a iniciar sesión al cerrar el navegador |
| **Remember this device** (paso 2) | **El navegador**: no se vuelve a pedir el **código** en ese navegador. La contraseña sí |

**Las cookies temporales de Identity:**

| Cookie | Para qué |
|---|---|
| `Identity.External` | Datos devueltos por Google |
| `Identity.TwoFactorUserId` | "Ya pasó la contraseña, falta el código" |
| `Identity.TwoFactorRememberMe` | "Remember this device" |

### Paso 4: los códigos de recuperación

**10 códigos de un solo uso** (`XXXXX-XXXXX`) para entrar si se pierde el celular.

| Método | Qué hace |
|---|---|
| `GenerateNewTwoFactorRecoveryCodesAsync(user, 10)` | Genera 10 y **reemplaza** los anteriores |
| `CountRecoveryCodesAsync(user)` | Cuántos quedan |
| `TwoFactorRecoveryCodeSignInAsync(code)` | Inicia sesión con uno **y lo elimina** |

- Se generan **cada vez que se activa el 2FA**, para que los de una activación anterior dejen de valer.
- Se muestran **una sola vez** (`ShowRecoveryCodes`), pasados por **`TempData`**: si se recarga la página, ya no aparecen.
- El panel muestra cuántos quedan (en amarillo si son 3 o menos) y permite **regenerarlos** (con confirmación, porque invalida los anteriores).
- En el login: *"Lost access to your authenticator app? Use a recovery code"* → `LoginWithRecoveryCode`, que usa **la misma cookie temporal** que el login con código.

❌ **Error cometido:** llamar `code` a la variable del `foreach` en la vista. **`@code` es una palabra reservada de Razor** (una directiva para bloques de código). Solución: otro nombre (`@recoveryCode`). Otras palabras a evitar tras `@`: `functions`, `model`, `using`, `inject`, `inherits`, `section`, `page`, `layout`. Una etiqueta HTML `<code>` sin `@` no tiene problema.

### Paso 5: desactivar el 2FA (`POST /Manage/Disable2fa`)

```csharp
await _userManager.SetTwoFactorEnabledAsync(user, false);
await _userManager.ResetAuthenticatorKeyAsync(user);   // la entrada de la app deja de servir
await _signInManager.ForgetTwoFactorClientAsync();      // olvida "Remember this device"
await _signInManager.RefreshSignInAsync(user);          // el SecurityStamp cambió
```

- Con una **clave nueva**, volver a activarlo exige escanear un QR nuevo.
- El botón pide confirmación (`onsubmit="return confirm(...)"`).

### Páginas del 2FA

| Página | Controlador | Para qué |
|---|---|---|
| `/Manage/EnableAuthenticator` | `ManageController` | QR, clave y primer código |
| `/Manage/ShowRecoveryCodes` | `ManageController` | Ver los códigos una vez |
| `POST /Manage/GenerateRecoveryCodes` | `ManageController` | Regenerarlos |
| `POST /Manage/Disable2fa` | `ManageController` | Desactivarlo |
| `/Account/LoginWith2fa` | `AccountController` | Segundo paso con la app |
| `/Account/LoginWithRecoveryCode` | `AccountController` | Segundo paso con un código de recuperación |

### Cómo probarlo

| # | Prueba | Resultado esperado |
|---|---|---|
| 1 | "Set up" → escanear el QR → código correcto | 2FA activado y los 10 códigos en pantalla |
| 2 | Recargar la página de los códigos | Vuelve al panel, sin códigos |
| 3 | Cerrar sesión y entrar con contraseña | Pide el código |
| 4 | Código de la app | ✅ Entra |
| 5 | "Use a recovery code" con uno de los códigos | ✅ Entra, y el panel muestra uno menos |
| 6 | Repetir el mismo código de recuperación | "invalid or has already been used" |
| 7 | "Remember this device" y volver a entrar | Pide la contraseña, no el código |
| 8 | Entrar con Google | Sin código (`bypassTwoFactor`) |
| 9 | "Disable" | El login vuelve a pedir solo la contraseña |

---

## 18. Git y commits

### Conventional Commits

| Prefijo | Uso |
|---|---|
| `chore:` | Preparar el entorno: plantilla, paquetes, configuración |
| `feat:` | Funcionalidad o código propio del proyecto |
| `docs:` | Cambios que solo afectan a la documentación |
| `fix:` | Corrige algo que estaba roto |
| `style:` | Cambios visuales o de formato que no alteran el comportamiento |

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
12. `docs: add email confirmation notes`
13. `feat: add password reset`
14. `feat: ask for username on registration`
15. `docs: document Google sign-in setup`
16. `feat: add Google sign-in`
17. `style: redesign login view and add Google sign-in button`
18. `docs: add Google sign-in notes`
19. `fix: allow login with email or username`
20. `docs: add notes on ApplicationUser constructor, username and email-or-username login`
21. `feat: add account page and authenticator key setup`
22. `feat: add two-factor authentication with authenticator app`
23. `feat: add 2FA recovery codes`
24. `fix: rename recovery code loop variable that clashed with Razor @code directive`
25. `feat: show a QR code to set up the authenticator app`
26. `feat: allow disabling 2FA and deleting the account`

---

## 19. Comandos útiles

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

## 20. Pendientes

- [x] Corregir `GET Login` para que reciba `returnUrl` y lo guarde en `ViewData`.
- [x] **Logout**: acción `POST` con `SignOutAsync()`.
- [x] Mostrar en el navbar si hay sesión iniciada (directamente en `_Layout.cshtml`, con `User.Identity?.IsAuthenticated`).
- [x] **Confirmación de correo**: SMTP con Gmail, MailKit, `IEmailSender`, token de confirmación y `RequireConfirmedEmail`.
- [ ] Resolver los usuarios antiguos con `EmailConfirmed = false` (ya no pueden iniciar sesión).
- [ ] (Opcional) Reenviar el correo de confirmación.
- [ ] (Opcional) Manejar un `code` que no sea Base64Url válido en `ConfirmEmail`.
- [x] Reset de contraseña (reutiliza `IEmailSender`, con `GeneratePasswordResetTokenAsync`).
- [ ] (Opcional) Enviar los correos en segundo plano, para evitar la fuga por tiempo de respuesta.
- [x] `RequireUniqueEmail`.
- [ ] Ajustar las reglas de contraseña (`options.Password`).
- [ ] ⚠️ **Crear y aplicar la migración de `[MaxLength(100)]` en `FullName`** (`AddFullNameMaxLength`). El atributo está en el código, pero la migración no existe: `dotnet ef migrations has-pending-model-changes` lo confirma.
- [ ] `ConfigureApplicationCookie`, después de `AddIdentity`.
- [x] Proteger páginas con `[Authorize]` (panel de cuenta).
- [ ] Etapa 3: `IdentityLab.Api` con `AddIdentityCore` + JWT.
- [x] **Login con Google**: `AddGoogle`, `ExternalLogin`, `ExternalLoginCallback` y botón en Login y Register.
- [x] **Login con email o username** (`FindByEmailAsync` / `FindByNameAsync` + `PasswordSignInAsync(user, ...)`).
- [ ] Manejar la cancelación en la pantalla de Google (hoy da error 500).
- [ ] Resolver la vulnerabilidad de `System.Security.Cryptography.Pkcs` 10.0.0 (llega por MailKit → MimeKit).
- [x] **2FA con app autenticadora**: activar con QR, login en dos pasos, códigos de recuperación y desactivar.
- [x] Eliminar la cuenta desde el panel.
- [ ] Vincular Google a una cuenta existente (opción A o B de la sección 15).
- [ ] Cambiar la contraseña y el username desde el panel.
- [x] Crear `.env` y `.env.example` en la raíz.
- [ ] Dockerizar: `Dockerfile` y `docker-compose.yml` que lean el `.env`.
