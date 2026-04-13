using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BridgeController : ControllerBase
{
    private readonly DataStore _store;

    public BridgeController(DataStore store)
    {
        _store = store;
    }

    [HttpPost("register")]
    public object Register([FromBody] BridgeRegisterRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";
        _store.RegisteredPCs[key] = new
        {
            request.UserId,
            request.Mt5Login,
            request.PcName,
            lastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        Console.WriteLine($"PC Registered: {request.PcName} (Login: {request.Mt5Login})");
        return new { success = true, message = "PC registered successfully" };
    }

    [HttpPost("accountinfo")]
    public object AccountInfo([FromBody] BridgeDataRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";
        if (!_store.PcData.ContainsKey(key))
            _store.PcData[key] = new Dictionary<string, string>();

        _store.PcData[key]["accountInfo"] = request.Content;
        _store.PcData[key]["lastUpdate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        return new { success = true };
    }

    [HttpPost("opentrades")]
    public object OpenTrades([FromBody] BridgeDataRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";
        if (!_store.PcData.ContainsKey(key))
            _store.PcData[key] = new Dictionary<string, string>();

        _store.PcData[key]["openTrades"] = request.Content;

        return new { success = true };
    }

    [HttpPost("status")]
    public object Status([FromBody] BridgeDataRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";
        if (!_store.PcData.ContainsKey(key))
            _store.PcData[key] = new Dictionary<string, string>();

        _store.PcData[key]["status"] = request.Content;

        return new { success = true };
    }

    [HttpPost("history")]
    public object History([FromBody] BridgeDataRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";
        if (!_store.PcData.ContainsKey(key))
            _store.PcData[key] = new Dictionary<string, string>();

        _store.PcData[key]["history"] = request.Content;

        return new { success = true };
    }

    [HttpGet("commands")]
    public object Commands(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.PendingCommands.ContainsKey(key))
        {
            _store.PendingCommands.TryRemove(key, out string? command);
            return command ?? "NONE";
        }

        return "NONE";
    }

    [HttpGet("registeredpcs")]
    public object RegisteredPCs()
    {
        return new
        {
            success = true,
            pcs = _store.RegisteredPCs.Values,
            count = _store.RegisteredPCs.Count
        };
    }
}

public class BridgeRegisterRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public string PcName { get; set; } = "";
}

public class BridgeDataRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
    public string Timestamp { get; set; } = "";
}
