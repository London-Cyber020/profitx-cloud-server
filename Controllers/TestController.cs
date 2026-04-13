using System;
using Microsoft.AspNetCore.Mvc;

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
            message = "ProfitX Cloud Server is running!",
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            version = "1.0",
            developer = "London Cyber 2026"
        };
    }

    [HttpGet("status")]
    public object Status()
    {
        return new
        {
            success = true,
            serverName = "ProfitX Cloud Server",
            version = "1.0",
            uptime = DateTime.Now.ToString(),
            environment = "Railway Cloud"
        };
    }
}
