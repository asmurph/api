Here's the improved `README.md` file, incorporating the new content while maintaining the existing structure and information:

# EntraApi

A .NET 9 ASP.NET Core Web API for case management with:

- Microsoft Entra ID JWT bearer authentication
- Draft/open case workflow
- Case revision history (version snapshots)
- Secure file attachments
- SQL Server via Entity Framework Core
- Swagger/OpenAPI

## Tech Stack

- .NET 9 (`net9.0`)
- ASP.NET Core Web API
- Entity Framework Core 9 (SQL Server)
- Microsoft Identity Web (Entra ID)
- Swashbuckle (Swagger UI)

## Project Structure

- `Program.cs` – app startup, DI, auth, CORS, Swagger, middleware
- `Data/PricingDbContext.cs` – EF Core model and mappings
- `Domain/Entities.cs` – domain entities (`PricingCase`, `CaseAttachment`, `PricingCaseVersion`)
- `Services/CaseService.cs` – business logic for create/update/get/list/delete/versioning
- `Controllers/CasesController.cs` – case endpoints
- `Controllers/AttachmentsController.cs` – attachment upload/download/delete
- `Storage/FileStore.cs` – local and (optional) blob-backed file storage abstraction

## Prerequisites

- Visual Studio 2022
- .NET SDK 9
- SQL Server instance (or LocalDB)

## Configuration

Set these values in `appsettings.json`.

### Connection String

"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=EntraApiDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}

### Entra ID

"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "<tenant-id>",
  "ClientId": "<api-app-client-id>",
  "Audience": "api://<api-app-client-id>"
}

### CORS

"Cors": {
  "AllowedOrigins": [ "http://localhost:5173" ]
}

### Logging

"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}

## Database Migrations

Use __Package Manager Console__ with the API project selected as both Startup Project and Default Project.

Add-Migration AddCaseVersioning
Update-Database

If design-time context creation fails, ensure:

- `AddDbContext<PricingDbContext>` is registered in `Program.cs`
- `ConnectionStrings:DefaultConnection` exists
- (Optional but recommended) `IDesignTimeDbContextFactory<PricingDbContext>` exists

## Run Locally

1. Restore/build the solution.
2. Apply migrations.
3. Start the API.
4. Open `/swagger`.

Health endpoint:

- `GET /health` (anonymous)

## Authentication

All controller endpoints require bearer tokens with scope:

- `access_as_user`

Configured via `AddMicrosoftIdentityWebApi` using `AzureAd` settings.

## API Overview

### Cases (`/api/cases`)

- `POST /api/cases` – create draft/open case
- `PUT /api/cases/{id}` – update draft (requires `RowVersion`)
- `GET /api/cases/{id}` – get case
- `GET /api/cases?status=Draft|Open` – list cases
- `DELETE /api/cases/{id}` – delete draft
- `GET /api/cases/{id}/versions` – list saved revisions
- `GET /api/cases/{id}/versions/{revision}` – get specific saved revision

### Attachments (`/api/attachments`)

- `POST /api/attachments` – upload attachment
- `GET /api/attachments/{id}` – download attachment
- `DELETE /api/attachments/{id}` – delete attachment

Attachment rules include max file size, blocked executable/script extensions, and owner checks.

## Case Versioning Behavior

- `Revision` starts at `1` for a newly created case.
- On each successful update, the current state is snapshotted to `CaseVersions` before applying changes.
- `RowVersion` protects against stale updates (optimistic concurrency).

## Security Notes

- Ownership is enforced in queries (`OwnerId` / `UploadedBy`).
- Attachments are downloaded as `application/octet-stream` with `X-Content-Type-Options: nosniff`.
- Untrusted file names are sanitized before persistence.

## Swagger

Swagger UI is enabled in development and includes JWT Bearer security definition.

- Root `/` redirects to `/swagger`.

## Future Enhancements

- Move file storage to Azure Blob (`BlobFileStore`) using managed identity
- Add centralized structured logging and telemetry
- Add CI/CD validation for migrations and API contract changes

This revised `README.md` maintains the original structure while integrating the new content seamlessly, ensuring clarity and coherence throughout the document.