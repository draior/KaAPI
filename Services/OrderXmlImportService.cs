using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using KaApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KaApi.Services;

public partial class OrderXmlImportService(
    IOptions<OrderXmlImportDefaults> defaultOptions,
    ILogger<OrderXmlImportService> logger) : IOrderXmlImportService
{
    private static readonly Encoding Windows1251;
    private readonly OrderXmlImportDefaults _defaults = defaultOptions.Value;
    private readonly ILogger<OrderXmlImportService> _logger = logger;

    static OrderXmlImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1251 = Encoding.GetEncoding(1251);
    }

    public async Task<SaleUpsertRequest> MapAsync(
        Stream xmlStream,
        OrderXmlImportOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var document = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken);
        var root = document.Root ?? throw new InvalidOperationException("The XML payload is empty.");
        var documentType = ResolveDocumentType(root);

        _logger.LogInformation(
            "Received XML import payload. RootElement={RootElement}, DocumentType={DocumentType}",
            root.Name.LocalName,
            documentType);

        var saleRequest = documentType switch
        {
            OrderXmlDocumentType.Transaction => MapTransactionDocument(root, overrides),
            OrderXmlDocumentType.Orders => MapOrdersDocument(root, overrides),
            _ => throw new InvalidOperationException("Unsupported XML format. Expected either <transaction> or <orders>.")
        };

        _logger.LogInformation(
            "Mapped XML import payload to sale request. DocumentType={DocumentType}, SaleNo={SaleNo}, ClientCode={ClientCode}, ClientTaxNo={ClientTaxNo}, DetailCount={DetailCount}",
            documentType,
            saleRequest.SaleNo,
            saleRequest.Client?.Code,
            saleRequest.Client?.TaxNo,
            saleRequest.Details.Count);

        return saleRequest;
    }

    private SaleUpsertRequest MapTransactionDocument(XElement root, OrderXmlImportOverrides? overrides)
    {
        var transactionDate = ParseDate(
            ChildValue(root, "transaction-date", "receipt-date", "bookkeeping-date", "book-keeping-date", "date", "beginning-timestamp"))
            ?? DateTime.Now;

        var customerElement = Child(root, "customer");
        var detailRequests = MapTransactionDetails(root);

        if (detailRequests.Count == 0)
        {
            throw new InvalidOperationException("No article lines were found in the transaction XML.");
        }

        var saleNo = ResolveTransactionSaleNo(root);
        _logger.LogInformation(
            "Mapping transaction XML document. SaleNo={SaleNo}, TransactionDate={TransactionDate}, DetailCount={DetailCount}, HasCustomer={HasCustomer}",
            saleNo,
            transactionDate,
            detailRequests.Count,
            customerElement is not null);

        return BuildSaleRequest(
            saleNo: saleNo,
            saleDate: transactionDate,
            businessGroupId: FirstNonEmpty(
                overrides?.BusinessGroupId,
                ChildValue(root, "businessgroup-id", "business-group-id", "shop-id"),
                _defaults.BusinessGroupId),
            userId: FirstNonEmpty(
                overrides?.UserId,
                ChildValue(root, "operator-id", "user-id"),
                _defaults.UserId),
            currencyId: FirstNonEmpty(
                overrides?.CurrencyId,
                ChildValue(root, "currency", "system-currency"),
                _defaults.CurrencyId),
            comment: BuildSaleComment(root),
            client: MapTransactionClient(customerElement, overrides),
            details: detailRequests,
            overrides: overrides);
    }

    private SaleUpsertRequest MapOrdersDocument(XElement root, OrderXmlImportOverrides? overrides)
    {
        var orderElement = root.Elements().FirstOrDefault(element => NameEquals(element.Name.LocalName, "order"))
            ?? throw new InvalidOperationException("The <orders> XML does not contain an <order> element.");

        var saleDate = ParseDate(ChildValue(orderElement, "data", "date")) ?? DateTime.Now;
        var detailRequests = MapOrderDetails(orderElement);

        if (detailRequests.Count == 0)
        {
            throw new InvalidOperationException("No product lines were found in the order XML.");
        }

        var saleNo = ChildValue(orderElement, "id");
        _logger.LogInformation(
            "Mapping order XML document. SaleNo={SaleNo}, SaleDate={SaleDate}, DetailCount={DetailCount}",
            saleNo,
            saleDate,
            detailRequests.Count);

        return BuildSaleRequest(
            saleNo: saleNo,
            saleDate: saleDate,
            businessGroupId: FirstNonEmpty(overrides?.BusinessGroupId, _defaults.BusinessGroupId),
            userId: FirstNonEmpty(overrides?.UserId, _defaults.UserId),
            currencyId: FirstNonEmpty(overrides?.CurrencyId, _defaults.CurrencyId),
            comment: BuildOrderComment(orderElement),
            client: MapOrderClient(orderElement, overrides),
            details: detailRequests,
            overrides: MergeOrderOverrides(orderElement, overrides));
    }

    private SaleUpsertRequest BuildSaleRequest(
        string? saleNo,
        DateTime saleDate,
        string? businessGroupId,
        string? userId,
        string? currencyId,
        string? comment,
        ClientUpsertRequest? client,
        List<SaleDetailUpsertRequest> details,
        OrderXmlImportOverrides? overrides)
    {
        var saleRequest = new SaleUpsertRequest
        {
            SaleNo = saleNo,
            SaleDate = saleDate,
            Deadline = saleDate,
            ContractDate = saleDate,
            SaleTypeId = overrides?.SaleTypeId ?? _defaults.SaleTypeId,
            BusinessGroupId = businessGroupId,
            UserId = userId,
            CurrencyId = currencyId,
            CurrencyRate = overrides?.CurrencyRate ?? _defaults.CurrencyRate,
            PayStatusId = overrides?.PayStatusId ?? _defaults.PayStatusId,
            DocStatusId = overrides?.DocStatusId ?? _defaults.DocStatusId,
            Comment = comment,
            Client = client,
            Details = details
        };

        saleRequest.ClientId = saleRequest.Client?.Id ?? saleRequest.Client?.Code;
        saleRequest.ClientAgentId = saleRequest.Client?.Agent?.Id;

        if (string.IsNullOrWhiteSpace(saleRequest.BusinessGroupId))
        {
            throw new InvalidOperationException("BusinessGroupId is required. Provide it in XML or in OrderXmlImportDefaults.");
        }

        if (string.IsNullOrWhiteSpace(saleRequest.UserId))
        {
            throw new InvalidOperationException("UserId is required. Provide it in XML or in OrderXmlImportDefaults.");
        }

        return saleRequest;
    }

    private ClientUpsertRequest? MapTransactionClient(XElement? customerElement, OrderXmlImportOverrides? overrides)
    {
        if (customerElement is null)
        {
            return null;
        }

        var street = ChildValue(customerElement, "street", "address");
        var careOf = ChildValue(customerElement, "care-of");
        var zipCode = ChildValue(customerElement, "zip-code", "zipcode");
        var city = ChildValue(customerElement, "city");
        var country = ChildValue(customerElement, "country", "nation");

        var addressParts = new[] { careOf, street, zipCode, city, country }
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .Select(static part => part!.Trim());

        var combinedAddress = string.Join(", ", addressParts);
        if (string.IsNullOrWhiteSpace(combinedAddress))
        {
            combinedAddress = ChildValue(customerElement, "address");
        }

        var client = new ClientUpsertRequest
        {
            Id = ChildValue(customerElement, "id"),
            Code = ChildValue(customerElement, "card-number", "code"),
            Name = ChildValue(customerElement, "description", "name"),
            Address = combinedAddress,
            Phone = ChildValue(customerElement, "phone"),
            Phone2 = ChildValue(customerElement, "phone2"),
            TaxNo = ChildValue(customerElement, "fiscal-vat-number", "vat-number", "taxno"),
            Bulstat = ChildValue(customerElement, "fiscal-code", "bulstat"),
            CountryId = ChildValue(customerElement, "country-id", "country"),
            ClientTypeId = FirstNonEmpty(ChildValue(customerElement, "clienttype-id"), overrides?.ClientTypeId, _defaults.ClientTypeId),
            Comment = BuildClientComment(customerElement),
            ContactAddress = ChildValue(customerElement, "care-of"),
            Email = ChildValue(customerElement, "email", "e-mail"),
            WebAddress = ChildValue(customerElement, "web-address"),
            CellPhone = ChildValue(customerElement, "cell-phone", "mobile"),
            Mol = ChildValue(customerElement, "contact-name", "contact", "person"),
            Agent = MapAgent(customerElement, combinedAddress)
        };

        if (string.IsNullOrWhiteSpace(client.Id) &&
            string.IsNullOrWhiteSpace(client.Code) &&
            string.IsNullOrWhiteSpace(client.Name))
        {
            return null;
        }

        return client;
    }

    private ClientUpsertRequest? MapOrderClient(XElement orderElement, OrderXmlImportOverrides? overrides)
    {
        var addressParts = new[]
        {
            ChildValue(orderElement, "adres"),
            ChildValue(orderElement, "grad")
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value!.Trim());

        var client = new ClientUpsertRequest
        {
            Name = ChildValue(orderElement, "firma"),
            Address = string.Join(", ", addressParts),
            Phone = ChildValue(orderElement, "telephone"),
            TaxNo = ChildValue(orderElement, "dds"),
            Bulstat = ChildValue(orderElement, "eik"),
            ClientTypeId = FirstNonEmpty(overrides?.ClientTypeId, _defaults.ClientTypeId),
            Email = ChildValue(orderElement, "email"),
            Mol = ChildValue(orderElement, "ime"),
            Agent = new ClientAgentUpsertRequest
            {
                Name = FirstNonEmpty(ChildValue(orderElement, "deliveryime"), ChildValue(orderElement, "ime")),
                Phone = ChildValue(orderElement, "telephone"),
                CellPhone = FirstNonEmpty(ChildValue(orderElement, "deliverytel"), ChildValue(orderElement, "telephone")),
                Email = ChildValue(orderElement, "email"),
                Address = FirstNonEmpty(ChildValue(orderElement, "deliveryadres"), ChildValue(orderElement, "adres"))
            }
        };

        if (string.IsNullOrWhiteSpace(client.Name) &&
            string.IsNullOrWhiteSpace(client.TaxNo) &&
            string.IsNullOrWhiteSpace(client.Bulstat))
        {
            return null;
        }

        return client;
    }

    private static ClientAgentUpsertRequest? MapAgent(XElement customerElement, string? combinedAddress)
    {
        var agent = new ClientAgentUpsertRequest
        {
            Name = ChildValue(customerElement, "contact-name", "contact", "person", "description", "name"),
            Phone = ChildValue(customerElement, "phone"),
            Phone2 = ChildValue(customerElement, "phone2"),
            Address = combinedAddress,
            Email = ChildValue(customerElement, "email", "e-mail"),
            CellPhone = ChildValue(customerElement, "cell-phone", "mobile")
        };

        if (string.IsNullOrWhiteSpace(agent.Name) &&
            string.IsNullOrWhiteSpace(agent.Phone) &&
            string.IsNullOrWhiteSpace(agent.CellPhone) &&
            string.IsNullOrWhiteSpace(agent.Email))
        {
            return null;
        }

        return agent;
    }

    private static List<SaleDetailUpsertRequest> MapTransactionDetails(XElement transactionElement)
    {
        return transactionElement.Elements()
            .Where(element => NameEquals(element.Name.LocalName, "trans-item"))
            .Where(IsArticleLikeItem)
            .Select(MapDetail)
            .Where(detail => detail is not null)
            .Cast<SaleDetailUpsertRequest>()
            .ToList();
    }

    private static List<SaleDetailUpsertRequest> MapOrderDetails(XElement orderElement)
    {
        var productsElement = Child(orderElement, "products");
        if (productsElement is null)
        {
            return [];
        }

        return productsElement.Elements()
            .Where(element => NameEquals(element.Name.LocalName, "product"))
            .Select(MapOrderDetail)
            .Where(detail => detail is not null)
            .Cast<SaleDetailUpsertRequest>()
            .ToList();
    }

    private static bool IsArticleLikeItem(XElement item)
    {
        var type = item.Attribute("type")?.Value;
        return NameEquals(type, "article");
    }

    private static SaleDetailUpsertRequest? MapDetail(XElement item)
    {
        var quantity = ParseDecimal(
            ChildValue(item, "quantity", "quantity-weight"),
            allowFormattedQuantity: false)
            ?? ParseDecimal(ChildValue(item, "quantity-description"), allowFormattedQuantity: true)
            ?? 1m;

        var explicitUnitPrice = ParseDecimal(ChildValue(item, "price", "unit-price", "single-price"));
        var discount = SumValues(item, "discount", "promotion-discount");
        var service = ParseDecimal(ChildValue(item, "service")) ?? 0m;
        var discountTotal = discount + service;
        var taxableAmount = ParseDecimal(ChildValue(item, "taxable-amount", "net-price", "price-without-tax"));
        var totalPrice = ParseDecimal(ChildValue(item, "total-price", "gross-price", "price-with-tax"));
        var vatPercent = ParseDecimal(ChildValue(item, "vat-percent", "tax-percent"));
        var explicitVatAmount = ParseDecimal(ChildValue(item, "vat", "vat-amount", "tax-amount"));

        var netTotal = taxableAmount;
        if (!netTotal.HasValue && explicitUnitPrice.HasValue)
        {
            netTotal = (explicitUnitPrice.Value * quantity) - discountTotal;
        }

        if (!netTotal.HasValue && totalPrice.HasValue && vatPercent.HasValue)
        {
            netTotal = totalPrice.Value / (1 + (vatPercent.Value / 100m));
        }

        if (!netTotal.HasValue && totalPrice.HasValue && !explicitVatAmount.HasValue)
        {
            netTotal = totalPrice.Value;
        }

        var vatAmount = explicitVatAmount;
        if (!vatAmount.HasValue && netTotal.HasValue && vatPercent.HasValue)
        {
            vatAmount = netTotal.Value * (vatPercent.Value / 100m);
        }

        vatAmount ??= 0m;

        if (!netTotal.HasValue)
        {
            return null;
        }

        var unitPrice = explicitUnitPrice;
        if (!unitPrice.HasValue)
        {
            unitPrice = quantity == 0
                ? 0m
                : (netTotal.Value + discountTotal) / quantity;
        }

        var grossTotal = unitPrice.Value * quantity;
        var discountPercent = grossTotal == 0m
            ? 0m
            : Math.Round((discountTotal / grossTotal) * 100m, 3, MidpointRounding.AwayFromZero);

        return new SaleDetailUpsertRequest
        {
            ArticleId = ChildValue(item, "article-id"),
            Code = ChildValue(item, "barcode", "article-code", "code", "id"),
            Name = ChildValue(item, "description", "name"),
            NameLatin = ChildValue(item, "description", "name"),
            Quantity = RoundToDouble(quantity),
            SinglePrice = RoundMoneyToDouble(unitPrice.Value),
            SourceSinglePrice = explicitUnitPrice.HasValue ? RoundMoneyToDouble(explicitUnitPrice.Value) : RoundMoneyToDouble(unitPrice.Value),
            Discount = RoundToDouble(discountPercent),
            Vat = RoundMoneyToDouble(vatAmount.Value),
            CodeEan = ChildValue(item, "barcode")
        };
    }

    private static SaleDetailUpsertRequest? MapOrderDetail(XElement product)
    {
        var quantity = ParseDecimal(ChildValue(product, "broi")) ?? 0m;
        var discountedUnitPrice = ParseDecimal(ChildValue(product, "cena")) ?? 0m;
        var lineTotal = ParseDecimal(ChildValue(product, "stoinostbezdds"));
        var originalPrice = ParseDecimal(ChildValue(product, "zadraskanacena"));
        var discountPercent = ParseDecimal(ChildValue(product, "otstapka"));

        var netLineTotal = lineTotal ?? Math.Round(quantity * discountedUnitPrice, 2, MidpointRounding.AwayFromZero);
        var discountPercentValue = discountPercent ?? 0m;
        var baseUnitPrice = discountedUnitPrice;

        if (originalPrice.HasValue && originalPrice.Value > discountedUnitPrice)
        {
            baseUnitPrice = originalPrice.Value;
            if (!discountPercent.HasValue && quantity > 0 && baseUnitPrice > 0)
            {
                discountPercentValue = Math.Round((((baseUnitPrice * quantity) - netLineTotal) / (baseUnitPrice * quantity)) * 100m, 3, MidpointRounding.AwayFromZero);
            }
        }
        else if (discountPercent.HasValue && discountPercent.Value > 0 && discountPercent.Value < 100)
        {
            baseUnitPrice = quantity == 0
                ? discountedUnitPrice
                : Math.Round((netLineTotal / quantity) / (1 - (discountPercent.Value / 100m)), 2, MidpointRounding.AwayFromZero);
        }

        return new SaleDetailUpsertRequest
        {
            Code = ChildValue(product, "kod"),
            Name = ChildValue(product, "ime"),
            NameLatin = ChildValue(product, "ime"),
            Quantity = RoundToDouble(quantity),
            SinglePrice = RoundMoneyToDouble(baseUnitPrice),
            SourceSinglePrice = RoundMoneyToDouble(discountedUnitPrice),
            Discount = RoundToDouble(discountPercentValue),
            Vat = 0d
        };
    }

    private static string? ResolveTransactionSaleNo(XElement root)
    {
        return FirstNonEmpty(
            ChildValue(root, "id"),
            ChildValue(root, "transaction-id"),
            ChildValue(root, "guid"),
            BuildTransactionNumber(root));
    }

    private static string? BuildTransactionNumber(XElement root)
    {
        var transactionNumber = ChildValue(root, "transaction-number");
        var tillId = ChildValue(root, "till-id");

        if (string.IsNullOrWhiteSpace(transactionNumber))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(tillId)
            ? transactionNumber
            : $"{tillId}-{transactionNumber}";
    }

    private static string? BuildSaleComment(XElement root)
    {
        var commentParts = new[]
        {
            ChildValue(root, "comment"),
            ChildValue(root, "transaction-causal-description"),
            ChildValue(root, "software-version")
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value!.Trim());

        var combined = string.Join(" | ", commentParts);
        return string.IsNullOrWhiteSpace(combined) ? "Imported from transaction XML" : combined;
    }

    private static string? BuildOrderComment(XElement orderElement)
    {
        var commentParts = new[]
        {
            "Imported from order XML",
            BuildLabelValue("payment", ChildValue(orderElement, "nachin")),
            BuildLabelValue("paid", ChildValue(orderElement, "paid")),
            BuildLabelValue("status", ChildValue(orderElement, "status")),
            BuildLabelValue("delivery", FirstNonEmpty(ChildValue(orderElement, "deliveryime"), ChildValue(orderElement, "deliverygrad"), ChildValue(orderElement, "deliveryadres")))
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value!.Trim());

        return string.Join(" | ", commentParts);
    }

    private static string? BuildClientComment(XElement customerElement)
    {
        var values = customerElement.Elements()
            .Where(element => element.Name.LocalName.StartsWith("notes", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value));

        var combined = string.Join(" | ", values);
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    private static decimal SumValues(XElement element, params string[] childNames)
    {
        decimal total = 0m;
        foreach (var childName in childNames)
        {
            total += ParseDecimal(ChildValue(element, childName)) ?? 0m;
        }

        return total;
    }

    private static string? BuildLabelValue(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}";
    }

    private OrderXmlImportOverrides MergeOrderOverrides(XElement orderElement, OrderXmlImportOverrides? overrides)
    {
        return new OrderXmlImportOverrides
        {
            BusinessGroupId = overrides?.BusinessGroupId,
            UserId = overrides?.UserId,
            SaleTypeId = overrides?.SaleTypeId,
            CurrencyId = overrides?.CurrencyId,
            CurrencyRate = overrides?.CurrencyRate,
            ClientTypeId = overrides?.ClientTypeId,
            PayStatusId = overrides?.PayStatusId ?? ResolveOrderPayStatusId(ChildValue(orderElement, "paid")),
            DocStatusId = overrides?.DocStatusId ?? ResolveOrderDocStatusId(ChildValue(orderElement, "status"))
        };
    }

    private static int? ResolveOrderPayStatusId(string? paidText)
    {
        var normalized = NormalizeForMatch(paidText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ContainsAny(normalized, "неплат", "neplat", "рќрµрїр»р°с‚"))
        {
            return 0;
        }

        if (ContainsAny(normalized, "частич", "chastich", "с‡р°сѓс‚рёс‡"))
        {
            return 2;
        }

        if (ContainsAny(normalized, "плат", "plat", "рїр»р°с‚"))
        {
            return 1;
        }

        return null;
    }

    private static int? ResolveOrderDocStatusId(string? statusText)
    {
        var normalized = NormalizeForMatch(statusText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ContainsAny(normalized, "необработ", "neobrabot", "рќрµрѕр±сђр°р±рѕс‚") ||
            ContainsAny(normalized, "неприключ", "nepriklyuch", "рќрµрїсђрёрєр»сћс‡"))
        {
            return 1;
        }

        if (ContainsAny(normalized, "приключ", "priklyuch", "рџсђрёрєр»сћс‡") ||
            ContainsAny(normalized, "обработ", "obrabot", "рћр±сђр°р±рѕс‚"))
        {
            return 2;
        }

        if (ContainsAny(normalized, "сторн", "storn", "сѓс‚рѕсђрѕ"))
        {
            return 3;
        }

        if (ContainsAny(normalized, "анулир", "anulir", "рђрѕсѓр»рёсђ"))
        {
            return 5;
        }

        return null;
    }

    private static string? NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ChildValue(XElement? parent, params string[] names)
    {
        return NormalizeImportedValue(Child(parent, names)?.Value);
    }

    private static XElement? Child(XElement? parent, params string[] names)
    {
        if (parent is null)
        {
            return null;
        }

        return parent.Elements()
            .FirstOrDefault(element => names.Any(name => NameEquals(element.Name.LocalName, name)));
    }

    private static bool NameEquals(string? left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? NormalizeImportedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value?.Trim();
        }

        var trimmed = value.Trim();
        if (!LooksLikeUtf8Mojibake(trimmed))
        {
            return trimmed;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(Windows1251.GetBytes(trimmed)).Trim();
            return IsBetterDecodedValue(trimmed, repaired) ? repaired : trimmed;
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
    }

    private static bool LooksLikeUtf8Mojibake(string value)
    {
        return value.Contains('Р') || value.Contains('С') || value.Contains('Ð') || value.Contains('Ñ');
    }

    private static bool IsBetterDecodedValue(string original, string repaired)
    {
        if (string.IsNullOrWhiteSpace(repaired) ||
            repaired.Contains('\uFFFD') ||
            string.Equals(original, repaired, StringComparison.Ordinal))
        {
            return false;
        }

        return CountCyrillicLetters(repaired) >= CountCyrillicLetters(original);
    }

    private static int CountCyrillicLetters(string value)
    {
        return value.Count(character => character is >= '\u0400' and <= '\u04FF');
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        var exactFormats = new[]
        {
            "dd.MM.yyyy",
            "d.M.yyyy",
            "dd.MM.yyyy HH:mm:ss",
            "d.M.yyyy HH:mm:ss",
            "dd.MM.yyyy H:mm:ss",
            "d.M.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm",
            "d.M.yyyy HH:mm",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff"
        };

        if (DateTime.TryParseExact(
            normalized,
            exactFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
            normalized,
            CultureInfo.GetCultureInfo("bg-BG"),
            DateTimeStyles.AssumeLocal,
            out parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
            normalized,
            CultureInfo.CurrentCulture,
            DateTimeStyles.AssumeLocal,
            out parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? ParseDecimal(string? value, bool allowFormattedQuantity = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace(" ", string.Empty);

        if (allowFormattedQuantity)
        {
            var match = LeadingNumberRegex().Match(normalized);
            if (match.Success)
            {
                normalized = match.Value;
            }
        }

        // Many partner XML payloads use comma as decimal separator, e.g. 153,42.
        // Parse that form first so the comma is not treated as a thousands separator.
        if (normalized.Contains(',') &&
            !normalized.Contains('.') &&
            decimal.TryParse(normalized.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double RoundToDouble(decimal value)
    {
        return Math.Round((double)value, 3, MidpointRounding.AwayFromZero);
    }

    private static double RoundMoneyToDouble(decimal value)
    {
        return Math.Round((double)value, 2, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"^-?\d+([.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex LeadingNumberRegex();

    private enum OrderXmlDocumentType
    {
        Transaction,
        Orders
    }

    private static OrderXmlDocumentType ResolveDocumentType(XElement root)
    {
        if (NameEquals(root.Name.LocalName, "transaction"))
        {
            return OrderXmlDocumentType.Transaction;
        }

        if (NameEquals(root.Name.LocalName, "orders"))
        {
            return OrderXmlDocumentType.Orders;
        }

        throw new InvalidOperationException($"Unsupported XML root element '{root.Name.LocalName}'.");
    }
}
