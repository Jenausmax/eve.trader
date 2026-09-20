using System.Net;
using ICSharpCode.SharpZipLib.BZip2;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Загрузка одного файла архива: условный запрос, проверка целости, распаковка bzip2.
///
/// Общая на дневную историю и на снимки стакана. Вежливость к источнику — не украшение,
/// а условие лицензии, и держаться она должна в одном месте: два адаптера с разными
/// представлениями о том, что такое «вежливо», — это отсутствие договорённости, а не
/// две договорённости.
/// </summary>
internal static class ArchiveDownload
{
    /// <summary>
    /// Условный запрос за файлом целиком.
    ///
    /// Файл скачивается целиком, и только потом распаковывается. Распаковка прямо из
    /// сокета выглядит экономнее, но обрыв соединения приходит в ней не сетевой ошибкой,
    /// которую можно повторить, а порчей архива: BZip2 падает на «end of compressed
    /// stream», и повторять уже нечего — поток проглочен. Здесь обрыв виден как обрыв и
    /// чинится повтором.
    /// </summary>
    /// <returns>
    /// Тело и время изменения файла. Тело пусто, если источник ответил «не изменилось»
    /// либо файла не публикует — исход различается по <c>Status</c>.
    /// </returns>
    public static async Task<(byte[]? Body, DateTimeOffset LastModified, HttpStatusCode Status)> FetchAsync(
        HttpClient client,
        string path,
        DateTimeOffset? ifModifiedSince,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (ifModifiedSince is { } since)
        {
            request.Headers.IfModifiedSince = since;
        }

        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound)
        {
            return (null, DateTimeOffset.UnixEpoch, response.StatusCode);
        }

        _ = response.EnsureSuccessStatusCode();

        DateTimeOffset lastModified = response.Content.Headers.LastModified ?? DateTimeOffset.UnixEpoch;
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // Объявленная длина и есть проверка целости: обрыв на середине тела иначе
        // доехал бы до разбора и выглядел бы как дефект данных, а не как обрыв.
        return response.Content.Headers.ContentLength is { } declared && body.LongLength != declared
            ? throw new EndOfStreamException(
                $"Файл {path}: источник объявил {declared} байт, получено {body.LongLength}")
            : ((byte[]?)body, lastModified, response.StatusCode);
    }

    /// <summary>
    /// Читатель распакованного CSV поверх скачанного тела. Освобождение читателя
    /// освобождает всю цепочку.
    /// </summary>
    public static StreamReader Read(byte[] body) =>
        new(new BZip2InputStream(new MemoryStream(body, writable: false)));

    /// <summary>
    /// Обрыв загрузки, таймаут и порча архива — всё это поводы повторить. Ошибка разбора
    /// повтором не лечится: формат файла от этого не изменится.
    /// </summary>
    public static bool IsTransient(Exception failure) =>
        failure is HttpRequestException or IOException or TaskCanceledException;
}
