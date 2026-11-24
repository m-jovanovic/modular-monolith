# Equipment library use cases

The running .NET host exposes 50 named business and operator operations. The declared permissions below apply at the endpoint. Lending also checks ownership when a borrower reads or changes an individual hold or loan.

Use the [quickstart](../README.md#run-the-library) to try these operations. The host serves its OpenAPI document at `/openapi/v1.json`.

## Catalog

| Use case | HTTP | Endpoint authorization |
| --- | --- | --- |
| Archive Item | `POST /api/catalog/items/{itemId}/archive` | Catalog.Manage |
| Assign Category | `PUT /api/catalog/items/{itemId}/categories/{categoryId}` | Catalog.Manage |
| Create Category | `POST /api/catalog/categories` | Catalog.Manage |
| Create Item | `POST /api/catalog/items` | Catalog.Manage |
| Get Category | `GET /api/catalog/categories/{categoryId}` | Public |
| Get Item | `GET /api/catalog/items/{itemId}` | Public |
| Get Item Categories | `GET /api/catalog/items/{itemId}/categories` | Public |
| List Categories | `GET /api/catalog/categories` | Public |
| Remove Category | `DELETE /api/catalog/items/{itemId}/categories/{categoryId}` | Catalog.Manage |
| Restore Item | `POST /api/catalog/items/{itemId}/restore` | Catalog.Manage |
| Search Archived Items | `GET /api/catalog/items/archived` | Catalog.Manage |
| Search Items | `GET /api/catalog/items` | Public |
| Update Item | `PUT /api/catalog/items/{itemId}` | Catalog.Manage |

## Lending

| Use case | HTTP | Endpoint authorization |
| --- | --- | --- |
| Cancel Hold | `POST /api/lending/holds/{holdId}/cancel` | Borrower |
| Collect Hold | `POST /api/lending/holds/{holdId}/collect` | Pickup.Manage |
| Enroll Copy | `POST /api/lending/copies` | Lending.Enroll |
| Expire Due Holds | `POST /api/lending/holds/expire-due` | Pickup.Manage |
| Find Copies | `GET /api/lending/copies` | Public |
| Get Availability | `GET /api/lending/availability/{catalogItemId}` | Public |
| Get Copy | `GET /api/lending/copies/{copyId}` | Public |
| Get Copy History | `GET /api/lending/copies/{copyId}/history` | Pickup.Manage |
| Get Hold | `GET /api/lending/holds/{holdId}` | Requires an authenticated library identity |
| Get Loan | `GET /api/lending/loans/{loanId}` | Requires an authenticated library identity |
| Get Maintenance Order | `GET /api/lending/maintenance/{orderId}` | Lending.Enroll |
| List My Holds | `GET /api/lending/me/holds` | Borrower |
| List My Loans | `GET /api/lending/me/loans` | Borrower |
| List Open Maintenance Orders | `GET /api/lending/maintenance` | Lending.Enroll |
| List Overdue Loans | `GET /api/lending/loans/overdue` | Pickup.Manage |
| Open Maintenance Order | `POST /api/lending/copies/{copyId}/maintenance` | Lending.Enroll |
| Place Hold | `POST /api/lending/holds` | Borrower |
| Reinstate Copy | `POST /api/lending/copies/{copyId}/reinstate` | Lending.Enroll |
| Renew Loan | `POST /api/lending/loans/{loanId}/renew` | Borrower |
| Repair Copy | `POST /api/lending/maintenance/{orderId}/repair` | Lending.Enroll |
| Retire Copy | `POST /api/lending/copies/{copyId}/retire` | Lending.Enroll |
| Return Loan | `POST /api/lending/loans/{loanId}/return` | Pickup.Manage |
| Withdraw Damaged Copy | `POST /api/lending/maintenance/{orderId}/withdraw` | Lending.Enroll |

## Lending delivery

| Use case | HTTP | Endpoint authorization |
| --- | --- | --- |
| Get Delivery Status | `GET /api/lending/delivery/status` | Pickup.Operate |
| List Quarantined Deliveries | `GET /api/lending/delivery/quarantine` | Pickup.Operate |
| Redrive Delivery | `POST /api/lending/delivery/quarantine/{messageId}/redrive` | Pickup.Operate |

## PickupDesk

| Use case | HTTP | Endpoint authorization |
| --- | --- | --- |
| Claim Pickup | `POST /api/pickup-desk/queue/{holdId}/claim` | Pickup.Manage |
| Get Desk Workload | `GET /api/pickup-desk/workload` | Pickup.Manage |
| Get Pickup | `GET /api/pickup-desk/queue/{holdId}` | Pickup.Manage |
| List Closed Pickups | `GET /api/pickup-desk/history` | Pickup.Manage |
| List Pickups | `GET /api/pickup-desk/queue` | Pickup.Manage |
| List Quarantined Receipts | `GET /api/pickup-desk/inbox/quarantine` | Pickup.Manage, Pickup.Operate |
| Mark Pickup Ready | `POST /api/pickup-desk/queue/{holdId}/ready` | Pickup.Manage |
| Redrive Receipt | `POST /api/pickup-desk/inbox/{messageId}/redrive` | Pickup.Manage, Pickup.Operate |
| Release Pickup | `POST /api/pickup-desk/queue/{holdId}/release` | Pickup.Manage |
| Report Pickup Issue | `POST /api/pickup-desk/queue/{holdId}/issues` | Pickup.Manage |
| Resolve Pickup Issue | `POST /api/pickup-desk/queue/{holdId}/issues/resolve` | Pickup.Manage |
