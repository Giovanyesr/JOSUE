using AsistenciaColegio.Data;
using AsistenciaColegio.Models;
using AsistenciaColegio.Services;

namespace AsistenciaColegio.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _schema;
    private readonly Database _db;
    private readonly PasswordService _passwords = new();

    public DatabaseTests()
    {
        _schema = "test_" + Guid.NewGuid().ToString("N")[..12];
        var connString = GetConnectionString() + $";Search Path={_schema}";
        _db = new Database(connString, _passwords);
        ExecuteRaw($"CREATE SCHEMA IF NOT EXISTS {_schema}");
        _db.Initialize();
    }

    [Fact]
    public void Initialize_creates_tables_and_default_data()
    {
        var config = _db.GetConfiguration();
        Assert.NotNull(config);
        Assert.Equal("CLASE2026", config.CodigoActual);
    }

    [Fact]
    public void CreateStudent_inserts_and_returns_id()
    {
        var id = _db.CreateStudent(new StudentCreateRequest("Juan", "Perez Lopez", "12345678", "juanp", "pass1234"), _passwords.Hash("pass1234"));
        Assert.True(id > 0);
    }

    [Fact]
    public void CreateStudent_throws_on_duplicate_dni()
    {
        _db.CreateStudent(new StudentCreateRequest("Juan", "Perez Lopez", "12345678", "juanp", "pass1234"), _passwords.Hash("pass1234"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _db.CreateStudent(new StudentCreateRequest("Ana", "Lopez Ruiz", "12345678", "anal", "pass5678"), _passwords.Hash("pass5678")));
        Assert.Contains("ya existe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StudentDniExists_checks_correctly()
    {
        _db.CreateStudent(new StudentCreateRequest("Luis", "Martinez Diaz", "11111111", "luism", "pass1234"), _passwords.Hash("pass1234"));
        Assert.True(_db.StudentDniExists("11111111"));
        Assert.False(_db.StudentDniExists("99999999"));
    }

    [Fact]
    public void InsertAttendance_returns_success()
    {
        var id = _db.CreateStudent(new StudentCreateRequest("Test", "Student A", "99999999", "teststu", "pass1234"), _passwords.Hash("pass1234"));
        var result = _db.InsertAttendance(id, "CLASE2026", DateOnly.FromDateTime(DateTime.Now), TimeOnly.FromDateTime(DateTime.Now), "Presente");
        Assert.True(result.Success);
    }

    [Fact]
    public void InsertAttendance_prevents_duplicates()
    {
        var id = _db.CreateStudent(new StudentCreateRequest("Test2", "Student B", "88888888", "testst2", "pass1234"), _passwords.Hash("pass1234"));
        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = TimeOnly.FromDateTime(DateTime.Now);
        _db.InsertAttendance(id, "CODE", today, now, "Presente");
        var result = _db.InsertAttendance(id, "CODE", today, now, "Tardanza");
        Assert.False(result.Success);
    }

    [Fact]
    public void DeleteStudent_removes_student()
    {
        var id = _db.CreateStudent(new StudentCreateRequest("ToDelete", "E F", "77777777", "deleteu", "pass"), _passwords.Hash("pass"));
        _db.DeleteStudent(id);
        Assert.Null(_db.GetStudentByUser("deleteu"));
    }

    [Fact]
    public void GetDailyAttendanceWithAbsences_includes_absent_students()
    {
        _db.CreateStudent(new StudentCreateRequest("Absent", "Student Z", "55555555", "absentz", "pass"), _passwords.Hash("pass"));
        var daily = _db.GetDailyAttendanceWithAbsences(DateOnly.FromDateTime(DateTime.Now));
        Assert.Single(daily);
        Assert.Equal("Falta", daily[0].Estado);
    }

    [Fact]
    public void UpdateConfiguration_persists_changes()
    {
        _db.UpdateConfiguration("NEWCODE", new TimeOnly(8, 0), new TimeOnly(16, 0), 20);
        var config = _db.GetConfiguration();
        Assert.Equal("NEWCODE", config.CodigoActual);
        Assert.Equal("08:00", config.HoraInicio);
        Assert.Equal(20, config.MinutosTardanza);
    }

    public void Dispose()
    {
        try { ExecuteRaw($"DROP SCHEMA IF EXISTS {_schema} CASCADE"); } catch { }
    }

    private void ExecuteRaw(string sql)
    {
        using var conn = new Npgsql.NpgsqlConnection(GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("SUPABASE_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=asistencia_test;Username=testuser;Password=testpass";
    }
}
