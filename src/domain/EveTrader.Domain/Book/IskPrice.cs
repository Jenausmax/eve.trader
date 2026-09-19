using System.Globalization;

namespace EveTrader.Domain.Book;

/// <summary>
/// Цена в сотых долях ISK целым числом.
///
/// Не <see cref="double" /> и не <see cref="decimal" />: первый теряет точность ровно
/// там, где она нужна — на сравнении двух близких цен, а второй слишком дорог в цикле на
/// миллион ордеров. Сотая доля ISK — предел, который различает сама игра.
/// </summary>
public readonly record struct IskPrice : IComparable<IskPrice>
{
    public const int Scale = 100;

    private IskPrice(long cents)
    {
        Cents = cents;
    }

    public long Cents { get; }

    public static IskPrice Zero { get; }

    public static IskPrice FromCents(long cents) =>
        cents >= 0 ? new IskPrice(cents) : throw new ArgumentOutOfRangeException(nameof(cents), cents, "Цена неотрицательна");

    /// <summary>
    /// Из десятичного представления источника. Округление к ближайшему: источник даёт
    /// не больше двух знаков, и всё, что дробнее, — шум представления.
    /// </summary>
    public static IskPrice FromIsk(decimal isk) =>
        isk >= 0m
            ? new IskPrice((long)decimal.Round(isk * Scale, 0, MidpointRounding.ToEven))
            : throw new ArgumentOutOfRangeException(nameof(isk), isk, "Цена неотрицательна");

    public decimal ToIsk() => Cents / (decimal)Scale;

    public int CompareTo(IskPrice other) => Cents.CompareTo(other.Cents);

    public static bool operator <(IskPrice left, IskPrice right)
    {
        return left.Cents < right.Cents;
    }

    public static bool operator <=(IskPrice left, IskPrice right)
    {
        return left.Cents <= right.Cents;
    }

    public static bool operator >(IskPrice left, IskPrice right)
    {
        return left.Cents > right.Cents;
    }

    public static bool operator >=(IskPrice left, IskPrice right)
    {
        return left.Cents >= right.Cents;
    }

    public override string ToString() =>
        ToIsk().ToString("0.00", CultureInfo.InvariantCulture);
}
