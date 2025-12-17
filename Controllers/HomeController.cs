using Microsoft.AspNetCore.Mvc;

[ApiController]

public class HomeController : ControllerBase
{
    [HttpGet]
    [Route("")]
    public IActionResult HealthCheck()
    {
        return Ok(new { message = "API is running", status = "Healthy" });
    }

    [Route("/error")]
    public IActionResult Error()
    {
        return StatusCode(500, new
        {
            message = "An unexpected error occurred.",
            requestId = HttpContext.TraceIdentifier
        });
    }
}