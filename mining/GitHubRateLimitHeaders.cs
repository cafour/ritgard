using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Ritgard.Mining;

public sealed class GitHubRateLimitHeaders : DelegatingHandler
{
    private int rateLimit = int.MaxValue;
    private int rateRemaining = int.MaxValue;
    private int rateUsed = int.MinValue;
    private long rateReset = long.MinValue;

    public int RateLimit => rateLimit;
    public int RateRemaining => rateRemaining;
    public int RateUsed => rateUsed;
    public DateTimeOffset RateReset => DateTimeOffset.FromUnixTimeSeconds(rateReset);
    public string? RateLimitResource { get; private set; }
    public RetryConditionHeaderValue? RetryAfter { get; private set; }

    public int CustomLimit { get; set; } = -1;
    public int EffectiveLimit => Math.Clamp(
        CustomLimit < 0 ? RateLimit : CustomLimit,
        0,
        RateLimit
    );

    public int EffectiveRemaining => EffectiveLimit - RateUsed;

    public bool ShouldThrow { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        SetField(ref rateLimit, GetNumericHeader(response.Headers, GitHubConst.RateLimitHeader, 0));
        SetField(ref rateRemaining, GetNumericHeader(response.Headers, GitHubConst.RateRemainingHeader, 0));
        SetField(ref rateUsed, GetNumericHeader(response.Headers, GitHubConst.RateUsedHeader, 0));
        SetField(ref rateReset, GetNumericHeader(response.Headers, GitHubConst.RateResetHeader, 0L));
        RateLimitResource = response.Headers.GetValues(GitHubConst.RateResourceHeader).FirstOrDefault();
        RetryAfter = response.Headers.RetryAfter;

        return response;
    }

    private static T GetNumericHeader<T>(HttpResponseHeaders headers, string headerName, T fallback)
        where T : INumber<T>
    {
        if (!headers.TryGetValues(headerName, out var values))
        {
            return fallback;
        }

        if (!T.TryParse(values.SingleOrDefault(), CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return value;
    }

    private static void SetField<T>(ref T field, T value, int attempts = 3)
        where T : INumber<T>
    {
        for (int attempt = 0; attempt < attempts; ++attempt)
        {
            if (field == value)
            {
                break;
            }
            Interlocked.CompareExchange(ref field, value, field);
        }
    }
}
