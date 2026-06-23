using System.Text;
using AsistenciaColegio.Data;
using AsistenciaColegio.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AsistenciaColegio.Services;

public sealed class ReportService
{
    private readonly Database _database;

    public ReportService(Database database)
    {
        _database = database;
    }

    public byte[] BuildExcelReport(AttendanceFilterRequest filter)
    {
        var rows = GetRowsForReport(filter);
        var summary = BuildSummary(rows);
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0"?>""");
        sb.AppendLine("""<?mso-application progid="Excel.Sheet"?>""");
        sb.AppendLine("""<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">""");
        sb.AppendLine("<Worksheet ss:Name=\"Resumen\"><Table>");
        AppendExcelRow(sb, "Estado", "Cantidad");
        foreach (var item in summary)
        {
            AppendExcelRow(sb, item.Key, item.Value.ToString());
        }
        sb.AppendLine("</Table></Worksheet>");
        sb.AppendLine("<Worksheet ss:Name=\"Asistencias\"><Table>");
        AppendExcelRow(sb, "Fecha", "Hora", "Alumno", "DNI", "Codigo", "Estado");
        foreach (var row in rows)
        {
            AppendExcelRow(sb, row.Fecha, row.HoraRegistro, row.Alumno, row.Dni, row.Codigo, row.Estado);
        }

        sb.AppendLine("</Table></Worksheet></Workbook>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] BuildPdfReport(AttendanceFilterRequest filter)
    {
        var rows = GetRowsForReport(filter);
        var summary = BuildSummary(rows);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Reporte de asistencia").SemiBold().FontSize(18);
                    col.Item().Text($"Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Presentes: {summary.GetValueOrDefault("Presente")}  Tardanzas: {summary.GetValueOrDefault("Tardanza")}  Faltas: {summary.GetValueOrDefault("Falta")}")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Text("Fecha").SemiBold();
                        header.Cell().Background(Colors.Grey.Lighten3).Text("Hora").SemiBold();
                        header.Cell().Background(Colors.Grey.Lighten3).Text("Alumno").SemiBold();
                        header.Cell().Background(Colors.Grey.Lighten3).Text("DNI").SemiBold();
                        header.Cell().Background(Colors.Grey.Lighten3).Text("Codigo").SemiBold();
                        header.Cell().Background(Colors.Grey.Lighten3).Text("Estado").SemiBold();
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Text(row.Fecha);
                        table.Cell().Text(row.HoraRegistro);
                        table.Cell().Text(row.Alumno);
                        table.Cell().Text(row.Dni);
                        table.Cell().Text(row.Codigo);
                        table.Cell().Text(row.Estado);
                    }

                    if (rows.Count == 0)
                    {
                        table.Cell().ColumnSpan(6).Text("Sin asistencias registradas.").FontColor(Colors.Grey.Darken2);
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Pagina ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf();
    }

    private static void AppendExcelRow(StringBuilder sb, params string[] values)
    {
        sb.AppendLine("<Row>");
        foreach (var value in values)
        {
            sb.Append("<Cell><Data ss:Type=\"String\">");
            sb.Append(System.Security.SecurityElement.Escape(value));
            sb.AppendLine("</Data></Cell>");
        }

        sb.AppendLine("</Row>");
    }

    private static Dictionary<string, int> BuildSummary(IEnumerable<AttendanceDto> rows)
    {
        var summary = new Dictionary<string, int>
        {
            ["Presente"] = 0,
            ["Tardanza"] = 0,
            ["Falta"] = 0
        };

        foreach (var row in rows)
        {
            summary[row.Estado] = summary.GetValueOrDefault(row.Estado) + 1;
        }

        return summary;
    }

    private IReadOnlyList<AttendanceDto> GetRowsForReport(AttendanceFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.FechaInicio)
            && filter.FechaInicio == filter.FechaFin
            && DateOnly.TryParse(filter.FechaInicio, out var date)
            && string.IsNullOrWhiteSpace(filter.Alumno)
            && string.IsNullOrWhiteSpace(filter.Dni))
        {
            var daily = _database.GetDailyAttendanceWithAbsences(date);
            return string.IsNullOrWhiteSpace(filter.Estado)
                ? daily
                : daily.Where(row => row.Estado == filter.Estado).ToList();
        }

        return _database.GetAttendanceReport(filter);
    }
}
