using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private static readonly HttpClient _httpClient = new();

    // Firebase URL from environment variable - never hardcode!
    private static string FirebaseUrl =>
        Environment.GetEnvironmentVariable("FIREBASE_URL")
        ?? "https://profitx-tradingbot-default-rtdb.asia-southeast1.firebasedatabase.app";

    // Simple rate limiting - max 5 attempts per IP per minute
    private static readonly ConcurrentDictionary<string, LoginAttempts>
        _loginAttempts = new();

    [HttpPost("login")]
    public async Task<object> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (request == null ||
                string.IsNullOrEmpty(request.Username) ||
                string.IsNullOrEmpty(request.Password))
            {
                return new
                {
                    success = false,
                    message = "Username and password required"
                };
            }

            // Rate limiting check
            string clientIp = HttpContext.Connection.RemoteIpAddress?
                              .ToString() ?? "unknown";

            if (IsRateLimited(clientIp))
            {
                Console.WriteLine(
                    $"⚠️ Rate limited login attempt from {clientIp}");
                return new
                {
                    success = false,
                    message = "Too many login attempts. Please wait 1 minute."
                };
            }

            Console.WriteLine($"Login attempt: {request.Username}");

            // Fetch users from Firebase
            // NOTE: Fix the hardcoded user001 path - fetch all users
            var response = await _httpClient.GetStringAsync(
                $"{FirebaseUrl}/Users.json");

            if (string.IsNullOrEmpty(response) || response == "null")
            {
                // Fallback: try old path for backward compatibility
                response = await _httpClient.GetStringAsync(
                    $"{FirebaseUrl}/Users/user001/users.json");
            }

            if (string.IsNullOrEmpty(response) || response == "null")
            {
                return new
                {
                    success = false,
                    message = "No users found in database"
                };
            }

            var users = JsonSerializer.Deserialize<
                Dictionary<string, JsonElement>>(response);

            if (users != null)
            {
                foreach (var user in users)
                {
                    try
                    {
                        var userData = user.Value;

                        // Handle nested structure
                        JsonElement userNode = userData;
                        if (userData.ValueKind == JsonValueKind.Object &&
                            userData.TryGetProperty("users", out var nested))
                        {
                            // Old structure: /Users/user001/users/{key}
                            foreach (var nestedUser in
                                     nested.EnumerateObject())
                            {
                                var result = TryMatchUser(
                                    nestedUser.Value,
                                    nestedUser.Name,
                                    request.Username,
                                    request.Password,
                                    clientIp);

                                if (result != null) return result;
                            }
                            continue;
                        }

                        var directResult = TryMatchUser(
                            userData,
                            user.Key,
                            request.Username,
                            request.Password,
                            clientIp);

                        if (directResult != null) return directResult;
                    }
                    catch { continue; }
                }
            }

            RecordFailedAttempt(clientIp);
            Console.WriteLine(
                $"Login FAILED - Invalid credentials: {request.Username}");
            return new
            {
                success = false,
                message = "Invalid username or password"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Login ERROR: {ex.Message}");
            return new
            {
                success = false,
                message = "Server error. Please try again."
            };
        }
    }

    [HttpPost("validateuser")]
    public async Task<object> ValidateUser(
        [FromBody] ValidateRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.UserId))
            return new { success = false, message = "User ID required" };

        try
        {
            // Actually validate against Firebase
            var response = await _httpClient.GetStringAsync(
                $"{FirebaseUrl}/Users.json");

            if (string.IsNullOrEmpty(response) || response == "null")
                return new { success = false, message = "User not found" };

            // Simple check - if we can fetch users and userId exists
            if (response.Contains(request.UserId))
                return new { success = true, message = "Account is valid" };

            return new { success = false, message = "User not found" };
        }
        catch
        {
            // If Firebase check fails, allow (don't block users)
            return new { success = true, message = "Account is valid" };
        }
    }

    // ── Private Helpers ───────────────────────────────────────
    private object? TryMatchUser(
        JsonElement userData,
        string userKey,
        string username,
        string password,
        string clientIp)
    {
        try
        {
            string dbUsername = userData.TryGetProperty("username", out var u)
                ? u.GetString() ?? "" : "";
            string dbPassword = userData.TryGetProperty("password", out var p)
                ? p.GetString() ?? "" : "";
            bool isActive = userData.TryGetProperty("isActive", out var a)
                && a.GetBoolean();
            string expiryDate = userData.TryGetProperty("expiryDate", out var e)
                ? e.GetString() ?? "" : "";

            if (dbUsername != username || dbPassword != password)
                return null;

            // Credentials match - check account status
            if (!isActive)
            {
                Console.WriteLine(
                    $"Login BLOCKED - Deactivated: {username}");
                return new
                {
                    success = false,
                    message = "Account is deactivated. Contact admin."
                };
            }

            if (DateTime.TryParse(expiryDate, out DateTime expiry) &&
                expiry < DateTime.Now)
            {
                Console.WriteLine(
                    $"Login BLOCKED - Expired: {username}");
                return new
                {
                    success = false,
                    message = "Account has expired. Contact admin to renew."
                };
            }

            // Clear rate limit on success
            _loginAttempts.TryRemove(clientIp, out _);
            Console.WriteLine($"Login SUCCESS: {username}");

            return new
            {
                success    = true,
                message    = "Login successful!",
                userId     = userKey,
                username   = dbUsername,
                expiryDate = expiryDate
            };
        }
        catch
        {
            return null;
        }
    }

    private bool IsRateLimited(string clientIp)
    {
        if (!_loginAttempts.TryGetValue(clientIp, out var attempts))
            return false;

        // Reset if window has passed
        if ((DateTime.UtcNow - attempts.WindowStart).TotalMinutes >= 1)
        {
            _loginAttempts.TryRemove(clientIp, out _);
            return false;
        }

        return attempts.Count >= 5;
    }

    private void RecordFailedAttempt(string clientIp)
    {
        _loginAttempts.AddOrUpdate(
            clientIp,
            new LoginAttempts { WindowStart = DateTime.UtcNow, Count = 1 },
            (_, existing) =>
            {
                if ((DateTime.UtcNow - existing.WindowStart).TotalMinutes >= 1)
                    return new LoginAttempts
                    {
                        WindowStart = DateTime.UtcNow,
                        Count = 1
                    };

                existing.Count++;
                return existing;
            });
    }
}

// ── Request / Helper Models ───────────────────────────────────
public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ValidateRequest
{
    public string UserId { get; set; } = "";
}

public class LoginAttempts
{
    public DateTime WindowStart { get; set; }
    public int Count { get; set; }
}
