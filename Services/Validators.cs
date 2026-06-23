using AsistenciaColegio.Models;

namespace AsistenciaColegio.Services;

public static class Validators
{
    public static string? ValidateStudent(StudentCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Contrasena) || request.Contrasena.Length < 4)
        {
            return "La contrasena debe tener al menos 4 caracteres.";
        }

        return ValidateStudentCore(request.Nombres, request.Apellidos, request.Dni, request.Usuario);
    }

    public static string? ValidateStudent(StudentUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Contrasena) && request.Contrasena.Length < 4)
        {
            return "La contrasena debe tener al menos 4 caracteres.";
        }

        return ValidateStudentCore(request.Nombres, request.Apellidos, request.Dni, request.Usuario);
    }

    private static string? ValidateStudentCore(string nombres, string apellidos, string dni, string usuario)
    {
        if (string.IsNullOrWhiteSpace(nombres) || string.IsNullOrWhiteSpace(apellidos) || string.IsNullOrWhiteSpace(usuario))
        {
            return "Nombres, apellidos y usuario son obligatorios.";
        }

        if (dni.Length != 8 || !dni.All(char.IsDigit))
        {
            return "El DNI debe tener 8 digitos numericos.";
        }

        if (apellidos.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            return "Ingresa apellido paterno y apellido materno.";
        }

        return null;
    }
}
