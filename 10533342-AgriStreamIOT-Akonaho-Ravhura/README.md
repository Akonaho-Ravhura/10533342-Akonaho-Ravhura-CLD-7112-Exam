# AgriStream IoT

ASP.NET Core 8 MVC app with three user types, backed by Azure SQL Database (SQL Server LocalDB in development).

| User type | What they do |
|---|---|
| **Farmer** | Owns farms and fields, registers IoT devices (soil sensors, humidity sensors, drones), records soil density / moisture / pH / N-P-K, humidity / temperature, and drone images. Marks advice as actioned. |
| **Agronomist** | Sees the data of the farms an admin assigned to them (with automatic alerts such as low nitrogen or dry soil), and writes crop health and yield recommendations per field. |
| **Admin** | Manages users and their types, and assigns agronomists to farms. Can view all field data. |

Public registration creates a **Farmer**. Agronomists and admins are created by an admin.

## Structure

```
Models/            Entities: ApplicationUser, Farm, Field, IoTDevice, SensorReading, DroneImage, Recommendation
Models/ViewModels/ View models for forms and dashboards
Data/              ApplicationDbContext (EF Core + Identity), DbSeeder (roles, 3 users, sample data)
Services/          FarmAccessService: who can see / edit which farm
Controllers/       Account, Admin, Farmer, Agronomist, Fields (shared field data page), Home
Views/             One folder per controller
Migrations/        EF Core migrations for SQL Server / Azure SQL
```

Data model: `Farm (Farmer, Agronomist?) 1─* Field 1─* IoTDevice 1─* SensorReading`, plus `Field 1─* DroneImage` and `Field 1─* Recommendation (Agronomist)`.

## Run locally

Requires the .NET 8 runtime and SQL Server LocalDB (installed with Visual Studio).

```bash
dotnet run --launch-profile http
```

On startup the app applies migrations and seeds (from `appsettings.Development.json`):

| Role | Email | Password |
|---|---|---|
| Admin | admin@agristream.local | Admin@123 |
| Farmer | farmer@agristream.local | Farmer@123 |
| Agronomist | agronomist@agristream.local | Agronomist@123 |

plus a sample farm with two fields, five devices, a week of sensor readings, drone images and a recommendation.

## Drone images (Azure Blob Storage)

Images are uploaded to a **private** blob container (`drone-images`, created automatically) and streamed through
`/Fields/Image/{id}`, which checks the viewer may see that field. Configure `ConnectionStrings:BlobStorage`
(user-secrets locally, App Service connection string in Azure). Without it, images are stored in `App_Data/drone-images`.
For local blob testing, run Azurite (`npx -p azurite azurite-blob`) and use `UseDevelopmentStorage=true`.

## Registration

Login and registration pages are shared by all user types. Farmers and agronomists register freely;
registering as an **Admin** requires `Registration:AdminCode` (set it as a secret / App Service setting;
if it's not set, admin sign-up is disabled).

## Deploy with Azure SQL Database

1. Create an Azure SQL server + database (e.g. `AgriStreamIoT`) and allow your App Service through the server firewall
   ("Allow Azure services and resources to access this server").
2. In the App Service, add a connection string named **`DefaultConnection`** (type *SQLAzure*):
   `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=AgriStreamIoT;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;`
   Do not commit the real password to `appsettings.json`.
3. Seed users are configured, not hard-coded. Add App Service settings for the accounts you want created, e.g.
   `SeedUsers__Admin__Email`, `SeedUsers__Admin__Password`, `SeedUsers__Admin__FullName` (same for `Farmer` and `Agronomist`).
   Leave `SeedUsers__SampleData` as `false` in production.
4. Deploy. Migrations run automatically on startup. To apply them manually instead:
   `dotnet ef database update --connection "<azure connection string>"`

The DbContext uses `EnableRetryOnFailure()` so transient Azure SQL faults are retried.
