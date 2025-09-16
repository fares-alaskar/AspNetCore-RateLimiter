# 🔒 AspNetCore-RateLimiter
An **ASP.NET Core Action Filter** for **rate limiting API requests** using `IDistributedCache`.  
Helps protect your API from abuse by limiting how many requests a user or IP address can make within a specified time window.  

---

## ✨ Features
- ✅ Simple attribute-based usage: `[RateLimit]`
- ✅ Configurable **max attempts** and **time window**
- ✅ Supports **IP-based** or **UserId-based** throttling
- ✅ Works with any `IDistributedCache` provider (Redis, SQL Server, or in-memory cache)
- ✅ Returns **HTTP 429 Too Many Requests** when the limit is exceeded

---

## 📦 Installation

Add the file **`RateLimitAttribute.cs`** to your project.  
Make sure you have `Microsoft.Extensions.Caching.StackExchangeRedis` or another `IDistributedCache` provider configured.

Example: **Redis setup in `Program.cs`**

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "RateLimit_";
});
```
---

## 🚀 Usage
1. Apply to a Controller Action
```csharp
[HttpPost("[action]")]
[RateLimit(maxAttempts: 3, minutes: 1)]
public IActionResult SubmitForm()
{
    return Ok("Form submitted successfully!");
}
```
This allows 3 requests per minute per IP address.
After the limit is reached, further requests return HTTP 429 Too Many Requests.

2. Use per UserId instead of IP
```csharp
[HttpPost("[action]")]
[RateLimit(maxAttempts: 5, minutes: 10, useUserId: true)]
public IActionResult SecureAction()
{
    return Ok("Action completed!");
}
```

👉 Here, the UserId from JWT claims is used instead of the client’s IP address.
Make sure your JWT contains a sub (subject) or userId claim.

---
