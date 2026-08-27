using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace KaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public ActionResult<object> Get()
    {
        return Ok(CreateResponse());
    }

    [HttpPost]
    [Produces("application/json")]
    public ActionResult<object> Post()
    {
        return Ok(CreateResponse());
    }

    private static object CreateResponse()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return new
        {
            name = "KaApi",
            version = string.IsNullOrWhiteSpace(informationalVersion) ? "1.0.1" : informationalVersion
        };
    }
}
