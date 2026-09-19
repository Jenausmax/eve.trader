namespace EveTrader.Domain.Book;

/// <summary>Размеры таблиц открытой адресации.</summary>
public static class HashHelpers
{
    public static int PowerOfTwoAtLeast(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);

        var result = 1;

        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
