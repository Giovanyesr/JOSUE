using AsistenciaColegio.Data;
using AsistenciaColegio.Models;
using AsistenciaColegio.Services;

namespace AsistenciaColegio.Tests;

public sealed class AttendanceServiceTests : IDisposable
{
    private readonly string _schema;
    private readonly Database _db;
    private readonly AttendanceService _service;
    private readonly int _studentId;

    public AttendanceServiceTests()
    {
        _schema = "test_" + Guid.NewGuid().ToString("N")[..12];
        var connString = GetConnectionString() + $";Search Path={_schema}";
        var passwords = new PasswordService();
        using (var conn = new Npgsql.NpgsqlConnection(GetConnectionString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {_schema}";
            cmd.ExecuteNonQuery();
        }
        _db = new Database(connString, passwords);
        _db.Initialize();
        _db.UpdateConfiguration("TEST2026", new TimeOnly(7, 0), new TimeOnly(17, 0), 15);
        _studentId = _db.CreateStudent(new StudentCreateRequest("Test", "Student A", "11111111", "teststu", "pass"), passwords.Hash("pass"));
        _service = new AttendanceService(_db);
    }

    [Fact]
    public void RegisterAttendance_rejects_invalid_code()
    {
        var result = _service.RegisterAttendance(_studentId, "WRONG");
        Assert.False(result.Success);
    }

    [Fact]
    public void RegisterAttendance_rejects_nonexistent_student()
    {
        var result = _service.RegisterAttendance(9999, "TEST2026");
        Assert.False(result.Success);
    }

    [Fact]
    public void RegisterAttendance_accepts_valid_code_in_window()
    {
        var result = _service.RegisterAttendance(_studentId, "TEST2026");
        Assert.True(result.Success);
    }

    [Fact]
    public void RegisterAttendance_rejects_duplicate()
    {
        _service.RegisterAttendance(_studentId, "TEST2026");
        var result = _service.RegisterAttendance(_studentId, "TEST2026");
        Assert.False(result.Success);
    }

    public void Dispose()
    {
        try
        {
            using var conn = new Npgsql.NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {_schema} CASCADE";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("SUPABASE_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=asistencia_test;Username=testuser;Password=testpass";
    }
}
