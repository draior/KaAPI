# KaApi

Simple ASP.NET Core API for inserting one `SALES` row with multiple `SALEDETAIL` rows into the `KM` SQL Server database, with automatic `CLIENT` and `CLIENTAGENT` creation when needed.

## What was analyzed

- `SALES.ID` is the primary key.
- `SALEDETAIL.ID` is the primary key.
- `SALEDETAIL.SALE_ID` is a foreign key to `SALES.ID`.
- `SALES.CLIENT_ID` points to `CLIENT.ID`.
- `SALES.CLIENTAGENT_ID` points to `CLIENTAGENT.ID`.
- Both tables have many nullable columns and a few defaults, so a focused insert is practical.
- There are no insert triggers on `SALES` or `SALEDETAIL`.

## Endpoint

- `POST /api/sales`
- `POST /api/sales/import-order-xml`
- Accepts `application/json`, `application/xml`, and `text/xml`
- Returns `201 Created` with inserted sale/detail IDs and calculated totals

## Request shape

The API accepts one sale header plus a `details` collection. If `id` values are omitted, the API generates 14-character fallback IDs.

Important:

- You can send a nested `client` object.
- If the client does not exist, the API creates it in `CLIENT`.
- The API also creates or resolves a matching `CLIENTAGENT` record and writes that ID back into the client and sale.
- Existing clients are searched by `client.id`, `clientId`, `client.code`, `client.taxNo`, then `client.bulstat`.
- `vat` on each detail is treated as the VAT amount, not the VAT percent.
- Totals are calculated from details.
- For best compatibility with your legacy system, prefer sending real IDs from your ERP if you already have them.

## Order XML import

The API also supports raw TCPOS-style transaction XML import:

- endpoint: `POST /api/sales/import-order-xml`
- content type: `application/xml` or `text/xml`
- article lines are read from `trans-item type="article"`
- customer data is read from the `customer` node
- defaults for `BusinessGroupId`, `UserId`, `SaleTypeId`, and related fields come from `OrderXmlImportDefaults` in appsettings

Current assumptions:

- root node is `<transaction>`
- only article lines are imported as `SALEDETAIL`
- detail VAT is imported as VAT amount
- transaction-level discounts/promotions/comments are not yet distributed across detail rows
- if a sale with the same `SALENO` and `CLIENT_ID` already exists, existing `SALEDETAIL` rows are deleted and recreated
- if a client already exists by code, tax number, or bulstat, the current API reuses it and does not update client data
- each detail must resolve to an existing `ARTICLE` by `CODE`

## Run

```powershell
$pwdPath=(Resolve-Path .).Path
$env:APPDATA=$pwdPath
$env:LOCALAPPDATA=$pwdPath
$env:USERPROFILE=$pwdPath
$env:DOTNET_CLI_HOME=$pwdPath
$env:NUGET_PACKAGES=(Resolve-Path .nuget).Path
dotnet run
```

By default the development profile listens on:

- `http://localhost:5001`
- `http://0.0.0.0:5001`

That means other devices on the same LAN can usually call:

- `http://192.168.100.132:5001/api/sales`
- `http://192.168.100.132:5001/api/sales/import-order-xml`

If that IP changes on your network, use the current IPv4 address of this PC instead.

## Configuration

- Development connection string is in `appsettings.Development.json`
- Base placeholder connection string is in `appsettings.json`

## Example JSON

```json
{
  "saleNo": "TEST-1001",
  "saleDate": "2026-07-15T10:00:00",
  "saleTypeId": 3,
  "businessGroupId": "09",
  "userId": "00SYSTEM10",
  "currencyId": "SYSTEM",
  "comment": "Inserted from KaApi",
  "client": {
    "code": "0120309999",
    "name": "Example Client Ltd",
    "taxNo": "BG999999999",
    "bulstat": "999999999",
    "address": "Sample address 1",
    "phone": "0888000000",
    "email": "client@example.com",
    "clientTypeId": "VK03",
    "agent": {
      "name": "Ivan Petrov",
      "cellPhone": "0888111222",
      "email": "ivan.petrov@example.com"
    }
  },
  "details": [
    {
      "code": "ITEM-1",
      "name": "Sample item",
      "quantity": 2,
      "singlePrice": 12.5,
      "discount": 0,
      "vat": 5
    }
  ]
}
```

## Example XML

```xml
<SaleRequest>
  <SaleNo>TEST-1002</SaleNo>
  <SaleDate>2026-07-15T10:15:00</SaleDate>
  <SaleTypeId>3</SaleTypeId>
  <BusinessGroupId>09</BusinessGroupId>
  <UserId>00SYSTEM10</UserId>
  <CurrencyId>SYSTEM</CurrencyId>
  <Comment>Inserted from XML</Comment>
  <Client>
    <Code>0120309998</Code>
    <Name>Example XML Client Ltd</Name>
    <TaxNo>BG999999998</TaxNo>
    <Bulstat>999999998</Bulstat>
    <Address>Sample address 2</Address>
    <Phone>0888999999</Phone>
    <Email>xmlclient@example.com</Email>
    <ClientTypeId>VK03</ClientTypeId>
    <Agent>
      <Name>Maria Petrova</Name>
      <CellPhone>0888444555</CellPhone>
      <Email>maria.petrova@example.com</Email>
    </Agent>
  </Client>
  <Details>
    <Detail>
      <Code>ITEM-XML-1</Code>
      <Name>XML item</Name>
      <Quantity>1</Quantity>
      <SinglePrice>20</SinglePrice>
      <Discount>0</Discount>
      <Vat>4</Vat>
    </Detail>
  </Details>
</SaleRequest>
```

## Example Order XML

```xml
<transaction>
  <id>ORDER-XML-1001</id>
  <transaction-date>2026-07-15T12:30:00</transaction-date>
  <transaction-number>12345</transaction-number>
  <till-id>09</till-id>
  <operator-id>00SYSTEM10</operator-id>
  <currency>SYSTEM</currency>
  <customer>
    <card-number>0120307777</card-number>
    <description>Template Client Ltd</description>
    <fiscal-vat-number>BG777777777</fiscal-vat-number>
    <fiscal-code>777777777</fiscal-code>
    <street>Sample street 7</street>
    <city>Dobrich</city>
    <country>BG</country>
  </customer>
  <trans-item type="article">
    <article-id>09121207000014</article-id>
    <code>ЕРБ03131</code>
    <description>МИНИ НОЖОВКА PROFI</description>
    <quantity>2</quantity>
    <price>12.5</price>
    <taxable-amount>25</taxable-amount>
    <vat-percent>20</vat-percent>
    <vat-amount>5</vat-amount>
    <total-price>30</total-price>
  </trans-item>
</transaction>
```
