using System.Text;
using System.Xml;
using System.Xml.Serialization;
using KaApi.Models;
using KaApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalesController(
    ISalesService salesService,
    IOrderXmlImportService orderXmlImportService,
    ILogger<SalesController> logger) : ControllerBase
{
    private readonly ILogger<SalesController> _logger = logger;

    [HttpPost]
    [Consumes("application/json", "application/xml", "text/xml")]
    [Produces("application/json", "application/xml")]
    [ProducesResponseType(typeof(CreateSaleResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateSaleResponse>> CreateSale(
        [FromBody] SaleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "POST /api/sales received. ContentType={ContentType}, SaleNo={SaleNo}, DetailCount={DetailCount}",
            Request.ContentType,
            request.SaleNo,
            request.Details.Count);

        var response = await salesService.CreateSaleAsync(request, cancellationToken);
        _logger.LogInformation(
            "POST /api/sales completed. SaleId={SaleId}, SaleNo={SaleNo}, DetailCount={DetailCount}",
            response.SaleId,
            response.SaleNo,
            response.DetailCount);
        return CreateFormattedResponse(response);
    }

    [HttpPost("import-order-xml")]
    [Consumes("application/xml", "text/xml")]
    [Produces("application/json", "application/xml")]
    [ProducesResponseType(typeof(CreateSaleResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateSaleResponse>> ImportOrderXml(
        [FromQuery] OrderXmlImportOverrides overrides,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "POST /api/sales/import-order-xml received. ContentType={ContentType}",
            Request.ContentType);

        var saleRequest = await orderXmlImportService.MapAsync(Request.Body, overrides, cancellationToken);
        var response = await salesService.CreateSaleAsync(saleRequest, cancellationToken);
        _logger.LogInformation(
            "POST /api/sales/import-order-xml completed. SaleId={SaleId}, SaleNo={SaleNo}, DetailCount={DetailCount}",
            response.SaleId,
            response.SaleNo,
            response.DetailCount);
        return CreateFormattedResponse(response);
    }

    private ActionResult<CreateSaleResponse> CreateFormattedResponse(CreateSaleResponse response)
    {
        var location = $"/api/sales/{response.SaleId}";

        if (WantsXmlResponse())
        {
            Response.Headers.Location = location;
            return new ContentResult
            {
                Content = SerializeXml(response),
                ContentType = "application/xml; charset=utf-8",
                StatusCode = StatusCodes.Status201Created
            };
        }

        return Created(location, response);
    }

    private bool WantsXmlResponse()
    {
        var contentType = Request.ContentType;
        return !string.IsNullOrWhiteSpace(contentType)
            && (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("text/xml", StringComparison.OrdinalIgnoreCase));
    }

    private static string SerializeXml(CreateSaleResponse response)
    {
        var serializer = new XmlSerializer(typeof(CreateSaleResponse));
        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = true
        };

        using var stringWriter = new Utf8StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        serializer.Serialize(xmlWriter, response);
        return stringWriter.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
