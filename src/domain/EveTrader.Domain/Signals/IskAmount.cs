using System.Globalization;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Знаковая сумма в сотых долях ISK.
///
/// Отдельный тип рядом с <see cref="Book.IskPrice" />, а не тот же
/// самый: цена неотрицательна по построению, и это её полезное свойство — отрицательной
/// цены в стакане не бывает. Маржа же бывает отрицательной, и именно отрицательная
/// интереснее всего: она отвечает на вопрос, ради которого комиссии и считаются.
/// </summary>
public readonly record struct IskAmount : IComparable<IskAmount>
{
    private IskAmount(long cents)
    {
        Cents = cents;
    }

    public long Cents { get; }

    public static IskAmount Zero { get; }

    public bool IsPositive => Cents > 0;

    public static IskAmount FromCents(long cents) => new(cents);

    /// <summary>Из десятичного представления; округление к ближайшему чётному, как у цены.</summary>
    public static IskAmount FromIsk(decimal isk) =>
        new((long)decimal.Round(isk * Book.IskPrice.Scale, 0, MidpointRounding.ToEven));

    public decimal ToIsk() => Cents / (decimal)Book.IskPrice.Scale;

    public int CompareTo(IskAmount other) => Cents.CompareTo(other.Cents);

    public static IskAmount operator +(IskAmount left, IskAmount right)
    {
        return new(left.Cents + right.Cents);
    }

    public static IskAmount operator -(IskAmount left, IskAmount right)
    {
        return new(left.Cents - right.Cents);
    }

    public static bool operator <(IskAmount left, IskAmount right)
    {
        return left.Cents < right.Cents;
    }

    public static bool operator <=(IskAmount left, IskAmount right)
    {
        return left.Cents <= right.Cents;
    }

    public static bool operator >(IskAmount left, IskAmount right)
    {
        return left.Cents > right.Cents;
    }

    public static bool operator >=(IskAmount left, IskAmount right)
    {
        return left.Cents >= right.Cents;
    }

    public static IskAmount Add(IskAmount left, IskAmount right) => left + right;

    public static IskAmount Subtract(IskAmount left, IskAmount right) => left - right;

    public override string ToString() => ToIsk().ToString("0.00", CultureInfo.InvariantCulture);
}
