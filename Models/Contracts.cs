namespace AsistenciaColegio.Models;

public record ApiMessage(string Message);

public record LoginRequest(string Rol, string Usuario, string Contrasena);

public record LoginResponse(string Rol, int Id, string Nombre);

public record StudentCreateRequest(
    string Nombres,
    string Apellidos,
    string Dni,
    string Usuario,
    string Contrasena);

public record StudentUpdateRequest(
    string Nombres,
    string Apellidos,
    string Dni,
    string Usuario,
    string? Contrasena);

public record AttendanceRegisterRequest(string Codigo);

public record ConfigurationRequest(string CodigoActual, string HoraInicio, string HoraFin, int MinutosTardanza);

public record AttendanceFilterRequest(string? FechaInicio, string? FechaFin, string? Alumno, string? Dni, string? Estado);

public record ImportStudentsRequest(string Csv);

public record StudentDto(int Id, string Nombres, string Apellidos, string Dni, string Usuario);

public record TeacherDto(int Id, string Nombres, string Usuario, string Contrasena);

public record StudentAuthDto(int Id, string Nombres, string Apellidos, string Usuario, string Contrasena);

public record ConfigurationDto(int Id, string CodigoActual, string HoraInicio, string HoraFin, int MinutosTardanza);

public record AttendanceDto(
    int Id,
    int AlumnoId,
    string Alumno,
    string Dni,
    string Fecha,
    string HoraRegistro,
    string Codigo,
    string Estado);

public record DashboardDto(int Presentes, int Tardanzas, int Faltas, int TotalAlumnos);

public record ImportResultDto(int Importados, IReadOnlyList<string> Errores);

public record OperationResult(bool Success, string Message);
