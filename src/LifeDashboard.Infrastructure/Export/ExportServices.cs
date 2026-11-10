using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.Export;

/// <summary>Flache Zeilenmodelle für den Export (gemeinsam für CSV und Excel).</summary>
internal static class ExportRows
{
    public static readonly string[] EventHeaders = ["Kalender", "Titel", "Beginn (UTC)", "Ende (UTC)", "Ganztägig", "Zeitzone", "Ort", "Von App erzeugt"];

    public static readonly string[] ActivityHeaders = ["ID", "Name", "Sportart", "Start (UTC)", "Dauer (min)", "Bewegungszeit (min)", "Distanz (km)", "Link"];

    public static readonly string[] LogHeaders = ["Zeit (UTC)", "Level", "Meldung"];

    public static IEnumerable<object?[]> Events(IEnumerable<CalendarEvent> events) => events
        .OrderBy(e => e.Schedule.SortKeyUtc)
        .Select(e => new object?[]
        {
            e.CalendarId,
            e.Title,
            e.Schedule.SortKeyUtc.UtcDateTime,
            e.Schedule.EndKeyUtc.UtcDateTime,
            e.Schedule is AllDaySchedule,
            (e.Schedule as TimedSchedule)?.TimeZoneId,
            e.Location,
            e.IsAppGenerated,
        });

    public static IEnumerable<object?[]> Activities(IEnumerable<SportActivity> activities) => activities
        .OrderBy(a => a.StartUtc)
        .Select(a => new object?[]
        {
            a.Id,
            a.Name,
            a.SportType,
            a.StartUtc.UtcDateTime,
            Math.Round(a.ElapsedTime.TotalMinutes, 1),
            Math.Round(a.MovingTime.TotalMinutes, 1),
            Math.Round(a.DistanceMeters / 1000d, 2),
            a.Url,
        });

    public static IEnumerable<object?[]> Log(IEnumerable<SyncLogEntry> entries) => entries
        .Select(l => new object?[] { l.TimestampUtc.UtcDateTime, l.Level, l.Message });
}

/// <summary>
/// Excel-Export mit ClosedXML (MIT-Lizenz). EPPlus wurde bewusst NICHT gewählt (ab v5 Polyform-Noncommercial/kommerziell).
/// </summary>
public sealed class ExcelExportService : IExportService
{
    public ExportFormat Format => ExportFormat.Xlsx;

    public string FileExtension => ".xlsx";

    public Task ExportEventsAsync(IEnumerable<CalendarEvent> events, Stream destination, CancellationToken cancellationToken = default)
        => Write("Termine", ExportRows.EventHeaders, ExportRows.Events(events), destination);

    public Task ExportActivitiesAsync(IEnumerable<SportActivity> activities, Stream destination, CancellationToken cancellationToken = default)
        => Write("Sport", ExportRows.ActivityHeaders, ExportRows.Activities(activities), destination);

    public Task ExportSyncLogAsync(IEnumerable<SyncLogEntry> entries, Stream destination, CancellationToken cancellationToken = default)
        => Write("Sync-Log", ExportRows.LogHeaders, ExportRows.Log(entries), destination);

    private static Task Write(string sheetName, string[] headers, IEnumerable<object?[]> rows, Stream destination)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                sheet.Cell(r, c + 1).Value = XLCellValue.FromObject(row[c]);
            }

            r++;
        }

        var table = sheet.Range(1, 1, Math.Max(r - 1, 1), headers.Length).CreateTable();
        table.Theme = XLTableTheme.TableStyleMedium2;
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(destination);
        return Task.CompletedTask;
    }
}

/// <summary>CSV-Export mit CsvHelper (Semikolon als Trenner, damit Excel im deutschsprachigen Raum korrekt öffnet).</summary>
public sealed class CsvExportService : IExportService
{
    private static readonly CsvConfiguration Config = new(CultureInfo.GetCultureInfo("de-AT")) { Delimiter = ";" };

    public ExportFormat Format => ExportFormat.Csv;

    public string FileExtension => ".csv";

    public Task ExportEventsAsync(IEnumerable<CalendarEvent> events, Stream destination, CancellationToken cancellationToken = default)
        => WriteAsync(ExportRows.EventHeaders, ExportRows.Events(events), destination, cancellationToken);

    public Task ExportActivitiesAsync(IEnumerable<SportActivity> activities, Stream destination, CancellationToken cancellationToken = default)
        => WriteAsync(ExportRows.ActivityHeaders, ExportRows.Activities(activities), destination, cancellationToken);

    public Task ExportSyncLogAsync(IEnumerable<SyncLogEntry> entries, Stream destination, CancellationToken cancellationToken = default)
        => WriteAsync(ExportRows.LogHeaders, ExportRows.Log(entries), destination, cancellationToken);

    private static async Task WriteAsync(string[] headers, IEnumerable<object?[]> rows, Stream destination, CancellationToken ct)
    {
        var writer = new StreamWriter(destination, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            var csv = new CsvWriter(writer, Config);
            await using (csv.ConfigureAwait(false))
            {
                foreach (var h in headers)
                {
                    csv.WriteField(h);
                }

                await csv.NextRecordAsync().ConfigureAwait(false);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var field in row)
                    {
                        csv.WriteField(field);
                    }

                    await csv.NextRecordAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
