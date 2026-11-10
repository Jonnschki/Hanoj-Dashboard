using System.Net;
using System.Text;
using ClosedXML.Excel;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using LifeDashboard.Infrastructure.Export;
using LifeDashboard.Infrastructure.Ics;
using LifeDashboard.Infrastructure.Markets;
using LifeDashboard.Infrastructure.Strava;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LifeDashboard.Infrastructure.Tests;

public sealed class StravaMapperTests
{
    [Fact]
    public void Maps_summary_activity_to_domain()
    {
        var dto = new StravaActivityDto(987, "Lauf im Prater", "Run", "Run", new DateTimeOffset(2026, 5, 1, 6, 0, 0, TimeSpan.Zero), "(GMT+01:00) Europe/Vienna", 3000, 2900, 10012.5);

        var activity = StravaMapper.ToDomain(dto);

        activity.Id.Should().Be("987");
        activity.TimeZoneId.Should().Be("Europe/Vienna");
        activity.ElapsedTime.Should().Be(TimeSpan.FromMinutes(50));
        activity.Url.Should().Be("https://www.strava.com/activities/987");
    }

    [Theory]
    [InlineData(null, "UTC")]
    [InlineData("Europe/Berlin", "Europe/Berlin")]
    [InlineData("(GMT-05:00) America/New_York", "America/New_York")]
    public void Parses_strava_timezone(string? input, string expected) => StravaMapper.ParseTimeZone(input).Should().Be(expected);
}

public sealed class ExportTests
{
    private static readonly CalendarEvent Sample = new("1", "cal", "Meeting; mit Semikolon", null, "Wien",
        new TimedSchedule(new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), "Europe/Vienna"),
        EventVisibility.Normal, ShowAs.Busy, DateTimeOffset.UnixEpoch.ToUniversalTime());

    [Fact]
    public async Task Csv_contains_header_and_escaped_row()
    {
        using var stream = new MemoryStream();

        await new CsvExportService().ExportEventsAsync([Sample], stream);

        var text = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿');
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("Kalender;Titel;");
        lines[1].Should().Contain("\"Meeting; mit Semikolon\"");
    }

    [Fact]
    public async Task Excel_contains_table_with_rows()
    {
        using var stream = new MemoryStream();

        await new ExcelExportService().ExportEventsAsync([Sample, Sample with { Id = "2", Title = "Zweiter" }], stream);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Termine");
        sheet.Cell(1, 2).GetString().Should().Be("Titel");
        sheet.Cell(3, 2).GetString().Should().Be("Zweiter");
        sheet.Tables.Should().ContainSingle();
    }
}

public sealed class FinnhubTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static FinnhubMarketDataProvider Create(StubHandler handler, string? apiKey = "test-key") => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://finnhub.io/api/v1/") },
        Microsoft.Extensions.Options.Options.Create(new MarketDataOptions { ApiKey = apiKey }),
        NullLogger<FinnhubMarketDataProvider>.Instance);

    [Fact]
    public async Task Parses_quotes_and_skips_unknown_symbols_without_key_in_url()
    {
        var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.Query.Contains("NOPE", StringComparison.Ordinal)
                ? """{"c":0,"d":null,"dp":null,"t":0}"""
                : """{"c":512.3,"d":2.1,"dp":0.41,"t":1767225600}""", Encoding.UTF8, "application/json"),
        });

        var result = await Create(handler).GetQuotesAsync(["SPY", "NOPE"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Price.Should().Be(512.3m);
        handler.Requests.Should().OnlyContain(r => !r.RequestUri!.ToString().Contains("test-key", StringComparison.Ordinal));
        handler.Requests[0].Headers.GetValues("X-Finnhub-Token").Should().Equal("test-key");
    }

    [Fact]
    public async Task Missing_api_key_is_a_config_error()
    {
        var result = await Create(new StubHandler(_ => throw new InvalidOperationException()), apiKey: null).GetQuotesAsync(["SPY"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("config.missing");
    }
}


public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Demo_container_resolves_and_runs_a_dry_sync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:UseDemoData"] = "true" }).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Substitute.For<ISecureTokenStore>())
            .AddSingleton(Substitute.For<IUrlLauncher>())
            .AddLifeDashboard(config, Path.Combine(Path.GetTempPath(), $"ld-di-{Guid.NewGuid():N}.db3"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var engine = provider.GetRequiredService<SyncEngine>();
        var calendars = provider.GetRequiredService<IEnumerable<ICalendarProvider>>().ToList();
        var own = new List<CalendarInfo>();
        foreach (var c in calendars)
        {
            own.AddRange((await c.GetCalendarsAsync()).Value.Where(x => x.IsWritable));
        }

        var clock = provider.GetRequiredService<IClock>();
        var report = await engine.RunAsync(new SyncRequest
        {
            MirrorCalendarIds = own.Select(c => c.Id).ToList(),
            Window = DateRange.Around(clock.UtcNow, TimeSpan.FromDays(7), TimeSpan.FromDays(30)),
            DryRun = true,
        });

        report.Created.Should().BeGreaterThan(0);
        provider.GetServices<IExportService>().Should().HaveCount(2);
        provider.GetServices<IAccountConnector>().Should().HaveCount(3);
        provider.GetRequiredService<IMarketDataProvider>().Should().BeOfType<FallbackMarketDataProvider>("Standard ist Yahoo (ohne Finnhub-Key kein Fallback)");
        provider.GetRequiredService<INewsProvider>().Feeds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Real_mode_wires_graph_google_ics_and_strava_without_connected_accounts()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:UseDemoData"] = "false" }).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ISecureTokenStore>(new LifeDashboard.Core.InMemory.InMemorySecureTokenStore())
            .AddSingleton(Substitute.For<IUrlLauncher>())
            .AddLifeDashboard(config, Path.Combine(Path.GetTempPath(), $"ld-di-{Guid.NewGuid():N}.db3"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var calendarProviders = provider.GetServices<ICalendarProvider>().ToList();
        calendarProviders.Select(p => p.Kind).Should().BeEquivalentTo([ProviderKind.Microsoft, ProviderKind.Google, ProviderKind.Ics]);
        provider.GetRequiredService<IActivityProvider>().Should().BeOfType<LifeDashboard.Infrastructure.Strava.StravaActivityProvider>();

        // Ohne verbundene Konten: keine Kalender, kein Fehler, und ein Sync läuft sauber durch.
        foreach (var p in calendarProviders)
        {
            (await p.GetCalendarsAsync()).IsSuccess.Should().BeTrue();
        }

        var report = await provider.GetRequiredService<SyncEngine>().RunAsync(new SyncRequest
        {
            MirrorCalendarIds = [],
            Window = DateRange.Around(DateTimeOffset.UtcNow, TimeSpan.FromDays(1), TimeSpan.FromDays(1)),
            DryRun = true,
        });
        report.Errors.Should().Be(0);
        report.Log.Should().NotContain(l => l.Level == "Error");
    }
}
