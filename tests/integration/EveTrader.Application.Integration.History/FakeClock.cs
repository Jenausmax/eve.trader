namespace EveTrader.Application.Integration.History;

/// <summary>
/// Часы под управлением теста.
///
/// Нужны ровно затем, что время получения в покрытии служит валидатором условного
/// запроса: «отдавай файл, если он менялся после нашей загрузки». В бою наши часы
/// всегда впереди времени изменения файла, и всё сходится; в тесте с вымышленным
/// календарём настоящие часы оказались бы на годы впереди и спрятали бы досинхронизацию.
/// </summary>
internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
