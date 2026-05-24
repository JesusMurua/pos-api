using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using POS.Domain.DTOs.Catalogs;

namespace POS.API.Extensions;

/// <summary>
/// HTTP negotiation helper for <see cref="CatalogResponse{T}"/> envelopes.
/// Centralizes the <c>ETag</c>, <c>Cache-Control</c>, and
/// <c>If-None-Match</c> handling defined by BDD-021 §5.1 / §6.1.C so every
/// catalog action stays a one-liner.
/// </summary>
public static class CatalogResponseExtensions
{
    /// <summary>Uniform <c>Cache-Control</c> directive — see BDD-021 §7.3.</summary>
    private const string CacheControlDirective = "public, max-age=3600, must-revalidate";

    /// <summary>
    /// Emits the cached envelope as either <c>200 OK</c> with body or
    /// <c>304 Not Modified</c> with no body, depending on whether the
    /// incoming <c>If-None-Match</c> header matches the envelope's ETag.
    /// The <c>ETag</c> and <c>Cache-Control</c> response headers are
    /// always set, on both 200 and 304 responses.
    /// </summary>
    /// <typeparam name="T">DTO type carried by the envelope.</typeparam>
    /// <param name="controller">
    /// The invoking controller — used to read request headers and write
    /// response headers.
    /// </param>
    /// <param name="envelope">Envelope returned by <c>ICatalogService.Get*Async</c>.</param>
    /// <returns><c>200 OK</c> with payload, or <c>304 Not Modified</c>.</returns>
    public static IActionResult ETagResult<T>(this ControllerBase controller, CatalogResponse<T> envelope)
    {
        var response = controller.Response;
        response.Headers[HeaderNames.ETag] = envelope.ETag;
        response.Headers[HeaderNames.CacheControl] = CacheControlDirective;

        if (IsClientCacheValid(controller, envelope.ETag))
        {
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        return new OkObjectResult(envelope.Payload);
    }

    /// <summary>
    /// Parses the <c>If-None-Match</c> request header (RFC 9110 §13.1.2)
    /// and returns true when any supplied entity tag — or the special
    /// <c>*</c> value — matches the envelope's ETag. Tolerates the
    /// optional <c>W/</c> weak-validator prefix.
    /// </summary>
    private static bool IsClientCacheValid(ControllerBase controller, string serverETag)
    {
        if (!controller.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var headerValues))
        {
            return false;
        }

        foreach (var raw in headerValues)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token == "*")
                {
                    return true;
                }

                var candidate = token.StartsWith("W/", StringComparison.Ordinal)
                    ? token[2..]
                    : token;

                if (string.Equals(candidate, serverETag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
