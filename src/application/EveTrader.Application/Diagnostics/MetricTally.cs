namespace EveTrader.Application.Diagnostics;

/// <summary>Накопленное по одному инструменту: сумма, число замеров и последнее значение.</summary>
public sealed class MetricTally
{
    private readonly Lock guard = new();

    private double total;

    private long count;

    public double Total
    {
        get
        {
            lock (guard)
            {
                return total;
            }
        }
    }

    public long Count
    {
        get
        {
            lock (guard)
            {
                return count;
            }
        }
    }

    /// <summary>Последнее записанное значение — единственное осмысленное у датчика.</summary>
    public double Last
    {
        get
        {
            lock (guard)
            {
                return field;
            }
        }

        private set;
    }

    public void Record(double value)
    {
        lock (guard)
        {
            total += value;
            count++;
            Last = value;
        }
    }
}
