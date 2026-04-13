using Microsoft.AspNetCore.Mvc;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet("ping")]
    public object Ping()
    {
        return new
        {
            success = true,
            message = "ProfitX Cloud Trading Server is running!",
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            version = "2.0",
            developer = "London Cyber 2026"
        };
    }

    [HttpGet("status")]
    public object Status()
    {
        return new
        {
            success = true,
            serverName = "ProfitX Cloud Trading Server",
            version = "2.0",
            uptime = DateTime.Now.ToString(),
            environment = "Render Cloud"
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // CLEANUP: Delete all MetaApi accounts and start fresh
    // Call this once: /api/test/cleanup
    // ═══════════════════════════════════════════════════════════════
    [HttpGet("cleanup")]
    public async Task<object> Cleanup()
    {
        string apiToken = Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        if (string.IsNullOrEmpty(apiToken))
        {
            return new { success = false, message = "No MetaApi token set" };
        }

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("auth-token", apiToken);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        string apiUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";

        try
        {
            // Step 1: Get all accounts
            Console.WriteLine("CLEANUP: Fetching all accounts...");
            var response = await httpClient.GetAsync($"{apiUrl}/users/current/accounts");
            string body = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Response: {response.StatusCode}");
            Console.WriteLine($"Body: {body}");

            if (!response.IsSuccessStatusCode)
            {
                return new { success = false, message = $"Failed to get accounts: {response.StatusCode}", details = body };
            }

            var accounts = JArray.Parse(body);
            Console.WriteLine($"Found {accounts.Count} accounts");

            var results = new List<object>();

            foreach (var acc in accounts)
            {
                string accId = acc["_id"]?.ToString() ?? acc["id"]?.ToString() ?? "";
                string accLogin = acc["login"]?.ToString() ?? "";
                string accState = acc["state"]?.ToString() ?? "";

                Console.WriteLine($"Processing: {accLogin} -> {accId} ({accState})");

                if (string.IsNullOrEmpty(accId))
                {
                    results.Add(new { login = accLogin, status = "skipped - no ID" });
                    continue;
                }

                try
                {
                    // Undeploy first
                    if (accState == "DEPLOYED" || accState == "DEPLOYING")
                    {
                        Console.WriteLine($"  Undeploying: {accId}");
                        var undeployResponse = await httpClient.PostAsync(
                            $"{apiUrl}/users/current/accounts/{accId}/undeploy",
                            new StringContent("", Encoding.UTF8, "application/json"));
                        Console.WriteLine($"  Undeploy: {undeployResponse.StatusCode}");
                        await Task.Delay(3000);
                    }

                    // Delete
                    Console.WriteLine($"  Deleting: {accId}");
                    var deleteResponse = await httpClient.DeleteAsync(
                        $"{apiUrl}/users/current/accounts/{accId}");
                    string deleteBody = await deleteResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"  Delete: {deleteResponse.StatusCode} - {deleteBody}");

                    results.Add(new
                    {
                        login = accLogin,
                        id = accId,
                        status = deleteResponse.IsSuccessStatusCode ? "DELETED" : "FAILED",
                        details = deleteBody
                    });

                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    results.Add(new { login = accLogin, id = accId, status = "ERROR", details = ex.Message });
                }
            }

            return new
            {
                success = true,
                message = $"Cleanup complete. Processed {accounts.Count} accounts.",
                results = results
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CLEANUP ERROR: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LIST: Show all MetaApi accounts
    // Call: /api/test/accounts
    // ═══════════════════════════════════════════════════════════════
    [HttpGet("accounts")]
    public async Task<object> Accounts()
    {
        string apiToken = Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        if (string.IsNullOrEmpty(apiToken))
        {
            return new { success = false, message = "No MetaApi token set" };
        }

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("auth-token", apiToken);

        string apiUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";

        try
        {
            var response = await httpClient.GetAsync($"{apiUrl}/users/current/accounts");
            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var accounts = JArray.Parse(body);
                var accountList = new List<object>();

                foreach (var acc in accounts)
                {
                    accountList.Add(new
                    {
                        id = acc["_id"]?.ToString() ?? acc["id"]?.ToString() ?? "",
                        login = acc["login"]?.ToString() ?? "",
                        server = acc["server"]?.ToString() ?? "",
                        state = acc["state"]?.ToString() ?? "",
                        connection = acc["connectionStatus"]?.ToString() ?? "",
                        name = acc["name"]?.ToString() ?? ""
                    });
                }

                return new { success = true, count = accountList.Count, accounts = accountList };
            }

            return new { success = false, message = body };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }
}
