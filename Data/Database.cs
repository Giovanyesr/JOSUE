using AsistenciaColegio.Models;
using AsistenciaColegio.Services;
using Microsoft.Data.Sqlite;

namespace AsistenciaColegio.Data;

public sealed class Database
{
    private readonly string _connectionString;
    private readonly PasswordService _passwords;

    public Database(IWebHostEnvironment environment, PasswordService passwords)
    {
        var dbPath = Path.Combine(environment.ContentRootPath, "asistencia.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _passwords = passwords;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Alumnos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nombres TEXT NOT NULL,
                apellidos TEXT NOT NULL,
                dni TEXT NOT NULL UNIQUE,
                usuario TEXT NOT NULL UNIQUE,
                contrasena TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Docentes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nombres TEXT NOT NULL,
                usuario TEXT NOT NULL UNIQUE,
                contrasena TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Asistencias (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                alumno_id INTEGER NOT NULL,
                fecha TEXT NOT NULL,
                hora_registro TEXT NOT NULL,
                codigo TEXT NOT NULL,
                estado TEXT NOT NULL,
                FOREIGN KEY (alumno_id) REFERENCES Alumnos(id) ON DELETE CASCADE,
                UNIQUE (alumno_id, fecha)
            );

            CREATE TABLE IF NOT EXISTS Configuracion (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                codigo_actual TEXT NOT NULL,
                hora_inicio TEXT NOT NULL,
                hora_fin TEXT NOT NULL,
                minutos_tardanza INTEGER NOT NULL DEFAULT 15
            );
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing("Configuracion", "minutos_tardanza", "INTEGER NOT NULL DEFAULT 15");
        Execute("INSERT OR IGNORE INTO Configuracion (id, codigo_actual, hora_inicio, hora_fin, minutos_tardanza) VALUES (1, 'CLASE2026', '07:00', '07:45', 15)");
        Execute(
            "INSERT OR IGNORE INTO Docentes (id, nombres, usuario, contrasena) VALUES (1, $nombres, $usuario, $contrasena)",
            ("$nombres", "Docente Principal"),
            ("$usuario", "admin"),
            ("$contrasena", _passwords.Hash("admin123")));
    }

    public void UpdateTeacherPassword(int id, string passwordHash)
    {
        Execute("UPDATE Docentes SET contrasena = $contrasena WHERE id = $id", ("$id", id), ("$contrasena", passwordHash));
    }

    public void UpdateStudentPassword(int id, string passwordHash)
    {
        Execute("UPDATE Alumnos SET contrasena = $contrasena WHERE id = $id", ("$id", id), ("$contrasena", passwordHash));
    }

    public TeacherDto? GetTeacherByUser(string usuario)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection, "SELECT id, nombres, usuario, contrasena FROM Docentes WHERE usuario = $usuario", ("$usuario", usuario.Trim()));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new TeacherDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public StudentAuthDto? GetStudentByUser(string usuario)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection, "SELECT id, nombres, apellidos, usuario, contrasena FROM Alumnos WHERE usuario = $usuario", ("$usuario", usuario.Trim()));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new StudentAuthDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    public int CreateStudent(StudentCreateRequest request, string passwordHash)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = CreateCommand(connection, """
                INSERT INTO Alumnos (nombres, apellidos, dni, usuario, contrasena)
                VALUES ($nombres, $apellidos, $dni, $usuario, $contrasena);
                SELECT last_insert_rowid();
                """,
                ("$nombres", request.Nombres.Trim()),
                ("$apellidos", request.Apellidos.Trim()),
                ("$dni", request.Dni.Trim()),
                ("$usuario", request.Usuario.Trim()),
                ("$contrasena", passwordHash));
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Ya existe un alumno con ese DNI o usuario.");
        }
    }

    public void UpdateStudent(int id, StudentUpdateRequest request, string? passwordHash)
    {
        try
        {
            var sql = passwordHash is null
                ? """
                  UPDATE Alumnos
                  SET nombres = $nombres, apellidos = $apellidos, dni = $dni, usuario = $usuario
                  WHERE id = $id
                  """
                : """
                  UPDATE Alumnos
                  SET nombres = $nombres, apellidos = $apellidos, dni = $dni, usuario = $usuario, contrasena = $contrasena
                  WHERE id = $id
                  """;

            var parameters = new List<(string, object?)>
            {
                ("$id", id),
                ("$nombres", request.Nombres.Trim()),
                ("$apellidos", request.Apellidos.Trim()),
                ("$dni", request.Dni.Trim()),
                ("$usuario", request.Usuario.Trim())
            };
            if (passwordHash is not null)
            {
                parameters.Add(("$contrasena", passwordHash));
            }

            Execute(sql, parameters.ToArray());
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Ya existe un alumno con ese DNI o usuario.");
        }
    }

    public void DeleteStudent(int id)
    {
        Execute("DELETE FROM Alumnos WHERE id = $id", ("$id", id));
    }

    public bool StudentDniExists(string dni, int? excludeId = null)
    {
        using var connection = OpenConnection();
        var sql = excludeId is null
            ? "SELECT COUNT(*) FROM Alumnos WHERE dni = $dni"
            : "SELECT COUNT(*) FROM Alumnos WHERE dni = $dni AND id <> $id";
        using var command = excludeId is null
            ? CreateCommand(connection, sql, ("$dni", dni.Trim()))
            : CreateCommand(connection, sql, ("$dni", dni.Trim()), ("$id", excludeId.Value));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public IReadOnlyList<StudentDto> GetStudents()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nombres, apellidos, dni, usuario
            FROM Alumnos
            ORDER BY
                lower(CASE WHEN instr(trim(apellidos), ' ') > 0 THEN substr(trim(apellidos), 1, instr(trim(apellidos), ' ') - 1) ELSE trim(apellidos) END),
                lower(CASE WHEN instr(trim(apellidos), ' ') > 0 THEN substr(trim(apellidos), instr(trim(apellidos), ' ') + 1) ELSE '' END),
                lower(nombres)
            """;
        using var reader = command.ExecuteReader();
        var students = new List<StudentDto>();
        while (reader.Read())
        {
            students.Add(new StudentDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return students;
    }

    public ConfigurationDto GetConfiguration()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, codigo_actual, hora_inicio, hora_fin, minutos_tardanza FROM Configuracion WHERE id = 1";
        using var reader = command.ExecuteReader();
        reader.Read();
        return new ConfigurationDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
    }

    public void UpdateConfiguration(string codigo, TimeOnly inicio, TimeOnly fin, int minutosTardanza)
    {
        Execute("""
            UPDATE Configuracion
            SET codigo_actual = $codigo, hora_inicio = $inicio, hora_fin = $fin, minutos_tardanza = $minutosTardanza
            WHERE id = 1
            """,
            ("$codigo", codigo),
            ("$inicio", inicio.ToString("HH:mm")),
            ("$fin", fin.ToString("HH:mm")),
            ("$minutosTardanza", minutosTardanza));
    }

    public OperationResult InsertAttendance(int alumnoId, string codigo, DateOnly fecha, TimeOnly hora, string estado)
    {
        try
        {
            Execute("""
                INSERT INTO Asistencias (alumno_id, fecha, hora_registro, codigo, estado)
                VALUES ($alumnoId, $fecha, $hora, $codigo, $estado)
                """,
                ("$alumnoId", alumnoId),
                ("$fecha", fecha.ToString("yyyy-MM-dd")),
                ("$hora", hora.ToString("HH:mm:ss")),
                ("$codigo", codigo),
                ("$estado", estado));
            return new OperationResult(true, $"Asistencia registrada como {estado}.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return new OperationResult(false, "El alumno ya registro asistencia hoy.");
        }
    }

    public bool StudentExists(int alumnoId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection, "SELECT COUNT(*) FROM Alumnos WHERE id = $id", ("$id", alumnoId));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public IReadOnlyList<AttendanceDto> GetAttendanceHistory()
    {
        return QueryAttendance("""
            SELECT a.id, a.alumno_id, al.apellidos || ', ' || al.nombres, al.dni, a.fecha, a.hora_registro, a.codigo, a.estado
            FROM Asistencias a
            INNER JOIN Alumnos al ON al.id = a.alumno_id
            ORDER BY a.fecha DESC, a.hora_registro DESC
            """);
    }

    public IReadOnlyList<AttendanceDto> GetAttendanceReport(AttendanceFilterRequest filter)
    {
        var sql = new List<string>
        {
            """
            SELECT a.id, a.alumno_id, al.apellidos || ', ' || al.nombres, al.dni, a.fecha, a.hora_registro, a.codigo, a.estado
            FROM Asistencias a
            INNER JOIN Alumnos al ON al.id = a.alumno_id
            WHERE 1 = 1
            """
        };
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(filter.FechaInicio))
        {
            sql.Add("AND a.fecha >= $fechaInicio");
            parameters.Add(("$fechaInicio", filter.FechaInicio));
        }

        if (!string.IsNullOrWhiteSpace(filter.FechaFin))
        {
            sql.Add("AND a.fecha <= $fechaFin");
            parameters.Add(("$fechaFin", filter.FechaFin));
        }

        if (!string.IsNullOrWhiteSpace(filter.Alumno))
        {
            sql.Add("AND lower(al.apellidos || ' ' || al.nombres) LIKE $alumno");
            parameters.Add(("$alumno", $"%{filter.Alumno.Trim().ToLowerInvariant()}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Dni))
        {
            sql.Add("AND al.dni LIKE $dni");
            parameters.Add(("$dni", $"%{filter.Dni.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Estado))
        {
            sql.Add("AND a.estado = $estado");
            parameters.Add(("$estado", filter.Estado));
        }

        sql.Add("ORDER BY a.fecha DESC, a.hora_registro DESC");
        return QueryAttendance(string.Join('\n', sql), parameters.ToArray());
    }

    public IReadOnlyList<AttendanceDto> GetDailyAttendanceWithAbsences(DateOnly fecha)
    {
        return QueryAttendance("""
            SELECT
                COALESCE(a.id, 0),
                al.id,
                al.apellidos || ', ' || al.nombres,
                al.dni,
                $fecha,
                COALESCE(a.hora_registro, ''),
                COALESCE(a.codigo, ''),
                COALESCE(a.estado, 'Falta')
            FROM Alumnos al
            LEFT JOIN Asistencias a ON a.alumno_id = al.id AND a.fecha = $fecha
            ORDER BY
                lower(CASE WHEN instr(trim(al.apellidos), ' ') > 0 THEN substr(trim(al.apellidos), 1, instr(trim(al.apellidos), ' ') - 1) ELSE trim(al.apellidos) END),
                lower(CASE WHEN instr(trim(al.apellidos), ' ') > 0 THEN substr(trim(al.apellidos), instr(trim(al.apellidos), ' ') + 1) ELSE '' END),
                lower(al.nombres)
            """, ("$fecha", fecha.ToString("yyyy-MM-dd")));
    }

    public IReadOnlyList<AttendanceDto> GetStudentAttendance(int alumnoId)
    {
        return QueryAttendance("""
            SELECT a.id, a.alumno_id, al.apellidos || ', ' || al.nombres, al.dni, a.fecha, a.hora_registro, a.codigo, a.estado
            FROM Asistencias a
            INNER JOIN Alumnos al ON al.id = a.alumno_id
            WHERE a.alumno_id = $alumnoId
            ORDER BY a.fecha DESC, a.hora_registro DESC
            """, ("$alumnoId", alumnoId));
    }

    public DashboardDto GetDashboard(DateOnly fecha)
    {
        using var connection = OpenConnection();
        var total = ScalarInt(connection, "SELECT COUNT(*) FROM Alumnos");
        var presentes = ScalarInt(connection, "SELECT COUNT(*) FROM Asistencias WHERE fecha = $fecha AND estado = 'Presente'", ("$fecha", fecha.ToString("yyyy-MM-dd")));
        var tardanzas = ScalarInt(connection, "SELECT COUNT(*) FROM Asistencias WHERE fecha = $fecha AND estado = 'Tardanza'", ("$fecha", fecha.ToString("yyyy-MM-dd")));
        var faltas = Math.Max(0, total - presentes - tardanzas);
        return new DashboardDto(presentes, tardanzas, faltas, total);
    }

    private IReadOnlyList<AttendanceDto> QueryAttendance(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<AttendanceDto>();
        while (reader.Read())
        {
            rows.Add(new AttendanceDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return rows;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void AddColumnIfMissing(string table, string column, string definition)
    {
        var exists = false;
        using var connection = OpenConnection();
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, sql, parameters);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
