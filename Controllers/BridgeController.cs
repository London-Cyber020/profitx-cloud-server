using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BridgeController : ControllerBase
{
    [HttpGet("status")]
    public object Status()
    {
        return new { success = true, message = "Bridge not needed - using cloud trading" };
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
