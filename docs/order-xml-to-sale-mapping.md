# Order XML to Sale Mapping

This is a provisional mapping based on the XML transaction structure visible in the Kumo/TCPOS templates and models.

Important:

- The actual file `order.xml` was not found in the repo on July 15, 2026.
- The mapping below is inferred from the `/transaction` XML structure referenced by the receipt/report XSL templates and the public `Transaction` / `TransactionArticle` models in `KumoAPI`.
- Once a real `order.xml` sample is available, this mapping should be tightened against the exact node names and example values.

## Observed source structure

From the XSL/template content in the repo, the input XML appears to look roughly like this:

```xml
<transaction>
  <customer>
    ...
  </customer>
  <trans-item type="article">
    <description>...</description>
    <quantity-description>...</quantity-description>
    <total-price>...</total-price>
    <notes>
      <line>...</line>
    </notes>
  </trans-item>
  <trans-item type="discount">
    ...
  </trans-item>
</transaction>
```

The Kumo API models also show a transaction shape with:

- transaction header data
- customer/card context
- article lines
- discounts
- payments
- VAT totals

## Proposed mapping to our `SaleRequest`

### Sale header

| Order XML / transaction data | SaleRequest field | Notes |
| --- | --- | --- |
| `transaction id` or external order id | `id` or `saleNo` | Prefer storing source order id in `saleNo` if it is human-readable. |
| `transaction date` | `saleDate` | Required for sale timing. |
| `bookkeeping date` | `deadline` or `contractDate` | Optional; depends on business rule. |
| transaction total without VAT | computed | Our API computes from details. |
| transaction VAT total | computed | Our API computes from details. |
| transaction total with VAT | computed | Our API computes from details. |
| shop / business unit | `businessGroupId` | Must be mapped from source shop/till/business context. |
| operator / cashier | `userId` | Map from transaction operator if available. |
| price level / currency context | `currencyId`, `currencyRate` | Usually `SYSTEM` and `1` in current examples unless source provides more. |
| transaction comment | `comment` | Can concatenate source comments if needed. |

### Client

| Order XML / transaction data | SaleRequest.client field | Notes |
| --- | --- | --- |
| `customer/id` or customer code | `client.id` or `client.code` | Best identifier if present. |
| `customer/name` / description | `client.name` | Main client/company name. |
| `customer/vat number` | `client.taxNo` | Good lookup key. |
| `customer/fiscal code` / company registration | `client.bulstat` | Useful fallback lookup key. |
| `customer/street` | `client.address` | Plain address line. |
| `customer/phone` | `client.phone` | |
| `customer/email` | `client.email` | |
| `customer/city` | `client.cityId` or free text | Needs lookup if `CITY_ID` is required. |
| `customer/country` | `client.countryId` | Needs lookup from source country code. |
| `customer notes` | `client.comment` | |

### Client agent

| Order XML / transaction data | SaleRequest.client.agent field | Notes |
| --- | --- | --- |
| customer contact name | `client.agent.name` | If company order has contact person. |
| customer contact mobile | `client.agent.cellPhone` | |
| customer contact phone | `client.agent.phone` | |
| customer contact email | `client.agent.email` | |
| customer contact address | `client.agent.address` | Optional. |

If the order XML has no distinct contact person, current API logic falls back to:

1. `client.agent.name`
2. `client.mol`
3. `client.name`

## Detail lines

Only `trans-item` nodes of type `article` should normally become `SALEDETAIL` rows.

| Order XML / transaction data | SaleRequest.details[] field | Notes |
| --- | --- | --- |
| article code / id | `articleId` or `code` | Prefer true article id if available; otherwise use code. |
| article description | `name` | |
| alternate description | `nameLatin` | Can mirror `name` if not present separately. |
| quantity / quantity-description | `quantity` | `quantity-description` may need parsing if quantity is formatted text. |
| unit price | `singlePrice` | Must come from source line amount before total. |
| line discount | `discount` | If discount is separate for the article. |
| line VAT amount or VAT percent | `vat` | Our current API expects VAT amount, not VAT percent. |
| delivery date | `deliveryDate` | Optional. |
| supplier reference | `supplierId` | Optional, likely not in order XML. |
| barcode | `codeEan` | Optional. |

## Items that should not directly become sale details

These source elements likely need separate handling instead of direct `SALEDETAIL` inserts:

- `trans-item type="discount"`
- `trans-item type="promotion"`
- `trans-item type="comment"`
- `trans-item type="menu-article"`
- payment nodes
- VAT summary nodes

Recommended handling:

- article-level discounts: fold into the matching detail `discount`
- transaction-level discounts: fold into sale `discount` or distribute across details
- comment nodes: append to sale `comment` or detail notes
- payment nodes: map later if you want to fill `PAYEDSUM`, `PAYSTATUS_ID`, or payment-specific tables

## Suggested first-pass transformation rules

1. Read the transaction header and map date, order number, business group, user, and comment.
2. Resolve customer data into the nested `client` section.
3. Iterate `trans-item` nodes and keep only `@type="article"` for `details`.
4. Parse quantity from a numeric field if present; use `quantity-description` only as fallback.
5. Use the article total and VAT data to derive:
   - `singlePrice`
   - `discount`
   - `vat`
6. Ignore payments/promotions/comments at first, unless they materially affect the totals.

## Example target payload

```json
{
  "saleNo": "ORDER-12345",
  "saleDate": "2026-07-15T12:30:00",
  "businessGroupId": "09",
  "userId": "00SYSTEM10",
  "currencyId": "SYSTEM",
  "comment": "Imported from order.xml",
  "client": {
    "code": "CUST-1001",
    "name": "Example Client Ltd",
    "taxNo": "BG999999999",
    "bulstat": "999999999",
    "address": "Main street 1",
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
      "nameLatin": "Sample item",
      "quantity": 2,
      "singlePrice": 12.5,
      "discount": 0,
      "vat": 5
    }
  ]
}
```

## What is still needed

To finish an exact mapper, I need the real `order.xml` sample or at least:

- header nodes
- customer node
- one article line
- one discount/promotion line if present

With that, we can create:

- a precise node-by-node mapping
- an XML deserializer model
- a transformation service from `order.xml` to `SaleRequest`
