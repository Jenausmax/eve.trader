using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Integration.Replay;

/// <summary>
/// Записанный прогон: последовательность наблюдений одного региона, какую дал бы живой
/// сбор. Подаётся на тот же вход, что и всё остальное.
/// </summary>
internal sealed class RecordedRun(string name, IReadOnlyList<RegionObservation> observations) : IObservationSource
{
    public string Name => name;

    public async IAsyncEnumerable<RegionObservation> ObserveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (RegionObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return observation;
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    public static RegionId Forge { get; } = RegionId.From(10000002);

    public static DateTimeOffset At(int minutes) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    public static RegionObservation Observation(
        int minute,
        IReadOnlyList<OrderSnapshot> orders,
        CoverageOutcome outcome = CoverageOutcome.Success,
        int pagesReceived = 1,
        int pagesExpected = 1) =>
        new(
            Forge,
            ObservationId.From($"live-{Forge.Value}-{At(minute):yyyyMMddTHHmmssZ}"),
            TimeRange.Between(At(minute), At(minute).AddSeconds(20)),
            outcome,
            TimeSpan.FromMinutes(5),
            orders,
            pagesReceived,
            pagesExpected,
            IsBaseline: false,
            FailureReason: null);

    public static OrderSnapshot Order(
        long id,
        decimal price,
        long remain = 100,
        int issuedMinute = 0,
        bool isBuy = false,
        int typeId = 34) =>
        new(id, typeId, 60003760, isBuy, IskPrice.FromIsk(price), remain, 100, 90,
            At(issuedMinute).ToUnixTimeSeconds());
}
