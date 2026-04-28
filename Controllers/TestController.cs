using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Text;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private const string AppVersion = "3.1";

    [HttpGet("ping")]
    public object Ping()
    {
        return new
        {
            success   = true,
            message   = "ProfitX Cloud Trading Server is running!",
            time      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            version   = AppVersion,
            developer = "London Cyber 2026"
        };
    }

    [HttpGet("status")]
    public object Status()
    {
        return new
        {
            success     = true,
            serverName  = "ProfitX Cloud Trading Server",
            version     = AppVersion,
            environment = "Render Cloud"
        };
    }

    // ── Cleanup - ADMIN KEY PROTECTED ─────────────────────────
    [HttpGet("cleanup")]
    public async Task<object> Cleanup([FromQuery] string adminKey = "")
    {
        // Require admin key - never expose this endpoint publicly
        string expectedKey =
            Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";

        if (string.IsNullOrEmpty(expectedKey) || adminKey != expectedKey)
        {
            Console.WriteLine("⚠️ Unauthorized cleanup attempt!");
            return Unauthorized(new
            {
                success = false,
                message = "Unauthorized"
            });
        }

        string apiToken =
            Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        if (string.IsNullOrEmpty(apiToken))
            return new { success = false, message = "No MetaApi token" };

        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("auth-token", apiToken);
        http.Timeout = TimeSpan.FromSeconds(30);

        string apiUrl =
            "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";

        try
        {
            var response = await http.GetAsync(
                $"{apiUrl}/users/current/accounts");
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new { success = false, message = body };

            var accounts = JArray.Parse(body);
            var results  = new List<object>();

            foreach (var acc in accounts)
            {
                string accId    = acc["_id"]?.ToString()
                                  ?? acc["id"]?.ToString() ?? "";
                string accLogin = acc["login"]?.ToString() ?? "";
                string accState = acc["state"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(accId)) continue;

                try
                {
                    if (accState == "DEPLOYED" || accState == "DEPLOYING")
                    {
                        await http.PostAsync(
                            $"{apiUrl}/users/current/accounts/{accId}/undeploy",
                            new StringContent("", Encoding.UTF8,
                                             "application/json"));
                        await Task.Delay(3000);
                    }

                    var del = await http.DeleteAsync(
                        $"{apiUrl}/users/current/accounts/{accId}");
                    string delBody = await del.Content.ReadAsStringAsync();

                    results.Add(new
                    {
                        login   = accLogin,
                        id      = accId,
                        status  = del.IsSuccessStatusCode ? "DELETED" : "FAILED",
                        details = delBody
                    });

                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        login   = accLogin,
                        id      = accId,
                        status  = "ERROR",
                        details = ex.Message
                    });
                }
            }

            return new
            {
                success = true,
                message = $"Processed {accounts.Count} accounts",
                results
            };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }

    // ── Accounts - ADMIN KEY PROTECTED ───────────────────────
    [HttpGet("accounts")]
    public async Task<object> Accounts([FromQuery] string adminKey = "")
    {
        string expectedKey =
            Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";

        if (string.IsNullOrEmpty(expectedKey) || adminKey != expectedKey)
            return Unauthorized(new { success = false, message = "Unauthorized" });

        string apiToken =
            Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        if (string.IsNullOrEmpty(apiToken))
            return new { success = false, message = "No token" };

        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("auth-token", apiToken);

        string apiUrl =
            "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";

        try
        {
            var response = await http.GetAsync(
                $"{apiUrl}/users/current/accounts");
            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var accounts = JArray.Parse(body);
                var list     = new List<object>();

                foreach (var acc in accounts)
                {
                    list.Add(new
                    {
                        id         = acc["_id"]?.ToString()
                                     ?? acc["id"]?.ToString() ?? "",
                        login      = acc["login"]?.ToString() ?? "",
                        server     = acc["server"]?.ToString() ?? "",
                        state      = acc["state"]?.ToString() ?? "",
                        connection = acc["connectionStatus"]?.ToString() ?? ""
                    });
                }

                return new
                {
                    success  = true,
                    count    = list.Count,
                    accounts = list
                };
            }

            return new { success = false, message = body };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }
}
