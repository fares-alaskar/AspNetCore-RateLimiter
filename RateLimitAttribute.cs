using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace RateLimitFilter
{
    /// <summary>
    /// Rate limit attribute that can be applied to controller actions.
    /// Limits how many requests a user or IP can make in a given time window.
    /// Uses <see cref="IDistributedCache"/> for tracking attempts.
    /// </summary>
    public class RateLimitAttribute : ActionFilterAttribute
    {
        private readonly TimeSpan _duration;          // How long the rate limit window lasts
        private readonly int _maxAttempts;            // Maximum allowed requests within the duration
        private readonly string _cacheKeyPrefix;      // Cache key prefix (useful if you have multiple rate limit rules)
        private readonly bool _useUserId;             // Whether to use UserId from JWT claims instead of IP address

        /// <summary>
        /// Constructor to configure rate limiting.
        /// </summary>
        /// <param name="maxAttempts">Maximum number of allowed requests per time window.</param>
        /// <param name="hours">Rate limit window in hours.</param>
        /// <param name="minutes">Rate limit window in minutes.</param>
        /// <param name="cacheKeyPrefix">Prefix used for cache keys.</param>
        /// <param name="useUserId">If true, rate limit per userId instead of IP address.</param>
        public RateLimitAttribute(int maxAttempts = 1, int hours = 0, int minutes = 0, string cacheKeyPrefix = "RL", bool useUserId = false)
        {
            _maxAttempts = maxAttempts;
            _duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            _cacheKeyPrefix = cacheKeyPrefix;
            _useUserId = useUserId;
        }

        /// <summary>
        /// Executed before the controller action.
        /// Checks whether the current request exceeds the configured rate limit.
        /// </summary>
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Resolve distributed cache (e.g., Redis, SQL server, in-memory)
            var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();

            string identifier;

            // Use UserId if configured, otherwise fall back to client IP
            if (_useUserId)
            {
                // You will need to implement your own extension method 
                // to extract UserId from JWT claims or authentication context.
                var userIdClaim = context.HttpContext.User.Claims
                    .FirstOrDefault(c => c.Type == "sub")?.Value; // Example: use "sub" claim

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    context.Result = new UnauthorizedObjectResult("You are not authenticated");
                    return;
                }

                identifier = $"user_{userIdClaim}";
            }
            else
            {
                // Get client IP address
                var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.MapToIPv4()?.ToString();
                if (string.IsNullOrEmpty(ipAddress))
                {
                    context.Result = new BadRequestObjectResult("Unable to read request origin information");
                    return;
                }

                identifier = $"ip_{ipAddress}";
            }

            // Build cache key based on user/IP and action name
            string cacheKey = $"{_cacheKeyPrefix}_{identifier}_{context.ActionDescriptor.DisplayName}";

            // Try to get existing rate limit entry from cache
            var cachedValue = await cache.GetStringAsync(cacheKey);

            RateLimitEntry entry;
            if (cachedValue != null)
            {
                // Deserialize cached entry
                entry = JsonSerializer.Deserialize<RateLimitEntry>(cachedValue)!;

                if (entry.ResetTime > DateTime.UtcNow)
                {
                    // Still within the rate limit window
                    if (entry.AttemptCount >= _maxAttempts)
                    {
                        // Too many requests → return 429 Too Many Requests
                        context.Result = new ObjectResult($"You are allowed to make {_maxAttempts} request(s) every {_duration.TotalMinutes} minute(s). Please try again later.")
                        {
                            StatusCode = StatusCodes.Status429TooManyRequests
                        };
                        return;
                    }
                    else
                    {
                        // Increment request count
                        entry.AttemptCount++;
                    }
                }
                else
                {
                    // Window expired → reset counter
                    entry = new RateLimitEntry
                    {
                        AttemptCount = 1,
                        FirstAttemptTime = DateTime.UtcNow,
                        ResetTime = DateTime.UtcNow + _duration
                    };
                }
            }
            else
            {
                // No cache entry found → first request
                entry = new RateLimitEntry
                {
                    AttemptCount = 1,
                    FirstAttemptTime = DateTime.UtcNow,
                    ResetTime = DateTime.UtcNow + _duration
                };
            }

            // Save updated entry back to cache
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = entry.ResetTime
            };

            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(entry), options);

            // Continue with action execution
            await next();
        }

        /// <summary>
        /// Internal class to store rate limiting state in cache.
        /// </summary>
        private class RateLimitEntry
        {
            public int AttemptCount { get; set; }           // Number of requests made in the current window
            public DateTime FirstAttemptTime { get; set; }  // When the first request was made
            public DateTime ResetTime { get; set; }         // When the limit window resets
        }
    }
}
