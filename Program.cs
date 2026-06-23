using AsistenciaColegio.Data;
using AsistenciaColegio.Models;
using AsistenciaColegio.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using QuestPDF.Infrastructure;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var supabaseConnectionString = builder.Configuration.GetConnectionString("Supabase")
    ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Supabase connection string is required. Set ConnectionStrings:Supabase in appsettings.json or SUPABASE_CONNECTION_STRING env var.");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys")));
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "asistencia_sesion";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddSingleton<Database>(sp => new Database(supabaseConnectionString, sp.GetRequiredService<PasswordService>()));
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<AttendanceService>();
builder.Services.AddSingleton<ReportService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

QuestPDF.Settings.License = LicenseType.Community;

var database = app.Services.GetRequiredService<Database>();
database.Initialize();

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, Database db, PasswordService passwords) =>
{
    if (string.Equals(request.Rol, "docente", StringComparison.OrdinalIgnoreCase))
    {
        var docente = db.GetTeacherByUser(request.Usuario);
        if (docente is null || !passwords.Verify(request.Contrasena, docente.Contrasena))
        {
            return Results.BadRequest(new ApiMessage("Usuario o contrasena incorrectos."));
        }

        if (passwords.NeedsRehash(docente.Contrasena))
        {
            db.UpdateTeacherPassword(docente.Id, passwords.Hash(request.Contrasena));
        }

        await SignIn(context, "docente", docente.Id, docente.Nombres);
        return Results.Ok(new LoginResponse("docente", docente.Id, docente.Nombres));
    }

    var alumno = db.GetStudentByUser(request.Usuario);
    if (alumno is null || !passwords.Verify(request.Contrasena, alumno.Contrasena))
    {
        return Results.BadRequest(new ApiMessage("Usuario o contrasena incorrectos."));
    }

    if (passwords.NeedsRehash(alumno.Contrasena))
    {
        db.UpdateStudentPassword(alumno.Id, passwords.Hash(request.Contrasena));
    }

    var nombre = $"{alumno.Apellidos}, {alumno.Nombres}";
    await SignIn(context, "alumno", alumno.Id, nombre);
    return Results.Ok(new LoginResponse("alumno", alumno.Id, nombre));
}).RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync();
    return Results.Ok(new ApiMessage("Sesion cerrada."));
});

app.MapGet("/api/auth/sesion", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new LoginResponse(
        user.FindFirstValue(ClaimTypes.Role) ?? "",
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"),
        user.FindFirstValue(ClaimTypes.Name) ?? ""));
}).RequireAuthorization();

app.MapPost("/api/alumnos/registro", (StudentCreateRequest request, Database db, PasswordService passwords) =>
{
    var error = Validators.ValidateStudent(request);
    if (error is not null)
    {
        return Results.BadRequest(new ApiMessage(error));
    }

    try
    {
        var id = db.CreateStudent(request, passwords.Hash(request.Contrasena));
        return Results.Ok(new { id, message = "Alumno registrado correctamente." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ApiMessage(ex.Message));
    }
});

app.MapGet("/api/alumnos/validar-dni/{dni}", (string dni, Database db) =>
{
    if (dni.Length != 8 || !dni.All(char.IsDigit))
    {
        return Results.Ok(new { disponible = false, message = "El DNI debe tener 8 digitos." });
    }

    var exists = db.StudentDniExists(dni);
    return Results.Ok(new { disponible = !exists, message = exists ? "El DNI ya esta registrado." : "DNI disponible." });
});

app.MapGet("/api/alumnos", (Database db) => Results.Ok(db.GetStudents())).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapPost("/api/alumnos", (StudentCreateRequest request, Database db, PasswordService passwords) =>
{
    var error = Validators.ValidateStudent(request);
    if (error is not null)
    {
        return Results.BadRequest(new ApiMessage(error));
    }

    try
    {
        var id = db.CreateStudent(request, passwords.Hash(request.Contrasena));
        return Results.Ok(new { id, message = "Alumno creado correctamente." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ApiMessage(ex.Message));
    }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapPut("/api/alumnos/{id:int}", (int id, StudentUpdateRequest request, Database db, PasswordService passwords) =>
{
    var error = Validators.ValidateStudent(request);
    if (error is not null)
    {
        return Results.BadRequest(new ApiMessage(error));
    }

    try
    {
        var passwordHash = string.IsNullOrWhiteSpace(request.Contrasena) ? null : passwords.Hash(request.Contrasena);
        db.UpdateStudent(id, request, passwordHash);
        return Results.Ok(new ApiMessage("Alumno actualizado correctamente."));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ApiMessage(ex.Message));
    }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapDelete("/api/alumnos/{id:int}", (int id, Database db) =>
{
    db.DeleteStudent(id);
    return Results.Ok(new ApiMessage("Alumno eliminado correctamente."));
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapGet("/api/configuracion", (Database db) => Results.Ok(db.GetConfiguration())).RequireAuthorization();

app.MapPut("/api/configuracion", (ConfigurationRequest request, Database db) =>
{
    if (string.IsNullOrWhiteSpace(request.CodigoActual))
    {
        return Results.BadRequest(new ApiMessage("El codigo de asistencia es obligatorio."));
    }

    if (!TimeOnly.TryParse(request.HoraInicio, out var inicio) || !TimeOnly.TryParse(request.HoraFin, out var fin) || inicio >= fin)
    {
        return Results.BadRequest(new ApiMessage("El horario de asistencia no es valido."));
    }

    if (request.MinutosTardanza < 0 || request.MinutosTardanza > 45)
    {
        return Results.BadRequest(new ApiMessage("Los minutos de tolerancia deben estar entre 0 y 45."));
    }

    db.UpdateConfiguration(request.CodigoActual.Trim(), inicio, fin, request.MinutosTardanza);
    return Results.Ok(new ApiMessage("Configuracion actualizada correctamente."));
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapPost("/api/asistencias/registrar", (AttendanceRegisterRequest request, ClaimsPrincipal user, AttendanceService service) =>
{
    var alumnoId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
    var result = service.RegisterAttendance(alumnoId, request.Codigo);
    return result.Success ? Results.Ok(new ApiMessage(result.Message)) : Results.BadRequest(new ApiMessage(result.Message));
}).RequireAuthorization(new AuthorizeAttribute { Roles = "alumno" });

app.MapGet("/api/asistencias", (string? fechaInicio, string? fechaFin, string? alumno, string? dni, string? estado, Database db) =>
{
    return Results.Ok(db.GetAttendanceReport(new AttendanceFilterRequest(fechaInicio, fechaFin, alumno, dni, estado)));
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapGet("/api/asistencias/diario", (string? fecha, Database db) =>
{
    var target = DateOnly.TryParse(fecha, out var parsed) ? parsed : DateOnly.FromDateTime(DateTime.Now);
    return Results.Ok(db.GetDailyAttendanceWithAbsences(target));
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapGet("/api/alumnos/{id:int}/asistencias", (int id, ClaimsPrincipal user, Database db) =>
{
    var currentId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
    if (!user.IsInRole("docente") && id != currentId)
    {
        return Results.Forbid();
    }

    return Results.Ok(db.GetStudentAttendance(id));
}).RequireAuthorization();

app.MapGet("/api/dashboard", (Database db) => Results.Ok(db.GetDashboard(DateOnly.FromDateTime(DateTime.Now))))
    .RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapPost("/api/alumnos/importar", (ImportStudentsRequest request, Database db, PasswordService passwords) =>
{
    var result = ImportStudents(request.Csv, db, passwords);
    return Results.Ok(result);
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapGet("/api/reportes/excel", (string? fechaInicio, string? fechaFin, string? alumno, string? dni, string? estado, ReportService reports) =>
{
    var file = reports.BuildExcelReport(new AttendanceFilterRequest(fechaInicio, fechaFin, alumno, dni, estado));
    return Results.File(file, "application/vnd.ms-excel", $"reporte-asistencia-{DateTime.Now:yyyyMMdd}.xls");
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.MapGet("/api/reportes/pdf", (string? fechaInicio, string? fechaFin, string? alumno, string? dni, string? estado, ReportService reports) =>
{
    var file = reports.BuildPdfReport(new AttendanceFilterRequest(fechaInicio, fechaFin, alumno, dni, estado));
    return Results.File(file, "application/pdf", $"reporte-asistencia-{DateTime.Now:yyyyMMdd}.pdf");
}).RequireAuthorization(new AuthorizeAttribute { Roles = "docente" });

app.Run();

static async Task SignIn(HttpContext context, string role, int id, string name)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, id.ToString()),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Role, role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
}

static ImportResultDto ImportStudents(string csv, Database db, PasswordService passwords)
{
    var imported = 0;
    var errors = new List<string>();
    var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    for (var index = 0; index < lines.Length; index++)
    {
        var values = lines[index].Split(',').Select(value => value.Trim()).ToArray();
        if (values.Length < 5)
        {
            errors.Add($"Linea {index + 1}: usa nombres,apellidos,dni,usuario,contrasena.");
            continue;
        }

        var request = new StudentCreateRequest(values[0], values[1], values[2], values[3], values[4]);
        var error = Validators.ValidateStudent(request);
        if (error is not null)
        {
            errors.Add($"Linea {index + 1}: {error}");
            continue;
        }

        try
        {
            db.CreateStudent(request, passwords.Hash(request.Contrasena));
            imported++;
        }
        catch (InvalidOperationException ex)
        {
            errors.Add($"Linea {index + 1}: {ex.Message}");
        }
    }

    return new ImportResultDto(imported, errors);
}
