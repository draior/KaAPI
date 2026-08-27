Add-Type -AssemblyName System.Data

function Get-NextId {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [System.Data.SqlClient.SqlTransaction]$Transaction,
        [string]$TableName
    )

    $command = $Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandText = "dbo.SYS_GETNEXTID"
    [void]$command.Parameters.AddWithValue("@NTABLE", $TableName)
    $output = New-Object System.Data.SqlClient.SqlParameter("@TID", [System.Data.SqlDbType]::VarChar, 14)
    $output.Direction = [System.Data.ParameterDirection]::Output
    [void]$command.Parameters.Add($output)
    [void]$command.ExecuteNonQuery()

    if ([string]::IsNullOrWhiteSpace($output.Value)) {
        throw "dbo.SYS_GETNEXTID did not return an ID for table '$TableName'."
    }

    return $output.Value.ToString().Trim()
}

function DbValue {
    param($Value)

    if ($null -eq $Value) {
        return [DBNull]::Value
    }

    if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) {
        return [DBNull]::Value
    }

    return $Value
}

function TlsCode {
    param([string]$Suffix)

    return ([string]([char]0x0422) + [char]0x041B + [char]0x0421 + $Suffix)
}

function ProCode {
    param([string]$Suffix)

    return ([string]([char]0x041F) + [char]0x0420 + [char]0x041E + $Suffix)
}

$saleNo = "13210"
$saleDate = [datetime]"2026-07-14T00:00:00"
$businessGroupId = "09"
$userId = "00SYSTEM10"
$saleTypeId = 4
$currencyId = "SYSTEM"
$currencyRate = 1.0
$payStatusId = 0
$docStatusId = 1
$clientTaxNo = "BG201828014"
$clientBulstat = "201828014"
$comment = "Imported from order-template-example2.xml"

$details = @(
    [pscustomobject]@{ Code = (TlsCode "62261"); Quantity = 5.0; SinglePrice = 11.17; TotalPrice = 39.09; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "30051"); Quantity = 2.0; SinglePrice = 2.67; TotalPrice = 3.73; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "50100"); Quantity = 3.0; SinglePrice = 3.00; TotalPrice = 6.30; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "30045"); Quantity = 2.0; SinglePrice = 1.83; TotalPrice = 2.57; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "44911"); Quantity = 2.0; SinglePrice = 3.83; TotalPrice = 5.37; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "25053"); Quantity = 1.0; SinglePrice = 6.58; TotalPrice = 4.61; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "38058"); Quantity = 2.0; SinglePrice = 13.17; TotalPrice = 18.44; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (ProCode "40136"); Quantity = 2.0; SinglePrice = 6.83; TotalPrice = 9.57; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "62243"); Quantity = 3.0; SinglePrice = 1.50; TotalPrice = 3.14; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "10101"); Quantity = 3.0; SinglePrice = 3.83; TotalPrice = 8.05; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "38016"); Quantity = 2.0; SinglePrice = 19.92; TotalPrice = 27.89; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "44911"); Quantity = 2.0; SinglePrice = 3.83; TotalPrice = 5.37; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "76757"); Quantity = 3.0; SinglePrice = 7.42; TotalPrice = 15.58; Discount = 0.0; Vat = 0.0 },
    [pscustomobject]@{ Code = (TlsCode "62266"); Quantity = 2.0; SinglePrice = 2.67; TotalPrice = 3.73; Discount = 0.0; Vat = 0.0 }
)

foreach ($detail in $details) {
    $detail.Discount = [math]::Round(($detail.SinglePrice * $detail.Quantity) - $detail.TotalPrice, 2, [MidpointRounding]::AwayFromZero)
    $detail.Vat = 0.0
}

$totalWithoutVat = [math]::Round((($details | Measure-Object -Property TotalPrice -Sum).Sum), 3, [MidpointRounding]::AwayFromZero)
$totalVat = 0.0
$totalWithVat = $totalWithoutVat
$discountSum = [math]::Round((($details | Measure-Object -Property Discount -Sum).Sum), 3, [MidpointRounding]::AwayFromZero)

$connectionString = "Server=ZUCCHETTIBG-DD\TRINITI2019;Database=KM;User Id=sa;Password=68087506;Encrypt=False;TrustServerCertificate=False;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()
$transaction = $connection.BeginTransaction()

try {
    $clientLookup = $connection.CreateCommand()
    $clientLookup.Transaction = $transaction
    $clientLookup.CommandText = @"
SELECT TOP 1 ID, CLIENTAGENT_ID
FROM CLIENT
WHERE TAXNO = @TAXNO OR BULSTAT = @BULSTAT
"@
    [void]$clientLookup.Parameters.AddWithValue("@TAXNO", $clientTaxNo)
    [void]$clientLookup.Parameters.AddWithValue("@BULSTAT", $clientBulstat)
    $reader = $clientLookup.ExecuteReader()

    if (-not $reader.Read()) {
        $reader.Close()
        throw "Client with TAXNO '$clientTaxNo' or BULSTAT '$clientBulstat' was not found."
    }

    $clientId = $reader.GetString(0)
    $clientAgentId = if ($reader.IsDBNull(1)) { $null } else { $reader.GetString(1) }
    $reader.Close()

    $saleLookup = $connection.CreateCommand()
    $saleLookup.Transaction = $transaction
    $saleLookup.CommandText = @"
SELECT TOP 1 ID
FROM SALES
WHERE SALENO = @SALENO
  AND CLIENT_ID = @CLIENT_ID
ORDER BY C_ACTIONDATETIME DESC
"@
    [void]$saleLookup.Parameters.AddWithValue("@SALENO", $saleNo)
    [void]$saleLookup.Parameters.AddWithValue("@CLIENT_ID", $clientId)
    $existingSaleId = $saleLookup.ExecuteScalar()

    $saleId = if ($existingSaleId -is [string] -and -not [string]::IsNullOrWhiteSpace($existingSaleId)) { $existingSaleId } else { Get-NextId -Connection $connection -Transaction $transaction -TableName "SALES" }
    $saleExists = $saleId -eq $existingSaleId

    if ($saleExists) {
        $deleteDetails = $connection.CreateCommand()
        $deleteDetails.Transaction = $transaction
        $deleteDetails.CommandText = "DELETE FROM SALEDETAIL WHERE SALE_ID = @SALE_ID"
        [void]$deleteDetails.Parameters.AddWithValue("@SALE_ID", $saleId)
        [void]$deleteDetails.ExecuteNonQuery()
    }

    $saleCommand = $connection.CreateCommand()
    $saleCommand.Transaction = $transaction
    $saleCommand.CommandText = if ($saleExists) {
@"
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
    COMMENT = @COMMENT,
    C_BUSINESSGROUP_ID = @C_BUSINESSGROUP_ID,
    C_USER_ID = @C_USER_ID,
    DISCOUNTSUM = @DISCOUNTSUM,
    CVWITHOUTVAT = @CVWITHOUTVAT,
    CLIENTAGENT_ID = @CLIENTAGENT_ID,
    DESIGNER_ID = @DESIGNER_ID,
    CONTRACTNO = @CONTRACTNO,
    CONTRACTDATE = @CONTRACTDATE,
    UNP = @UNP
WHERE ID = @ID
"@
    }
    else {
@"
INSERT INTO SALES
(
    ID, SALENO, SALEDATE, SALETYPE_ID, CLIENT_ID, BUSINESSGROUP_ID, USER_ID,
    PRICE, DISCOUNT, TOTALPRICE, PAYEDSUM, DEADLINE, PAYSTATUS_ID, DOCSTATUS_ID,
    VAT, TOTALVAT, CURRENCY_ID, CURRENCYVALUE, CURRENCYRATE, PAYEDCURRENCY,
    COMMENT, C_BUSINESSGROUP_ID, C_USER_ID, DISCOUNTSUM, CVWITHOUTVAT,
    CLIENTAGENT_ID, DESIGNER_ID, CONTRACTNO, CONTRACTDATE, UNP
)
VALUES
(
    @ID, @SALENO, @SALEDATE, @SALETYPE_ID, @CLIENT_ID, @BUSINESSGROUP_ID, @USER_ID,
    @PRICE, @DISCOUNT, @TOTALPRICE, @PAYEDSUM, @DEADLINE, @PAYSTATUS_ID, @DOCSTATUS_ID,
    @VAT, @TOTALVAT, @CURRENCY_ID, @CURRENCYVALUE, @CURRENCYRATE, @PAYEDCURRENCY,
    @COMMENT, @C_BUSINESSGROUP_ID, @C_USER_ID, @DISCOUNTSUM, @CVWITHOUTVAT,
    @CLIENTAGENT_ID, @DESIGNER_ID, @CONTRACTNO, @CONTRACTDATE, @UNP
)
"@
    }

    [void]$saleCommand.Parameters.AddWithValue("@ID", $saleId)
    [void]$saleCommand.Parameters.AddWithValue("@SALENO", $saleNo)
    [void]$saleCommand.Parameters.AddWithValue("@SALEDATE", $saleDate)
    [void]$saleCommand.Parameters.AddWithValue("@SALETYPE_ID", $saleTypeId)
    [void]$saleCommand.Parameters.AddWithValue("@CLIENT_ID", $clientId)
    [void]$saleCommand.Parameters.AddWithValue("@BUSINESSGROUP_ID", $businessGroupId)
    [void]$saleCommand.Parameters.AddWithValue("@USER_ID", $userId)
    [void]$saleCommand.Parameters.AddWithValue("@PRICE", $totalWithoutVat)
    [void]$saleCommand.Parameters.AddWithValue("@DISCOUNT", 0d)
    [void]$saleCommand.Parameters.AddWithValue("@TOTALPRICE", $totalWithoutVat)
    [void]$saleCommand.Parameters.AddWithValue("@PAYEDSUM", 0d)
    [void]$saleCommand.Parameters.AddWithValue("@DEADLINE", $saleDate)
    [void]$saleCommand.Parameters.AddWithValue("@PAYSTATUS_ID", $payStatusId)
    [void]$saleCommand.Parameters.AddWithValue("@DOCSTATUS_ID", $docStatusId)
    [void]$saleCommand.Parameters.AddWithValue("@VAT", $totalVat)
    [void]$saleCommand.Parameters.AddWithValue("@TOTALVAT", $totalWithVat)
    [void]$saleCommand.Parameters.AddWithValue("@CURRENCY_ID", $currencyId)
    [void]$saleCommand.Parameters.AddWithValue("@CURRENCYVALUE", $totalWithVat)
    [void]$saleCommand.Parameters.AddWithValue("@CURRENCYRATE", $currencyRate)
    [void]$saleCommand.Parameters.AddWithValue("@PAYEDCURRENCY", 0d)
    [void]$saleCommand.Parameters.AddWithValue("@COMMENT", $comment)
    [void]$saleCommand.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", $businessGroupId)
    [void]$saleCommand.Parameters.AddWithValue("@C_USER_ID", $userId)
    [void]$saleCommand.Parameters.AddWithValue("@DISCOUNTSUM", 0d)
    [void]$saleCommand.Parameters.AddWithValue("@CVWITHOUTVAT", $totalWithoutVat)
    [void]$saleCommand.Parameters.AddWithValue("@CLIENTAGENT_ID", (DbValue $clientAgentId))
    [void]$saleCommand.Parameters.AddWithValue("@DESIGNER_ID", [DBNull]::Value)
    [void]$saleCommand.Parameters.AddWithValue("@CONTRACTNO", [DBNull]::Value)
    [void]$saleCommand.Parameters.AddWithValue("@CONTRACTDATE", $saleDate)
    [void]$saleCommand.Parameters.AddWithValue("@UNP", [DBNull]::Value)
    [void]$saleCommand.ExecuteNonQuery()

    foreach ($detail in $details) {
        $articleCommand = $connection.CreateCommand()
        $articleCommand.Transaction = $transaction
        $articleCommand.CommandText = @"
SELECT TOP 1 ID, CODE, NAME, NAME_LATIN, DIMENSION_ID, SUPPLIER_ID, CODEEAN
FROM ARTICLE
WHERE CODE = @CODE
"@
        [void]$articleCommand.Parameters.AddWithValue("@CODE", $detail.Code)
        $reader = $articleCommand.ExecuteReader()

        if (-not $reader.Read()) {
            $reader.Close()
            throw "Article with code '$($detail.Code)' was not found in ARTICLE."
        }

        $articleId = $reader.GetString(0)
        $articleCode = if ($reader.IsDBNull(1)) { $detail.Code } else { $reader.GetString(1) }
        $articleName = if ($reader.IsDBNull(2)) { $null } else { $reader.GetString(2) }
        $articleNameLatin = if ($reader.IsDBNull(3)) { $null } else { $reader.GetString(3) }
        $dimensionId = if ($reader.IsDBNull(4)) { $null } else { $reader.GetString(4) }
        $supplierId = if ($reader.IsDBNull(5)) { $null } else { $reader.GetString(5) }
        $codeEan = if ($reader.IsDBNull(6)) { $null } else { $reader.GetString(6) }
        $reader.Close()

        $insertDetail = $connection.CreateCommand()
        $insertDetail.Transaction = $transaction
        $insertDetail.CommandText = @"
INSERT INTO SALEDETAIL
(
    ID, SALE_ID, ARTICLE_ID, CODE, NAME, NAME_LATIN, DIMENSION_ID,
    QUANTITY, QUANTITYGET, QUANTITYRETURN, QUANTITYPAYED, SINGLEPRICE,
    DISCOUNT, TOTALPRICE, VAT, TOTALVAT, DELIVERYDATE, C_BUSINESSGROUP_ID,
    C_USER_ID, CATALOGCURRENCY_ID, SUPPLIER_ID, CURRENCYTOTALVAT, CODEEAN
)
VALUES
(
    @ID, @SALE_ID, @ARTICLE_ID, @CODE, @NAME, @NAME_LATIN, @DIMENSION_ID,
    @QUANTITY, @QUANTITYGET, @QUANTITYRETURN, @QUANTITYPAYED, @SINGLEPRICE,
    @DISCOUNT, @TOTALPRICE, @VAT, @TOTALVAT, @DELIVERYDATE, @C_BUSINESSGROUP_ID,
    @C_USER_ID, @CATALOGCURRENCY_ID, @SUPPLIER_ID, @CURRENCYTOTALVAT, @CODEEAN
)
"@
        [void]$insertDetail.Parameters.AddWithValue("@ID", (Get-NextId -Connection $connection -Transaction $transaction -TableName "SALEDETAIL"))
        [void]$insertDetail.Parameters.AddWithValue("@SALE_ID", $saleId)
        [void]$insertDetail.Parameters.AddWithValue("@ARTICLE_ID", $articleId)
        [void]$insertDetail.Parameters.AddWithValue("@CODE", $articleCode)
        [void]$insertDetail.Parameters.AddWithValue("@NAME", (DbValue $articleName))
        [void]$insertDetail.Parameters.AddWithValue("@NAME_LATIN", (DbValue $articleNameLatin))
        [void]$insertDetail.Parameters.AddWithValue("@DIMENSION_ID", (DbValue $dimensionId))
        [void]$insertDetail.Parameters.AddWithValue("@QUANTITY", $detail.Quantity)
        [void]$insertDetail.Parameters.AddWithValue("@QUANTITYGET", 0.0)
        [void]$insertDetail.Parameters.AddWithValue("@QUANTITYRETURN", 0.0)
        [void]$insertDetail.Parameters.AddWithValue("@QUANTITYPAYED", $detail.Quantity)
        [void]$insertDetail.Parameters.AddWithValue("@SINGLEPRICE", $detail.SinglePrice)
        [void]$insertDetail.Parameters.AddWithValue("@DISCOUNT", $detail.Discount)
        [void]$insertDetail.Parameters.AddWithValue("@TOTALPRICE", $detail.TotalPrice)
        [void]$insertDetail.Parameters.AddWithValue("@VAT", 0.0)
        [void]$insertDetail.Parameters.AddWithValue("@TOTALVAT", $detail.TotalPrice)
        [void]$insertDetail.Parameters.AddWithValue("@DELIVERYDATE", $saleDate)
        [void]$insertDetail.Parameters.AddWithValue("@C_BUSINESSGROUP_ID", $businessGroupId)
        [void]$insertDetail.Parameters.AddWithValue("@C_USER_ID", $userId)
        [void]$insertDetail.Parameters.AddWithValue("@CATALOGCURRENCY_ID", $currencyId)
        [void]$insertDetail.Parameters.AddWithValue("@SUPPLIER_ID", (DbValue $supplierId))
        [void]$insertDetail.Parameters.AddWithValue("@CURRENCYTOTALVAT", $detail.TotalPrice)
        [void]$insertDetail.Parameters.AddWithValue("@CODEEAN", (DbValue $codeEan))
        [void]$insertDetail.ExecuteNonQuery()
    }

    $transaction.Commit()

    $verify = $connection.CreateCommand()
    $verify.CommandText = @"
SELECT
    s.ID,
    s.SALENO,
    s.CLIENT_ID,
    s.PRICE,
    s.DISCOUNT,
    s.TOTALVAT,
    COUNT(d.ID) AS DETAIL_COUNT
FROM SALES s
LEFT JOIN SALEDETAIL d ON d.SALE_ID = s.ID
WHERE s.ID = @SALE_ID
GROUP BY s.ID, s.SALENO, s.CLIENT_ID, s.PRICE, s.DISCOUNT, s.TOTALVAT
"@
    [void]$verify.Parameters.AddWithValue("@SALE_ID", $saleId)
    $reader = $verify.ExecuteReader()
    if ($reader.Read()) {
        [pscustomobject]@{
            SaleId = $reader.GetString(0)
            SaleNo = $reader.GetString(1)
            ClientId = $reader.GetString(2)
            Price = $reader.GetDouble(3)
            Discount = $reader.GetDouble(4)
            TotalVat = $reader.GetDouble(5)
            DetailCount = $reader.GetInt32(6)
            ClientAgentId = $clientAgentId
            SaleReplaced = $saleExists
        } | ConvertTo-Json -Depth 4
    }
    $reader.Close()
}
catch {
    try {
        $transaction.Rollback()
    }
    catch {
    }
    throw
}
finally {
    if ($connection.State -ne "Closed") {
        $connection.Close()
    }
}
