# AgriStream IoT

ASP.NET Core 8 MVC app with three user types, storing all its data in an Azure Storage account: Azure Table Storage for users, roles and farm data, Blob Storage for drone images.

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
Data/              AgriDataStore (Table Storage access), TableUserStore / TableRoleStore (Identity on Table Storage),
                   DbSeeder (tables, roles, 3 users, sample data), DatabaseInitializer (runs the seeder after startup)
Services/          FarmAccessService: who can see / edit which farm
Controllers/       Account, Admin, Farmer, Agronomist, Fields (shared field data page), Home
Views/             One folder per controller
```

Data model: `Farm (Farmer, Agronomist?) 1─* Field 1─* IoTDevice 1─* SensorReading`, plus `Field 1─* DroneImage` and `Field 1─* Recommendation (Agronomist)`.

## Storage account

Everything is stored in one Azure Storage account, set in `appsettings.json`:

```json
"ConnectionStrings": {
  "StorageAccount": "DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net"
}
```

Copy the value from the Azure portal: **Storage account → Security + networking → Access keys → Connection string**.
On startup the app creates these tables (if missing) and the `drone-images` blob container:

| Table | Holds | PartitionKey / RowKey |
|---|---|---|
| `Users` | user accounts (email, password hash, name...) | `user` / user id |
| `Roles` | Admin, Farmer, Agronomist | `role` / role id |
| `UserRoles` | which user has which role | user id / role name |
| `Farms`, `Fields`, `Devices`, `DroneImages`, `Recommendations` | farm data | entity type / id |
| `SensorReadings` | soil and humidity readings | field id / reading id |
| `Counters` | the next id for each table | `counter` / table name |

`/health` shows whether the storage account is reachable (`Pending`, `Ready` or `Failed`). While it isn't `Ready`,
pages show a "temporarily unavailable" message instead of an error.

## Run locally

Requires the .NET 8 runtime. Point `ConnectionStrings:StorageAccount` at your storage account, or at the Azurite
emulator that ships with Visual Studio (`UseDevelopmentStorage=true`). In Development the app also seeds
(from `appsettings.Development.json`):

| Role | Email | Password |
|---|---|---|
| Admin | admin@agristream.local | Admin@123 |
| Farmer | farmer@agristream.local | Farmer@123 |
| Agronomist | agronomist@agristream.local | Agronomist@123 |

plus a sample farm with two fields, five devices, a week of sensor readings, drone images and a recommendation.

## Drone images (Azure Blob Storage)

Images are uploaded to a **private** blob container (`drone-images`, created automatically) in the same storage
account and streamed through `/Fields/Image/{id}`, which checks the viewer may see that field.

## Registration

Login and registration pages are shared by all user types. Farmers and agronomists register freely;
registering as an **Admin** requires `Registration:AdminCode` (set it as a secret / App Service setting;
if it's not set, admin sign-up is disabled).

## Deploy to Azure App Service

1. Set the storage account connection string, either in `appsettings.json` or (better, so the key isn't in git) as an
   App Service connection string named **`StorageAccount`** (type *Custom*).
2. Seed users are configured, not hard-coded. Add App Service settings for the accounts you want created, e.g.
   `SeedUsers__Admin__Email`, `SeedUsers__Admin__Password`, `SeedUsers__Admin__FullName` (same for `Farmer` and `Agronomist`).
   Leave `SeedUsers__SampleData` as `false` in production.
3. Deploy, then open `/health` and wait for `"database":"Ready"`.
