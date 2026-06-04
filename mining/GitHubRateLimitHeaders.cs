using System;
using System.Globalization;
using System.Linq;
using System.Net;
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
    public DateTimeOffset? ResetAt { get; private set; }

    public int CustomLimit { get; set; } = -1;

    public int EffectiveLimit => Math.Clamp(
        CustomLimit < 0 ? RateLimit : CustomLimit,
        0,
        RateLimit
    );

    public int EffectiveRemaining => EffectiveLimit - RateUsed;

    public bool ShouldThrow { get; set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        if (ShouldThrow && ResetAt is not null && ResetAt > DateTimeOffset.UtcNow)
        {
            throw new GitHubRateLimitedException
            {
                ResetAt = ResetAt ?? default,
                Remaining = RateRemaining,
                EffectiveRemaining = EffectiveRemaining
            };
        }

        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        SetField(ref rateLimit, GetNumericHeader(response.Headers, GitHubConst.RateLimitHeader, 0));
        var newRateRemaining = GetNumericHeader(response.Headers, GitHubConst.RateRemainingHeader, 0);
        SetField(ref rateRemaining, newRateRemaining);
        var newRateUsed = GetNumericHeader(response.Headers, GitHubConst.RateUsedHeader, 0);
        var newEffectiveRemaining = EffectiveLimit - newRateUsed;
        SetField(ref rateUsed, newRateUsed);
        var newRateReset = GetNumericHeader(response.Headers, GitHubConst.RateResetHeader, 0L);
        SetField(ref rateReset, newRateReset);
        RateLimitResource = response.Headers.GetValues(GitHubConst.RateResourceHeader).FirstOrDefault();
        var newRetryAfter = response.Headers.RetryAfter;
        RetryAfter = newRetryAfter;
        ResetAt = null;

        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || newEffectiveRemaining <= 0
            || (response.StatusCode == HttpStatusCode.Forbidden
                && (newRateRemaining == 0 || newRetryAfter is not null))
        )
        {
            var resetAt = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);
            if (newRateRemaining <= 0 && newRateReset > resetAt.ToUnixTimeSeconds())
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(newRateReset);
            }

            if (newRetryAfter?.Date is not null && newRetryAfter.Date > resetAt)
            {
                resetAt = newRetryAfter.Date.Value;
            }

            if (newRetryAfter?.Delta is not null && DateTimeOffset.UtcNow + newRetryAfter.Delta.Value > resetAt)
            {
                resetAt = DateTimeOffset.UtcNow + newRetryAfter.Delta.Value;
            }

            ResetAt = resetAt;
            if (ShouldThrow)
            {
                throw new GitHubRateLimitedException
                {
                    ResetAt = resetAt,
                    Remaining = newRateRemaining,
                    EffectiveRemaining = newEffectiveRemaining
                };
            }
        }

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
