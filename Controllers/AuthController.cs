using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private static readonly HttpClient _httpClient = new();
    private const string FirebaseUrl = "https://profitx-tradingbot-default-rtdb.asia-southeast1.firebasedatabase.app";

    [HttpPost("login")]
    public async Task<object> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                return new { success = false, message = "Username and password required" };
            }

            Console.WriteLine("Login attempt: " + request.Username);

            var response = await _httpClient.GetStringAsync($"{FirebaseUrl}/Users/user001/users.json");

            if (string.IsNullOrEmpty(response) || response == "null")
            {
                return new { success = false, message = "No users found in database" };
            }

            var users = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response);

            if (users != null)
            {
                foreach (var user in users)
                {
                    try
                    {
                        var userData = user.Value;
                        string dbUsername = userData.GetProperty("username").GetString() ?? "";
                        string dbPassword = userData.GetProperty("password").GetString() ?? "";
                        bool isActive = userData.GetProperty("isActive").GetBoolean();
                        string expiryDate = userData.GetProperty("expiryDate").GetString() ?? "";

                        if (dbUsername == request.Username && dbPassword == request.Password)
                        {
                            if (!isActive)
                            {
                                Console.WriteLine("Login BLOCKED - Account deactivated: " + request.Username);
                                return new { success = false, message = "Account is deactivated. Contact admin." };
                            }

                            if (DateTime.TryParse(expiryDate, out DateTime expiry) && expiry < DateTime.Now)
                            {
                                Console.WriteLine("Login BLOCKED - Account expired: " + request.Username);
                                return new { success = false, message = "Account has expired. Contact admin to renew." };
                            }

                            Console.WriteLine("Login SUCCESS: " + request.Username);

                            return new
                            {
                                success = true,
                                message = "Login successful!",
                                userId = user.Key,
                                username = dbUsername,
                                expiryDate = expiryDate
                            };
                        }
                    }
                    catch { continue; }
                }
            }

            Console.WriteLine("Login FAILED - Invalid credentials: " + request.Username);
            return new { success = false, message = "Invalid username or password" };
        }
        catch (Exception ex)
        {
            Console.WriteLine("Login ERROR: " + ex.Message);
            return new { success = false, message = "Server error: " + ex.Message };
        }
    }

    [HttpPost("validateuser")]
    public object ValidateUser([FromBody] ValidateRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.UserId))
        {
            return new { success = false, message = "User ID required" };
        }

        return new { success = true, message = "Account is valid" };
    }
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ValidateRequest
{
    public string UserId { get; set; } = "";
}
