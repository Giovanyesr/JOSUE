using AsistenciaColegio.Data;
using AsistenciaColegio.Models;
using AsistenciaColegio.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace AsistenciaColegio.Tests;

public sealed class AttendanceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Database _db;
    private readonly AttendanceService _service;
    private readonly int _studentId;

    public AttendanceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        var passwords = new PasswordService();
        _db = new Database(new FakeEnvironment(_tempDir), passwords);
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
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class FakeEnvironment(string rootPath) : IWebHostEnvironment
    {
        public string WebRootPath { get => rootPath; set { } }
        public string ContentRootPath { get => rootPath; set { } }
        public string EnvironmentName { get => "Testing"; set { } }
        public string ApplicationName { get => "Test"; set { } }
        public IFileProvider WebRootFileProvider { get => new NullFileProvider(); set { } }
        public IFileProvider ContentRootFileProvider { get => new NullFileProvider(); set { } }
    }
}
