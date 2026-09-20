namespace EveTrader.Domain.Scope;

/// <summary>Форма охвата живого наблюдения.</summary>
public enum ScopeKind
{
    /// <summary>Торговые хабы — основа.</summary>
    Hubs = 0,

    /// <summary>Произвольное подмножество регионов.</summary>
    Regions = 1,

    /// <summary>Все пригодные регионы.</summary>
    AllViable = 2,

    /// <summary>Единственный регион.</summary>
    Single = 3,
}
