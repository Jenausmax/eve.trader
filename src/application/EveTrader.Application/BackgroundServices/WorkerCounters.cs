using System.Reflection;
using EveTrader.Application.Diagnostics;

namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Чтение счётчиков результата цикла и их эмиссия. Вынесено из
/// <see cref="ScheduledWorkerBase" /> отдельным типом: приватные методы в
/// продакшн-коде запрещены (<c>csharp/class-layout-and-tooling.md</c> §1a).
/// </summary>
internal static class WorkerCounters
{
    /// <summary>
    /// Эмитит счётчики, помеченные <see cref="CounterAttribute" />. Возвращает
    /// <see langword="true" />, если хотя бы один счётчик положительный — это и есть
    /// «работа была» для <see cref="AdaptivePollSchedule" />.
    /// </summary>
    public static bool Emit(IDiagnosticSource? diagnostics, string workerName, object? counters)
    {
        if (counters is null)
        {
            return false;
        }

        var didWork = false;

        foreach (PropertyInfo property in counters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<CounterAttribute>() is not { } attribute)
            {
                continue;
            }

            var value = property.GetValue(counters) switch
            {
                long number => number,
                int number => number,
                _ => (long?)null,
            };

            if (value is not { } counted)
            {
                continue;
            }

            diagnostics?.Counter($"worker.{workerName}.{attribute.Name}", "{items}").Add(counted);

            if (counted > 0)
            {
                didWork = true;
            }
        }

        return didWork;
    }

    /// <summary>
    /// Исход цикла. Двумя счётчиками, а не одним с тегом: тег теряется в любом читателе,
    /// который смотрит на инструмент целиком, — а «сколько циклов упало» спрашивают
    /// именно так.
    /// </summary>
    public static void EmitOutcome(IDiagnosticSource? diagnostics, string workerName, bool failed) =>
        diagnostics?.Counter($"worker.{workerName}.outcome.{(failed ? "failed" : "succeeded")}", "{cycles}").Add(1);
}
