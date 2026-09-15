using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using EveTrader.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace EveTrader.Application.Integration;

/// <summary>
/// База воркера под настоящим хостом и настоящим конвейером метрик: без моков,
/// без вызова защищённых методов рефлексией. Unit-тесты базы проверяют её логику,
/// этот — что она работает как <see cref="IHostedService" /> и что счётчики
/// доезжают до <see cref="MeterListener" />.
/// </summary>
public sealed class ScheduledWorkerHostingShould
{
    [Fact]
    public async Task RunCyclesUnderHostAndEmitCountersToMeter()
    {
        var measurements = new ConcurrentBag<(string Instrument, long Value)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MeterDiagnosticSource.SourceName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => measurements.Add((instrument.Name, value)));
        listener.Start();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        _ = builder.Services.AddSingleton<MeterDiagnosticSource>();
        _ = builder.Services.AddSingleton<IDiagnosticSource>(
            provider => provider.GetRequiredService<MeterDiagnosticSource>());
        _ = builder.Services.AddSingleton(TimeProvider.System);
        _ = builder.Services.AddSingleton<TickingWorker>();
        _ = builder.Services.AddHostedService(provider => provider.GetRequiredService<TickingWorker>());

        using IHost host = builder.Build();
        TickingWorker worker = host.Services.GetRequiredService<TickingWorker>();

        await host.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await worker.Completed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await host.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        listener.RecordObservableInstruments();

        measurements.Where(measurement => measurement.Instrument == "worker.Ticking.ticked")
            .Sum(measurement => measurement.Value)
            .ShouldBeGreaterThanOrEqualTo(3);

        measurements.ShouldContain(measurement => measurement.Instrument == "worker.Ticking.outcome.succeeded");
        measurements.ShouldNotContain(measurement => measurement.Instrument == "worker.Ticking.outcome.failed");
    }
}
