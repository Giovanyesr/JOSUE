using AsistenciaColegio.Data;
using AsistenciaColegio.Models;

namespace AsistenciaColegio.Services;

public sealed class AttendanceService
{
    private readonly Database _database;

    public AttendanceService(Database database)
    {
        _database = database;
    }

    public OperationResult RegisterAttendance(int alumnoId, string codigo)
    {
        if (!_database.StudentExists(alumnoId))
        {
            return new OperationResult(false, "El alumno no existe.");
        }

        var configuration = _database.GetConfiguration();
        if (!string.Equals(configuration.CodigoActual, codigo.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new OperationResult(false, "El codigo de asistencia no es correcto.");
        }

        var now = DateTime.Now;
        var currentTime = TimeOnly.FromDateTime(now);
        var start = TimeOnly.Parse(configuration.HoraInicio);
        var end = TimeOnly.Parse(configuration.HoraFin);

        if (currentTime < start || currentTime > end)
        {
            return new OperationResult(false, $"La asistencia solo se registra entre {configuration.HoraInicio} y {configuration.HoraFin}.");
        }

        var tardyLimit = start.AddMinutes(configuration.MinutosTardanza);
        var status = currentTime <= tardyLimit ? "Presente" : "Tardanza";
        return _database.InsertAttendance(alumnoId, codigo.Trim(), DateOnly.FromDateTime(now), currentTime, status);
    }
}
