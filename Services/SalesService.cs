using KaApi.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace KaApi.Services;

public class SalesService(
    IConfiguration configuration,
    ILogger<SalesService> logger) : ISalesService
{
    private const string NextIdProcedureName = "dbo.SYS_GETNEXTID";

    private const string SalesInsertSql = """
        INSERT INTO SALES
        (
            ID, SALENO, SALEDATE, SALETYPE_ID, CLIENT_ID, BUSINESSGROUP_ID, USER_ID,
            PRICE, DISCOUNT, TOTALPRICE, PAYEDSUM, DEADLINE, PAYSTATUS_ID, DOCSTATUS_ID,
            VAT, TOTALVAT, CURRENCY_ID, CURRENCYVALUE, CURRENCYRATE, PAYEDCURRENCY,
            C_BUSINESSGROUP_ID, C_USER_ID, DISCOUNTSUM, CVWITHOUTVAT,
            CLIENTAGENT_ID, DESIGNER_ID, CONTRACTNO, CONTRACTDATE, UNP
        )
        VALUES
        (
            @ID, @SALENO, @SALEDATE, @SALETYPE_ID, @CLIENT_ID, @BUSINESSGROUP_ID, @USER_ID,
            @PRICE, @DISCOUNT, @TOTALPRICE, @PAYEDSUM, @DEADLINE, @PAYSTATUS_ID, @DOCSTATUS_ID,
            @VAT, @TOTALVAT, @CURRENCY_ID, @CURRENCYVALUE, @CURRENCYRATE, @PAYEDCURRENCY,
            @C_BUSINESSGROUP_ID, @C_USER_ID, @DISCOUNTSUM, @CVWITHOUTVAT,
            @CLIENTAGENT_ID, @DESIGNER_ID, @CONTRACTNO, @CONTRACTDATE, @UNP
        );
        """;

    private const string SalesUpdateSql = """
        UPDATE SALES
        SET
            SALENO = @SALENO,
            SALEDATE = @SALEDATE,
            SALETYPE_ID = @SALETYPE_ID,
            CLIENT_ID = @CLIENT_ID,
            BUSINESSGROUP_ID = @BUSINESSGROUP_ID,
            USER_ID = @USER_ID,
            PRICE = @PRICE,
            DISCOUNT = @DISCOUNT,
            TOTALPRICE = @TOTALPRICE,
            PAYEDSUM = @PAYEDSUM,
            DEADLINE = @DEADLINE,
            PAYSTATUS_ID = @PAYSTATUS_ID,
            DOCSTATUS_ID = @DOCSTATUS_ID,
            VAT = @VAT,
            TOTALVAT = @TOTALVAT,
            CURRENCY_ID = @CURRENCY_ID,
            CURRENCYVALUE = @CURRENCYVALUE,
            CURRENCYRATE = @CURRENCYRATE,
            PAYEDCURRENCY = @PAYEDCURRENCY,            
            C_BUSINESSGROUP_ID = @C_BUSINESSGROUP_ID,
            C_USER_ID = @C_USER_ID,
            DISCOUNTSUM = @DISCOUNTSUM,
            CVWITHOUTVAT = @CVWITHOUTVAT,
            CLIENTAGENT_ID = @CLIENTAGENT_ID,
            DESIGNER_ID = @DESIGNER_ID,
            CONTRACTNO = @CONTRACTNO,
            CONTRACTDATE = @CONTRACTDATE,
            UNP = @UNP
        WHERE ID = @ID;
        """;

    private const string SalesTotalsBySaleSql = """
        SELECT
            COALESCE(SUM(TOTALPRICE), 0),
            COALESCE(SUM(VAT), 0),
            COALESCE(SUM(TOTALVAT), 0),
            COALESCE(SUM((QUANTITY * SINGLEPRICE) * (DISCOUNT / 100.0)), 0)
        FROM SALEDETAIL
        WHERE SALE_ID = @SALE_ID;
        """;

    private const string ExistingSaleLookupSql = """
        SELECT TOP 1 ID, CLIENT_ID, CLIENTAGENT_ID, DOCSTATUS_ID, SYNC
        FROM SALES
        WHERE SALENO = @SALENO
        ORDER BY C_ACTIONDATETIME DESC;
        """;

    private const string DeleteSaleDetailsSql = """
        DELETE FROM SALEDETAIL
        WHERE SALE_ID = @SALE_ID;
        """;

    private const string SaleDetailCountSql = """
        SELECT COUNT(*)
        FROM SALEDETAIL
        WHERE SALE_ID = @SALE_ID;
        """;

    private const string ClientLookupByIdSql = """
        SELECT TOP 1 ID, CLIENTAGENT_ID
        FROM CLIENT
        WHERE ID = @ID;
        """;

    private const string ClientLookupByCodeSql = """
        SELECT TOP 1 ID, CLIENTAGENT_ID
        FROM CLIENT
        WHERE CODE = @CODE;
        """;

    private const string ClientLookupByTaxNoSql = """
        SELECT TOP 1 ID, CLIENTAGENT_ID
        FROM CLIENT
        WHERE TAXNO = @TAXNO;
        """;

    private const string ClientLookupByBulstatSql = """
        SELECT TOP 1 ID, CLIENTAGENT_ID
        FROM CLIENT
        WHERE BULSTAT = @BULSTAT;
        """;

    private const string ClientAgentLookupByIdSql = """
        SELECT TOP 1 ID
        FROM CLIENTAGENT
        WHERE ID = @ID;
        """;

    private const string ClientAgentLookupByClientAndNameSql = """
        SELECT TOP 1 ID
        FROM CLIENTAGENT
        WHERE CLIENT_ID = @CLIENT_ID
          AND NAME = @NAME;
        """;

    private const string ClientInsertSql = """
        INSERT INTO CLIENT
        (
            ID, CODE, NAME, COUNTRY_ID, ADDRESS, PHONE, PHONE2, TAXNO, BULSTAT,
            CLIENTTYPE_ID, REGVAT, MOL, EGN, COMMENT, CLIENTAGENT_ID,
            C_BUSINESSGROUP_ID, C_USER_ID, CONTACTADDRESS, E_MAIL, WEB_ADDRESS,
            CELL_PHONE, CITY_ID
        )
        VALUES
        (
            @ID, @CODE, @NAME, @COUNTRY_ID, @ADDRESS, @PHONE, @PHONE2, @TAXNO, @BULSTAT,
            @CLIENTTYPE_ID, @REGVAT, @MOL, @EGN, @COMMENT, @CLIENTAGENT_ID,
            @C_BUSINESSGROUP_ID, @C_USER_ID, @CONTACTADDRESS, @E_MAIL, @WEB_ADDRESS,
            @CELL_PHONE, @CITY_ID
        );
        """;

    private const string ClientUpdateAgentSql = """
        UPDATE CLIENT
        SET CLIENTAGENT_ID = @CLIENTAGENT_ID
        WHERE ID = @ID;
        """;

    private const string ClientAgentInsertSql = """
        INSERT INTO CLIENTAGENT
        (
            ID, CLIENT_ID, NAME, PHONE, PHONE2, EGN, PASSSERIES, PASSNO, PASSDATE,
            PASSUNIT, COMMENT, C_BUSINESSGROUP_ID, C_USER_ID, ADDRESS, DUTY_ID,
            E_MAIL, WEB_ADDRESS, CELL_PHONE, CRM_ADDRESS
        )
        VALUES
        (
            @ID, @CLIENT_ID, @NAME, @PHONE, @PHONE2, @EGN, @PASSSERIES, @PASSNO, @PASSDATE,
            @PASSUNIT, @COMMENT, @C_BUSINESSGROUP_ID, @C_USER_ID, @ADDRESS, @DUTY_ID,
            @E_MAIL, @WEB_ADDRESS, @CELL_PHONE, @CRM_ADDRESS
        );
        """;

    private const string ArticleLookupByCodeSql = """
        SELECT TOP 1
            a.ID,
            a.CODE,
            a.NAME,
            a.NAME_LATIN,
            a.DIMENSION_ID,
            a.SUPPLIER_ID,
            a.CODEEAN,
            a.COLOR_B,
            a.COLOR_E,
            a.ASIZE,
            a.MATERIAL_B,
            a.MATERIAL_E,
            a.SHORTDESCR_B,
            a.SHORTDESCR_E,
            a.SALEPRICE,
            a.SELFPRICE,
            a.VATGROUP_ID,
            vg.PERCENTVALUE
        FROM ARTICLE a
        LEFT JOIN VATGROUP vg ON vg.ID = a.VATGROUP_ID
        WHERE a.CODE = @CODE;
        """;

    private const string SaleDetailInsertSql = """
        INSERT INTO SALEDETAIL
        (
            ID, SALE_ID, ARTICLE_ID, CODE, NAME, NAME_LATIN, DIMENSION_ID,
            COLOR_B, COLOR_E, ASIZE, MATERIAL_B, MATERIAL_E, SHORTDESCR_B, SHORTDESCR_E,
            CATALOGPRICE,
            QUANTITY, QUANTITYGET, QUANTITYRETURN, QUANTITYPAYED, SINGLEPRICE,
            DISCOUNT, TOTALPRICE, SINPR_DISCOUNT, SINPR_TOTDISCOUNT, VAT, TOTALVAT, SELFPRICE, DELIVERYDATE, C_BUSINESSGROUP_ID,
            C_USER_ID, CATALOGCURRENCY_ID, SUPPLIER_ID, CURRENCYTOTALVAT, CODEEAN
        )
        VALUES
        (
            @ID, @SALE_ID, @ARTICLE_ID, @CODE, @NAME, @NAME_LATIN, @DIMENSION_ID,
            @COLOR_B, @COLOR_E, @ASIZE, @MATERIAL_B, @MATERIAL_E, @SHORTDESCR_B, @SHORTDESCR_E,
            @CATALOGPRICE,
            @QUANTITY, @QUANTITYGET, @QUANTITYRETURN, @QUANTITYPAYED, @SINGLEPRICE,
            @DISCOUNT, @TOTALPRICE, @SINPR_DISCOUNT, @SINPR_TOTDISCOUNT, @VAT, @TOTALVAT, @SELFPRICE, @DELIVERYDATE, @C_BUSINESSGROUP_ID,
            @C_USER_ID, @CATALOGCURRENCY_ID, @SUPPLIER_ID, @CURRENCYTOTALVAT, @CODEEAN
        );
        """;

    private readonly string _connectionString = configuration.GetConnectionString("KmDatabase")
        ?? throw new InvalidOperationException("Connection string 'KmDatabase' is missing.");
    private readonly ILogger<SalesService> _logger = logger;

    public async Task<CreateSaleResponse> CreateSaleAsync(SaleUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.Details.Count == 0)
        {
            throw new InvalidOperationException("At least one sale detail is required.");
        }

        var saleDate = request.SaleDate ?? DateTime.Now;
        _logger.LogInformation(
            "Starting sale import. SaleNo={SaleNo}, RequestedSaleId={RequestedSaleId}, ClientId={ClientId}, DetailCount={DetailCount}",
            request.SaleNo,
            request.Id,
            request.ClientId ?? request.Client?.Code ?? request.Client?.TaxNo ?? request.Client?.Bulstat,
            request.Details.Count);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        _logger.LogInformation("SQL connection opened for sale import. SaleNo={SaleNo}", request.SaleNo);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _logger.LogInformation("SQL transaction started. SaleNo={SaleNo}", request.SaleNo);

        try
        {
            var saleId = NormalizeId(request.Id)
                ?? await GetNextIdAsync(connection, (SqlTransaction)transaction, "SALES", cancellationToken);

            var saleResolution = await ResolveSaleAsync(
                connection,
                (SqlTransaction)transaction,
                request,
                saleId,
                cancellationToken);
            saleId = saleResolution.SaleId;
            _logger.LogInformation(
                "Sale header resolved. SaleNo={SaleNo}, SaleId={SaleId}, ExistingSale={ExistingSale}, SkipUpdate={SkipUpdate}",
                request.SaleNo,
                saleId,
                saleResolution.Exists,
                saleResolution.SkipUpdate);

            if (saleResolution.SkipUpdate)
            {
                var existingTotals = await LoadSaleTotalsAsync(
                    connection,
                    (SqlTransaction)transaction,
                    saleId,
                    cancellationToken);
                var existingDetailCount = await LoadSaleDetailCountAsync(
                    connection,
                    (SqlTransaction)transaction,
                    saleId,
                    cancellationToken);

                _logger.LogWarning(
                    "Skipping sale update because existing document is not eligible. SaleId={SaleId}, SaleNo={SaleNo}, DocStatusId={DocStatusId}, Sync={Sync}",
                    saleId,
                    request.SaleNo,
                    saleResolution.DocStatusId,
                    saleResolution.Sync);

                await transaction.CommitAsync(cancellationToken);

                return new CreateSaleResponse
                {
                    SaleId = saleId,
                    SaleNo = request.SaleNo,
                    ClientId = saleResolution.ClientId,
                    ClientAgentId = saleResolution.ClientAgentId,
                    ClientCreated = false,
                    ClientAgentCreated = false,
                    TotalWithoutVat = existingTotals.TotalWithoutVat,
                    Vat = existingTotals.Vat,
                    TotalWithVat = existingTotals.TotalWithVat,
                    DetailCount = existingDetailCount
                };
            }

            var clientInfo = await EnsureClientAsync(
                connection,
                (SqlTransaction)transaction,
                request,
                cancellationToken);
            _logger.LogInformation(
                "Client resolved. SaleNo={SaleNo}, ClientId={ClientId}, ClientAgentId={ClientAgentId}, ClientCreated={ClientCreated}, ClientAgentCreated={ClientAgentCreated}",
                request.SaleNo,
                clientInfo.ClientId,
                clientInfo.ClientAgentId,
                clientInfo.ClientCreated,
                clientInfo.ClientAgentCreated);

            var preparedDetails = await PrepareSaleDetailsAsync(
                connection,
                (SqlTransaction)transaction,
                request.Details,
                cancellationToken);
            _logger.LogInformation(
                "Prepared sale details. SaleId={SaleId}, DetailCount={DetailCount}",
                saleId,
                preparedDetails.Count);

            var totalWithoutVat = preparedDetails.Sum(static prepared => prepared.NetTotal);
            var totalVat = preparedDetails.Sum(static prepared => prepared.VatTotal);
            var totalWithVat = preparedDetails.Sum(static prepared => prepared.TotalWithVat);
            var discountSum = preparedDetails.Sum(static prepared => prepared.Detail.Discount);
            _logger.LogInformation(
                "Calculated in-memory totals before persistence. SaleId={SaleId}, NetTotal={NetTotal}, Vat={Vat}, TotalWithVat={TotalWithVat}, DiscountPercentSum={DiscountPercentSum}",
                saleId,
                totalWithoutVat,
                totalVat,
                totalWithVat,
                discountSum);

            await SaveSaleAsync(
                connection,
                (SqlTransaction)transaction,
                request,
                clientInfo,
                saleResolution.Exists,
                saleId,
                saleDate,
                totalWithoutVat,
                totalVat,
                totalWithVat,
                discountSum,
                cancellationToken);
            _logger.LogInformation("Saved sale header before inserting details. SaleId={SaleId}", saleId);

            var detailIds = new List<string>(preparedDetails.Count);

            foreach (var preparedDetail in preparedDetails)
            {
                var detailId = NormalizeId(preparedDetail.Detail.Id)
                    ?? await GetNextIdAsync(connection, (SqlTransaction)transaction, "SALEDETAIL", cancellationToken);
                detailIds.Add(detailId);

                await InsertSaleDetailAsync(
                    connection,
                    (SqlTransaction)transaction,
                    preparedDetail,
                    request,
                    saleId,
                    detailId,
                    cancellationToken);

                _logger.LogInformation(
                    "Inserted sale detail. SaleId={SaleId}, SaleDetailId={SaleDetailId}, ArticleId={ArticleId}, ArticleCode={ArticleCode}, Quantity={Quantity}, SinglePrice={SinglePrice}, DiscountPercent={DiscountPercent}, TotalWithoutVat={TotalWithoutVat}, Vat={Vat}, TotalWithVat={TotalWithVat}",
                    saleId,
                    detailId,
                    preparedDetail.Article.ArticleId,
                    preparedDetail.Article.Code,
                    preparedDetail.Detail.Quantity,
                    preparedDetail.Detail.SinglePrice,
                    preparedDetail.Detail.Discount,
                    preparedDetail.NetTotal,
                    preparedDetail.VatTotal,
                    preparedDetail.TotalWithVat);
            }

            var saleTotals = await LoadSaleTotalsAsync(
                connection,
                (SqlTransaction)transaction,
                saleId,
                cancellationToken);
            _logger.LogInformation(
                "Loaded persisted sale totals from SALEDETAIL. SaleId={SaleId}, NetTotal={NetTotal}, Vat={Vat}, TotalWithVat={TotalWithVat}, DiscountAmount={DiscountAmount}",
                saleId,
                saleTotals.TotalWithoutVat,
                saleTotals.Vat,
                saleTotals.TotalWithVat,
                saleTotals.DiscountSum);

            await SaveSaleAsync(
                connection,
                (SqlTransaction)transaction,
                request,
                clientInfo,
                true,
                saleId,
                saleDate,
                saleTotals.TotalWithoutVat,
                saleTotals.Vat,
                saleTotals.TotalWithVat,
                saleTotals.DiscountSum,
                cancellationToken);
            _logger.LogInformation("Updated sale header from persisted detail totals. SaleId={SaleId}", saleId);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Sale import committed successfully. SaleId={SaleId}, SaleNo={SaleNo}, DetailCount={DetailCount}",
                saleId,
                request.SaleNo,
                detailIds.Count);

            return new CreateSaleResponse
            {
                SaleId = saleId,
                SaleNo = request.SaleNo,
                ClientId = clientInfo.ClientId,
                ClientAgentId = clientInfo.ClientAgentId,
                ClientCreated = clientInfo.ClientCreated,
                ClientAgentCreated = clientInfo.ClientAgentCreated,
                TotalWithoutVat = saleTotals.TotalWithoutVat,
                Vat = saleTotals.Vat,
                TotalWithVat = saleTotals.TotalWithVat,
                DetailCount = detailIds.Count,
                DetailIds = detailIds
            };
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                exception,
                "Sale import failed and transaction rolled back. SaleNo={SaleNo}, RequestedSaleId={RequestedSaleId}",
                request.SaleNo,
                request.Id);
            throw;
        }
    }

    private static async Task SaveSaleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        ResolvedClientInfo clientInfo,
        bool existingSale,
        string saleId,
        DateTime saleDate,
        double totalWithoutVat,
        double totalVat,
        double totalWithVat,
        double discountSum,
        CancellationToken cancellationToken)
    {
        var commandText = existingSale ? SalesUpdateSql : SalesInsertSql;

        await using var command = new SqlCommand(commandText, connection, transaction);
        command.Parameters.AddWithValue("@ID", saleId);
        command.Parameters.AddWithValue("@SALENO", DbValue(request.SaleNo));
        command.Parameters.AddWithValue("@SALEDATE", saleDate);
        command.Parameters.AddWithValue("@SALETYPE_ID", DbValue(request.SaleTypeId));
        command.Parameters.AddWithValue("@CLIENT_ID", DbValue(clientInfo.ClientId));
        command.Parameters.AddWithValue("@BUSINESSGROUP_ID", DbValue(request.BusinessGroupId));
        command.Parameters.AddWithValue("@USER_ID", DbValue(request.UserId));
        command.Parameters.AddWithValue("@PRICE", 0d);
        command.Parameters.AddWithValue("@DISCOUNT", 0d);
        command.Parameters.AddWithValue("@TOTALPRICE", totalWithoutVat);
        command.Parameters.AddWithValue("@PAYEDSUM", 0d);
        command.Parameters.AddWithValue("@DEADLINE", DbValue(request.Deadline ?? saleDate));
        command.Parameters.AddWithValue("@PAYSTATUS_ID", DbValue(request.PayStatusId));
        command.Parameters.AddWithValue("@DOCSTATUS_ID", DbValue(request.DocStatusId));
        command.Parameters.AddWithValue("@VAT", totalVat);
        command.Parameters.AddWithValue("@TOTALVAT", totalWithVat);
        command.Parameters.AddWithValue("@CURRENCY_ID", DbValue(request.CurrencyId));
        command.Parameters.AddWithValue("@CURRENCYVALUE", totalWithVat);
        command.Parameters.AddWithValue("@CURRENCYRATE", request.CurrencyRate ?? 1d);
        command.Parameters.AddWithValue("@PAYEDCURRENCY", 0d);
        command.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", DbValue(request.BusinessGroupId));
        command.Parameters.AddWithValue("@C_USER_ID", DbValue(request.UserId));
        command.Parameters.AddWithValue("@DISCOUNTSUM", 0d);
        command.Parameters.AddWithValue("@CVWITHOUTVAT", totalWithoutVat);
        command.Parameters.AddWithValue("@CLIENTAGENT_ID", DbValue(clientInfo.ClientAgentId ?? request.ClientAgentId));
        command.Parameters.AddWithValue("@DESIGNER_ID", DbValue(request.DesignerId));
        command.Parameters.AddWithValue("@CONTRACTNO", DbValue(request.ContractNo));
        command.Parameters.AddWithValue("@CONTRACTDATE", DbValue(request.ContractDate ?? saleDate));
        command.Parameters.AddWithValue("@UNP", DbValue(request.Unp));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SaleResolution> ResolveSaleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        string generatedSaleId,
        CancellationToken cancellationToken)
    {
        var saleNo = NormalizeString(request.SaleNo);

        if (string.IsNullOrWhiteSpace(saleNo))
        {
            request.Id = generatedSaleId;
            _logger.LogInformation(
                "Sale lookup skipped because SaleNo is missing. GeneratedSaleId={GeneratedSaleId}, SaleNo={SaleNo}",
                generatedSaleId,
                saleNo);
            return new SaleResolution
            {
                SaleId = generatedSaleId,
                Exists = false
            };
        }

        await using var command = new SqlCommand(ExistingSaleLookupSql, connection, transaction);
        command.Parameters.AddWithValue("@SALENO", saleNo);
        string? existingSaleId;
        string? existingClientId;
        string? existingClientAgentId;
        int? docStatusId;
        int? sync;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                request.Id = generatedSaleId;
                _logger.LogInformation(
                    "Existing sale not found by SaleNo. Using new sale id. GeneratedSaleId={GeneratedSaleId}, SaleNo={SaleNo}",
                    generatedSaleId,
                    saleNo);
                return new SaleResolution
                {
                    SaleId = generatedSaleId,
                    Exists = false
                };
            }

            existingSaleId = reader.GetString(0);
            existingClientId = reader.IsDBNull(1) ? null : reader.GetString(1);
            existingClientAgentId = reader.IsDBNull(2) ? null : reader.GetString(2);
            docStatusId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            sync = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
        }

        var canUpdateExistingSale = docStatusId == 1 && sync == 1;

        if (!canUpdateExistingSale)
        {
            request.Id = existingSaleId;
            _logger.LogInformation(
                "Existing sale update blocked by eligibility rule. SaleId={SaleId}, SaleNo={SaleNo}, DocStatusId={DocStatusId}, Sync={Sync}",
                existingSaleId,
                saleNo,
                docStatusId,
                sync);
            return new SaleResolution
            {
                SaleId = existingSaleId,
                Exists = true,
                SkipUpdate = true,
                ClientId = existingClientId,
                ClientAgentId = existingClientAgentId,
                DocStatusId = docStatusId,
                Sync = sync
            };
        }

        await using var deleteCommand = new SqlCommand(DeleteSaleDetailsSql, connection, transaction);
        deleteCommand.Parameters.AddWithValue("@SALE_ID", existingSaleId);
        var deletedDetailCount = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "Existing sale found. Deleted old sale details before refresh. SaleId={SaleId}, SaleNo={SaleNo}, DeletedDetailCount={DeletedDetailCount}",
            existingSaleId,
            saleNo,
            deletedDetailCount);

        request.Id = existingSaleId;
        return new SaleResolution
        {
            SaleId = existingSaleId,
            Exists = true,
            ClientId = existingClientId,
            ClientAgentId = existingClientAgentId,
            DocStatusId = docStatusId,
            Sync = sync
        };
    }

    private async Task<ResolvedClientInfo> EnsureClientAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var resolved = await FindExistingClientAsync(connection, transaction, request, cancellationToken);

        if (resolved is null)
        {
            if (request.Client is null)
            {
                if (!string.IsNullOrWhiteSpace(request.ClientId))
                {
                    throw new InvalidOperationException(
                        $"Client '{request.ClientId}' was not found. Include the 'client' section in the request so it can be created.");
                }

                _logger.LogInformation(
                    "No client section provided and no existing client resolved. Continuing with request client references only. ClientId={ClientId}, ClientAgentId={ClientAgentId}",
                    request.ClientId,
                    request.ClientAgentId);

                return new ResolvedClientInfo
                {
                    ClientId = request.ClientId,
                    ClientAgentId = request.ClientAgentId
                };
            }

            resolved = await CreateClientAsync(connection, transaction, request, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Existing client matched and will be reused. ClientId={ClientId}, ClientAgentId={ClientAgentId}",
                resolved.ClientId,
                resolved.ClientAgentId);
        }

        if (request.Client is not null)
        {
            var ensuredAgentId = await EnsureClientAgentAsync(
                connection,
                transaction,
                request,
                resolved.ClientId!,
                resolved.ClientAgentId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(ensuredAgentId.AgentId))
            {
                resolved.ClientAgentId = ensuredAgentId.AgentId;
                resolved.ClientAgentCreated |= ensuredAgentId.Created;
                _logger.LogInformation(
                    "Client agent ensured. ClientId={ClientId}, ClientAgentId={ClientAgentId}, Created={Created}",
                    resolved.ClientId,
                    resolved.ClientAgentId,
                    ensuredAgentId.Created);
            }
        }

        return resolved;
    }

    private static async Task<ResolvedClientInfo?> FindExistingClientAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            (NormalizeId(request.Client?.Id) ?? NormalizeId(request.ClientId), ClientLookupByIdSql, "@ID"),
            (NormalizeString(request.Client?.Code), ClientLookupByCodeSql, "@CODE"),
            (NormalizeString(request.Client?.TaxNo), ClientLookupByTaxNoSql, "@TAXNO"),
            (NormalizeString(request.Client?.Bulstat), ClientLookupByBulstatSql, "@BULSTAT")
        };

        foreach (var (value, sql, parameterName) in candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(parameterName, value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new ResolvedClientInfo
                {
                    ClientId = reader.GetString(0),
                    ClientAgentId = reader.IsDBNull(1) ? null : reader.GetString(1)
                };
            }
        }

        return null;
    }

    private async Task<ResolvedClientInfo> CreateClientAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var client = request.Client
            ?? throw new InvalidOperationException("Client data is required to create a missing client.");

        var clientId = NormalizeId(client.Id)
            ?? NormalizeId(request.ClientId)
            ?? await GetNextIdAsync(connection, transaction, "CLIENT", cancellationToken);
        var clientAgentId = NormalizeId(request.ClientAgentId);

        await using var command = new SqlCommand(ClientInsertSql, connection, transaction);
        command.Parameters.AddWithValue("@ID", clientId);
        command.Parameters.AddWithValue("@CODE", DbValue(client.Code));
        command.Parameters.AddWithValue("@NAME", DbValue(client.Name));
        command.Parameters.AddWithValue("@COUNTRY_ID", DbValue(client.CountryId));
        command.Parameters.AddWithValue("@ADDRESS", DbValue(client.Address));
        command.Parameters.AddWithValue("@PHONE", DbValue(client.Phone));
        command.Parameters.AddWithValue("@PHONE2", DbValue(client.Phone2));
        command.Parameters.AddWithValue("@TAXNO", DbValue(client.TaxNo));
        command.Parameters.AddWithValue("@BULSTAT", DbValue(client.Bulstat));
        command.Parameters.AddWithValue("@CLIENTTYPE_ID", DbValue(client.ClientTypeId));
        command.Parameters.AddWithValue("@REGVAT", DbValue(client.RegVat));
        command.Parameters.AddWithValue("@MOL", DbValue(client.Mol));
        command.Parameters.AddWithValue("@EGN", DbValue(client.Egn));
        command.Parameters.AddWithValue("@COMMENT", DbValue(client.Comment));
        command.Parameters.AddWithValue("@CLIENTAGENT_ID", DbValue(clientAgentId));
        command.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", DbValue(request.BusinessGroupId));
        command.Parameters.AddWithValue("@C_USER_ID", DbValue(request.UserId));
        command.Parameters.AddWithValue("@CONTACTADDRESS", DbValue(client.ContactAddress));
        command.Parameters.AddWithValue("@E_MAIL", DbValue(client.Email));
        command.Parameters.AddWithValue("@WEB_ADDRESS", DbValue(client.WebAddress));
        command.Parameters.AddWithValue("@CELL_PHONE", DbValue(client.CellPhone));
        command.Parameters.AddWithValue("@CITY_ID", DbValue(client.CityId));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "Created client. ClientId={ClientId}, ClientCode={ClientCode}, ClientName={ClientName}",
            clientId,
            client.Code,
            client.Name);

        return new ResolvedClientInfo
        {
            ClientId = clientId,
            ClientAgentId = clientAgentId,
            ClientCreated = true
        };
    }

    private async Task<(string? AgentId, bool Created)> EnsureClientAgentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleUpsertRequest request,
        string clientId,
        string? existingClientAgentId,
        CancellationToken cancellationToken)
    {
        var agent = request.Client?.Agent;
        var candidateId = NormalizeId(agent?.Id) ?? NormalizeId(request.ClientAgentId) ?? NormalizeId(existingClientAgentId);

        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            await using var idCommand = new SqlCommand(ClientAgentLookupByIdSql, connection, transaction);
            idCommand.Parameters.AddWithValue("@ID", candidateId);
            var existing = await idCommand.ExecuteScalarAsync(cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing client agent by id. ClientId={ClientId}, ClientAgentId={ClientAgentId}",
                    clientId,
                    candidateId);
                return (candidateId, false);
            }
        }

        var fallbackName = NormalizeString(agent?.Name)
            ?? NormalizeString(request.Client?.Mol)
            ?? NormalizeString(request.Client?.Name);

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            await using var nameCommand = new SqlCommand(ClientAgentLookupByClientAndNameSql, connection, transaction);
            nameCommand.Parameters.AddWithValue("@CLIENT_ID", clientId);
            nameCommand.Parameters.AddWithValue("@NAME", fallbackName);
            var existingByName = await nameCommand.ExecuteScalarAsync(cancellationToken);
            if (existingByName is string existingAgentId)
            {
                await UpdateClientAgentReferenceAsync(connection, transaction, clientId, existingAgentId, cancellationToken);
                _logger.LogInformation(
                    "Reusing existing client agent by name and updating client reference. ClientId={ClientId}, ClientAgentId={ClientAgentId}, AgentName={AgentName}",
                    clientId,
                    existingAgentId,
                    fallbackName);
                return (existingAgentId, false);
            }
        }

        if (request.Client is null)
        {
            _logger.LogInformation(
                "Client agent creation skipped because client payload is missing. ClientId={ClientId}, ExistingClientAgentId={ExistingClientAgentId}",
                clientId,
                existingClientAgentId);
            return (existingClientAgentId, false);
        }

        var createdAgentId = NormalizeId(agent?.Id)
            ?? NormalizeId(request.ClientAgentId)
            ?? await GetNextIdAsync(connection, transaction, "CLIENTAGENT", cancellationToken);

        await using var command = new SqlCommand(ClientAgentInsertSql, connection, transaction);
        command.Parameters.AddWithValue("@ID", createdAgentId);
        command.Parameters.AddWithValue("@CLIENT_ID", clientId);
        command.Parameters.AddWithValue("@NAME", DbValue(fallbackName));
        command.Parameters.AddWithValue("@PHONE", DbValue(agent?.Phone ?? request.Client.Phone));
        command.Parameters.AddWithValue("@PHONE2", DbValue(agent?.Phone2 ?? request.Client.Phone2));
        command.Parameters.AddWithValue("@EGN", DbValue(agent?.Egn ?? request.Client.Egn));
        command.Parameters.AddWithValue("@PASSSERIES", DbValue(agent?.PassSeries));
        command.Parameters.AddWithValue("@PASSNO", DbValue(agent?.PassNo));
        command.Parameters.AddWithValue("@PASSDATE", DbValue(agent?.PassDate));
        command.Parameters.AddWithValue("@PASSUNIT", DbValue(agent?.PassUnit));
        command.Parameters.AddWithValue("@COMMENT", DbValue(agent?.Comment ?? request.Client.Comment));
        command.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", DbValue(request.BusinessGroupId));
        command.Parameters.AddWithValue("@C_USER_ID", DbValue(request.UserId));
        command.Parameters.AddWithValue("@ADDRESS", DbValue(agent?.Address ?? request.Client.Address));
        command.Parameters.AddWithValue("@DUTY_ID", DbValue(agent?.DutyId));
        command.Parameters.AddWithValue("@E_MAIL", DbValue(agent?.Email ?? request.Client.Email));
        command.Parameters.AddWithValue("@WEB_ADDRESS", DbValue(agent?.WebAddress ?? request.Client.WebAddress));
        command.Parameters.AddWithValue("@CELL_PHONE", DbValue(agent?.CellPhone ?? request.Client.CellPhone));
        command.Parameters.AddWithValue("@CRM_ADDRESS", DbValue(agent?.CrmAddress));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await UpdateClientAgentReferenceAsync(connection, transaction, clientId, createdAgentId, cancellationToken);
        _logger.LogInformation(
            "Created client agent and linked it to client. ClientId={ClientId}, ClientAgentId={ClientAgentId}, AgentName={AgentName}",
            clientId,
            createdAgentId,
            fallbackName);

        return (createdAgentId, true);
    }

    private static async Task UpdateClientAgentReferenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string clientId,
        string clientAgentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(ClientUpdateAgentSql, connection, transaction);
        command.Parameters.AddWithValue("@ID", clientId);
        command.Parameters.AddWithValue("@CLIENTAGENT_ID", clientAgentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<PreparedSaleDetail>> PrepareSaleDetailsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyCollection<SaleDetailUpsertRequest> details,
        CancellationToken cancellationToken)
    {
        var preparedDetails = new List<PreparedSaleDetail>(details.Count);

        foreach (var detail in details)
        {
            var article = await ResolveArticleAsync(connection, transaction, detail, cancellationToken);
            var netTotal = GetDetailNetTotal(detail);
            var vatTotal = GetDetailVatTotal(netTotal, article.VatPercentValue);

            preparedDetails.Add(new PreparedSaleDetail
            {
                Detail = detail,
                Article = article,
                NetTotal = netTotal,
                VatTotal = vatTotal,
                TotalWithVat = netTotal + vatTotal
            });

            _logger.LogInformation(
                "Prepared detail from article. ArticleId={ArticleId}, ArticleCode={ArticleCode}, Quantity={Quantity}, SinglePrice={SinglePrice}, DiscountPercent={DiscountPercent}, VatPercent={VatPercent}, TotalWithoutVat={TotalWithoutVat}, Vat={Vat}, TotalWithVat={TotalWithVat}",
                article.ArticleId,
                article.Code,
                detail.Quantity,
                detail.SinglePrice,
                detail.Discount,
                article.VatPercentValue,
                netTotal,
                vatTotal,
                netTotal + vatTotal);
        }

        return preparedDetails
            .OrderBy(
                static prepared => prepared.Article.Code,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task InsertSaleDetailAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PreparedSaleDetail preparedDetail,
        SaleUpsertRequest request,
        string saleId,
        string detailId,
        CancellationToken cancellationToken)
    {
        var detail = preparedDetail.Detail;
        var article = preparedDetail.Article;
        var netTotal = preparedDetail.NetTotal;
        var vatTotal = preparedDetail.VatTotal;
        var totalWithVat = preparedDetail.TotalWithVat;
        var sourceSinglePrice = detail.SourceSinglePrice ?? detail.SinglePrice;

        await using var command = new SqlCommand(SaleDetailInsertSql, connection, transaction);
        command.Parameters.AddWithValue("@ID", detailId);
        command.Parameters.AddWithValue("@SALE_ID", saleId);
        command.Parameters.AddWithValue("@ARTICLE_ID", article.ArticleId);
        command.Parameters.AddWithValue("@CODE", article.Code);
        command.Parameters.AddWithValue("@NAME", DbValue(article.Name ?? detail.Name));
        command.Parameters.AddWithValue("@NAME_LATIN", DbValue(article.NameLatin ?? article.Name ?? detail.NameLatin ?? detail.Name));
        command.Parameters.AddWithValue("@DIMENSION_ID", DbValue(article.DimensionId ?? detail.DimensionId));
        command.Parameters.AddWithValue("@COLOR_B", DbValue(article.ColorB));
        command.Parameters.AddWithValue("@COLOR_E", DbValue(article.ColorE));
        command.Parameters.AddWithValue("@ASIZE", DbValue(article.Asize));
        command.Parameters.AddWithValue("@MATERIAL_B", DbValue(article.MaterialB));
        command.Parameters.AddWithValue("@MATERIAL_E", DbValue(article.MaterialE));
        command.Parameters.AddWithValue("@SHORTDESCR_B", DbValue(article.ShortDescrB));
        command.Parameters.AddWithValue("@SHORTDESCR_E", DbValue(article.ShortDescrE));
        command.Parameters.AddWithValue("@CATALOGPRICE", DbValue(article.SalePrice));
        command.Parameters.AddWithValue("@QUANTITY", detail.Quantity);
        command.Parameters.AddWithValue("@QUANTITYGET", 0d);
        command.Parameters.AddWithValue("@QUANTITYRETURN", 0d);
        command.Parameters.AddWithValue("@QUANTITYPAYED", 0d);
        command.Parameters.AddWithValue("@SINGLEPRICE", detail.SinglePrice);
        command.Parameters.AddWithValue("@DISCOUNT", detail.Discount);
        command.Parameters.AddWithValue("@TOTALPRICE", netTotal);
        command.Parameters.AddWithValue("@SINPR_DISCOUNT", sourceSinglePrice);
        command.Parameters.AddWithValue("@SINPR_TOTDISCOUNT", sourceSinglePrice);
        command.Parameters.AddWithValue("@VAT", vatTotal);
        command.Parameters.AddWithValue("@TOTALVAT", totalWithVat);
        command.Parameters.AddWithValue("@SELFPRICE", DbValue(article.SelfPrice));
        command.Parameters.AddWithValue("@DELIVERYDATE", DbValue(detail.DeliveryDate ?? request.SaleDate ?? DateTime.Now));
        command.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", DbValue(request.BusinessGroupId));
        command.Parameters.AddWithValue("@C_USER_ID", DbValue(request.UserId));
        command.Parameters.AddWithValue("@CATALOGCURRENCY_ID", DbValue(request.CurrencyId));
        command.Parameters.AddWithValue("@SUPPLIER_ID", DbValue(detail.SupplierId ?? article.SupplierId));
        command.Parameters.AddWithValue("@CURRENCYTOTALVAT", totalWithVat);
        command.Parameters.AddWithValue("@CODEEAN", DbValue(detail.CodeEan ?? article.CodeEan));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ArticleLookupResult> ResolveArticleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SaleDetailUpsertRequest detail,
        CancellationToken cancellationToken)
    {
        var code = NormalizeString(detail.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Each sale detail must include article code.");
        }

        await using var command = new SqlCommand(ArticleLookupByCodeSql, connection, transaction);
        command.Parameters.AddWithValue("@CODE", code);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Article with code '{code}' was not found in ARTICLE.");
        }

        return new ArticleLookupResult
        {
            ArticleId = reader.GetString(0),
            Code = reader.IsDBNull(1) ? code : reader.GetString(1),
            Name = reader.IsDBNull(2) ? null : reader.GetString(2),
            NameLatin = reader.IsDBNull(3) ? null : reader.GetString(3),
            DimensionId = reader.IsDBNull(4) ? null : reader.GetString(4),
            SupplierId = reader.IsDBNull(5) ? null : reader.GetString(5),
            CodeEan = reader.IsDBNull(6) ? null : reader.GetString(6),
            ColorB = reader.IsDBNull(7) ? null : reader.GetString(7),
            ColorE = reader.IsDBNull(8) ? null : reader.GetString(8),
            Asize = reader.IsDBNull(9) ? null : reader.GetString(9),
            MaterialB = reader.IsDBNull(10) ? null : reader.GetString(10),
            MaterialE = reader.IsDBNull(11) ? null : reader.GetString(11),
            ShortDescrB = reader.IsDBNull(12) ? null : reader.GetString(12),
            ShortDescrE = reader.IsDBNull(13) ? null : reader.GetString(13),
            SalePrice = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            SelfPrice = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            VatGroupId = reader.IsDBNull(16) ? null : reader.GetString(16),
            VatPercentValue = reader.IsDBNull(17) ? null : reader.GetDouble(17)
        };
    }

    private static double GetDetailNetTotal(SaleDetailUpsertRequest detail)
    {
        return Math.Round(GetDetailGrossTotal(detail) - GetDetailDiscountAmount(detail), 3, MidpointRounding.AwayFromZero);
    }

    private static double GetDetailGrossTotal(SaleDetailUpsertRequest detail)
    {
        return Math.Round(detail.Quantity * detail.SinglePrice, 3, MidpointRounding.AwayFromZero);
    }

    private static double GetDetailDiscountAmount(SaleDetailUpsertRequest detail)
    {
        if (detail.Discount <= 0)
        {
            return 0d;
        }

        return Math.Round(GetDetailGrossTotal(detail) * (detail.Discount / 100d), 3, MidpointRounding.AwayFromZero);
    }

    private static double GetDetailVatTotal(double netTotal, double? vatPercentValue)
    {
        if (!vatPercentValue.HasValue || vatPercentValue.Value <= 0)
        {
            return 0d;
        }

        return Math.Round(netTotal * (vatPercentValue.Value / 100d), 3, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static async Task<string> GetNextIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(NextIdProcedureName, connection, transaction)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@NTABLE", tableName);
        var output = new SqlParameter("@TID", System.Data.SqlDbType.VarChar, 14)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        command.Parameters.Add(output);

        await command.ExecuteNonQueryAsync(cancellationToken);

        var nextId = output.Value as string;
        if (string.IsNullOrWhiteSpace(nextId))
        {
            throw new InvalidOperationException($"Stored procedure {NextIdProcedureName} did not return an ID for table '{tableName}'.");
        }

        return nextId.Trim();
    }

    private static async Task<SaleTotals> LoadSaleTotalsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string saleId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SalesTotalsBySaleSql, connection, transaction);
        command.Parameters.AddWithValue("@SALE_ID", saleId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SaleTotals();
        }

        return new SaleTotals
        {
            TotalWithoutVat = reader.GetDouble(0),
            Vat = reader.GetDouble(1),
            TotalWithVat = reader.GetDouble(2),
            DiscountSum = reader.GetDouble(3)
        };
    }

    private static async Task<int> LoadSaleDetailCountAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string saleId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SaleDetailCountSql, connection, transaction);
        command.Parameters.AddWithValue("@SALE_ID", saleId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int value ? value : Convert.ToInt32(result ?? 0);
    }

    private static object DbValue<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private sealed class ArticleLookupResult
    {
        public string ArticleId { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? NameLatin { get; set; }

        public string? DimensionId { get; set; }

        public string? SupplierId { get; set; }

        public string? CodeEan { get; set; }

        public string? ColorB { get; set; }

        public string? ColorE { get; set; }

        public string? Asize { get; set; }

        public string? MaterialB { get; set; }

        public string? MaterialE { get; set; }

        public string? ShortDescrB { get; set; }

        public string? ShortDescrE { get; set; }

        public double? SalePrice { get; set; }

        public double? SelfPrice { get; set; }

        public string? VatGroupId { get; set; }

        public double? VatPercentValue { get; set; }
    }

    private sealed class PreparedSaleDetail
    {
        public SaleDetailUpsertRequest Detail { get; set; } = new();

        public ArticleLookupResult Article { get; set; } = new();

        public double NetTotal { get; set; }

        public double VatTotal { get; set; }

        public double TotalWithVat { get; set; }
    }

    private sealed class SaleTotals
    {
        public double TotalWithoutVat { get; set; }

        public double Vat { get; set; }

        public double TotalWithVat { get; set; }

        public double DiscountSum { get; set; }
    }

    private sealed class SaleResolution
    {
        public string SaleId { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public bool SkipUpdate { get; set; }

        public string? ClientId { get; set; }

        public string? ClientAgentId { get; set; }

        public int? DocStatusId { get; set; }

        public int? Sync { get; set; }
    }
}
