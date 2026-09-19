namespace EveTrader.Domain.Book;

/// <summary>
/// Поиск ордера по идентификатору внутри наблюдения — открытая адресация поверх массивов.
///
/// Не <see cref="Dictionary{TKey, TValue}" />: на миллионе записей тот стоит десятки
/// мегабайт накладных расходов и даёт худшую локальность, а дифф двух наблюдений — это
/// миллион обращений подряд. Массивы переиспользуются между наблюдениями, поэтому
/// на цикл не приходится ни одной аллокации.
/// </summary>
public sealed class OrderIndex
{
    /// <summary>Идентификатор ордера; ноль означает пустой слот — источник их не выдаёт.</summary>
    private long[] keys = new long[16];

    private int[] slots = new int[16];

    private int mask = 15;

    public int Count { get; private set; }

    public int Capacity => keys.Length;

    /// <summary>
    /// Готовит индекс под указанное число ордеров. Заполнение держится ниже половины —
    /// на открытой адресации дальше резко растёт длина пробы.
    /// </summary>
    public void Reset(int expected)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var required = HashHelpers.PowerOfTwoAtLeast(Math.Max(16, expected * 2));

        if (required > keys.Length)
        {
            keys = new long[required];
            slots = new int[required];
        }
        else
        {
            Array.Clear(keys, 0, keys.Length);
        }

        mask = keys.Length - 1;
        Count = 0;
    }

    public void Add(long orderId, int slot)
    {
        if (orderId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orderId), "Ноль зарезервирован под пустой слот");
        }

        var position = OrderIndexProbe.Slot(orderId, mask);

        while (keys[position] != 0)
        {
            if (keys[position] == orderId)
            {
                slots[position] = slot;

                return;
            }

            position = (position + 1) & mask;
        }

        keys[position] = orderId;
        slots[position] = slot;
        Count++;
    }

    public bool TryGet(long orderId, out int slot)
    {
        var position = OrderIndexProbe.Slot(orderId, mask);

        while (keys[position] != 0)
        {
            if (keys[position] == orderId)
            {
                slot = slots[position];

                return true;
            }

            position = (position + 1) & mask;
        }

        slot = -1;

        return false;
    }
}
