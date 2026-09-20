using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>
/// Отпечаток NPC после замера на архиве (<c>add-orderbook-archive-import</c>, задача 2.2).
///
/// Здесь закреплены ровно те сочетания признаков, которые нашлись в снимке 2026-09-17 у
/// ордеров с двинувшимся <c>issued</c> и неизменной ценой. Стартовый отпечаток не узнавал
/// 4 185 из 7 443 таких ордеров — то есть каждое второе пополнение склада уехало бы в
/// обучение как действие конкурента.
/// </summary>
public sealed class NpcFingerprintShould
{
    private static OrderSnapshot Order(long id, short duration, long remain, long total, bool isBuy) =>
        new(id, 34, 60003760, isBuy, IskPrice.FromIsk(100m), remain, total, duration, 0);

    [Fact]
    public void RecogniseAnNpcSellOrderWithAnIdentifierBelowTheOldThreshold() =>
        NpcFingerprint.Default.Matches(Order(911_190_994, 365, 1000, 1000, isBuy: false)).ShouldBeTrue();

    [Fact]
    public void RecogniseAnNpcOrderWhoseIdentifierIsAboveTheOldThreshold() =>
        // Порог в пять миллиардов отсекал настоящие ордера NPC: в снимке они доходят до
        // 7 423 778 237, а игроцкие начинаются с 2 041 496 592 — диапазоны перекрываются
        // почти целиком, и разделять по ним нечего.
        NpcFingerprint.Default.Matches(Order(7_423_778_237, 365, 1000, 1000, isBuy: false)).ShouldBeTrue();

    [Fact]
    public void RecogniseAnNpcBuyOrder() =>
        // Восьмая часть годовых ордеров снимка — покупка (79 949 из 547 743).
        NpcFingerprint.Default.Matches(Order(3_000_000_000, 365, 1000, 1000, isBuy: true)).ShouldBeTrue();

    [Theory]
    [InlineData((short)1)]
    [InlineData((short)3)]
    [InlineData((short)7)]
    [InlineData((short)14)]
    [InlineData((short)30)]
    [InlineData((short)90)]
    public void NotRecogniseAnyDurationAvailableToAPlayer(short duration) =>
        // Игрок выбирает длительность из этого набора; года в нём нет, и в снимке
        // встречаются только эти семь значений.
        NpcFingerprint.Default.Matches(Order(1_000_000_000, duration, 100, 100, isBuy: false)).ShouldBeFalse();

    [Fact]
    public void NotRecogniseAPartiallyFilledOrder() =>
        // Все 547 743 годовых ордера снимка шли с полным остатком — ни одного исключения.
        NpcFingerprint.Default.Matches(Order(1_000_000_000, 365, 999, 1000, isBuy: false)).ShouldBeFalse();

    [Fact]
    public void RecogniseNobodyWhenTheSetCarriesNoNpcOrders()
    {
        // Длительности −1 не бывает ни у одного ордера. Нулевая для этого не годится:
        // её несут мгновенные ордера.
        NpcFingerprint.None.Matches(Order(1, 0, 100, 100, isBuy: false)).ShouldBeFalse();
        NpcFingerprint.None.Matches(Order(1, 365, 100, 100, isBuy: false)).ShouldBeFalse();
    }

    [Fact]
    public void StillHonourAnIdentifierThresholdWhenOneIsAskedFor()
    {
        // Порог не выброшен, а выключен по умолчанию: настройка остаётся на случай,
        // если источник когда-нибудь начнёт разводить диапазоны.
        NpcFingerprint bounded = NpcFingerprint.Default with { MaxOrderId = 5_000_000_000L };

        bounded.Matches(Order(4_000_000_000, 365, 100, 100, isBuy: false)).ShouldBeTrue();
        bounded.Matches(Order(6_000_000_000, 365, 100, 100, isBuy: false)).ShouldBeFalse();
    }
}
