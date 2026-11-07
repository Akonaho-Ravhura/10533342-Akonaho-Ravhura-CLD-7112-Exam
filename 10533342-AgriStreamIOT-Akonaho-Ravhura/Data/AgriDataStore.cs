using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using Azure;
using Azure.Data.Tables;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>Reads the Azure Storage account connection string from configuration.</summary>
    public static class StorageAccountSettings
    {
        /// <summary>ConnectionStrings:StorageAccount (falls back to the older ConnectionStrings:BlobStorage name).</summary>
        public static string? GetConnectionString(IConfiguration config) =>
            config.GetConnectionString("StorageAccount") is { Length: > 0 } cs ? cs : config.GetConnectionString("BlobStorage");

        /// <summary>False when the value is missing or still contains a &lt;placeholder&gt; from appsettings.json.</summary>
        public static bool IsConfigured(string? connectionString) =>
            !string.IsNullOrWhiteSpace(connectionString) && !connectionString.Contains('<');
    }

    /// <summary>Names of the tables AgriStream creates in the Azure Storage account.</summary>
    public static class TableNames
    {
        public const string Users = "Users";
        public const string Roles = "Roles";
        public const string UserRoles = "UserRoles";
        public const string Counters = "Counters";
        public const string Farms = "Farms";
        public const string Fields = "Fields";
        public const string Devices = "Devices";
        public const string SensorReadings = "SensorReadings";
        public const string DroneImages = "DroneImages";
        public const string Recommendations = "Recommendations";

        public static readonly string[] All =
            { Users, Roles, UserRoles, Counters, Farms, Fields, Devices, SensorReadings, DroneImages, Recommendations };
    }

    /// <summary>
    /// All AgriStream data lives in Azure Table Storage. Each entity type has its own table; rows use
    /// the entity's numeric id (zero-padded) as the RowKey. Sensor readings are partitioned by field so a
    /// field's readings can be read in one query. Ids come from a "Counters" table (optimistic concurrency),
    /// so URLs keep using simple numbers like /Fields/Details/3.
    /// </summary>
    public class AgriDataStore
    {
        private sealed record Kind(string Table, Func<object, string> PartitionKey, Func<object, long> GetId, Action<object, long> SetId);

        private static readonly Dictionary<Type, Kind> Kinds = new()
        {
            [typeof(Farm)] = new(TableNames.Farms, _ => "farm", o => ((Farm)o).Id, (o, id) => ((Farm)o).Id = (int)id),
            [typeof(Field)] = new(TableNames.Fields, _ => "field", o => ((Field)o).Id, (o, id) => ((Field)o).Id = (int)id),
            [typeof(IoTDevice)] = new(TableNames.Devices, _ => "device", o => ((IoTDevice)o).Id, (o, id) => ((IoTDevice)o).Id = (int)id),
            [typeof(DroneImage)] = new(TableNames.DroneImages, _ => "image", o => ((DroneImage)o).Id, (o, id) => ((DroneImage)o).Id = (int)id),
            [typeof(Recommendation)] = new(TableNames.Recommendations, _ => "recommendation", o => ((Recommendation)o).Id, (o, id) => ((Recommendation)o).Id = (int)id),
            [typeof(SensorReading)] = new(TableNames.SensorReadings, o => Key(((SensorReading)o).FieldId), o => ((SensorReading)o).Id, (o, id) => ((SensorReading)o).Id = id),
        };

        private readonly TableServiceClient _service;

        public AgriDataStore(TableServiceClient service) => _service = service;

        /// <summary>Shown on the admin dashboard.</summary>
        public string Description => $"Azure Table Storage ({_service.AccountName})";

        public static string Key(long id) => id.ToString("D10");

        public TableClient Table(string name) => _service.GetTableClient(name);

        public async Task EnsureTablesAsync(CancellationToken ct = default)
        {
            foreach (var name in TableNames.All)
                await _service.CreateTableIfNotExistsAsync(name, ct);
        }

        // ---------- Generic entity access ----------

        /// <summary>Rows of <typeparamref name="T"/>, optionally filtered (build filters with TableClient.CreateQueryFilter).</summary>
        public async Task<List<T>> ListAsync<T>(string? filter = null, CancellationToken ct = default) where T : class, new()
        {
            var kind = Kinds[typeof(T)];
            var list = new List<T>();
            await foreach (var entity in Table(kind.Table).QueryAsync<TableEntity>(filter, cancellationToken: ct))
                list.Add(TableMapper.FromEntity<T>(entity));
            return list;
        }

        public async Task<T?> GetAsync<T>(long id, CancellationToken ct = default) where T : class, new()
        {
            var kind = Kinds[typeof(T)];
            if (typeof(T) == typeof(SensorReading)) throw new NotSupportedException("Look readings up by field.");
            var result = await Table(kind.Table).GetEntityIfExistsAsync<TableEntity>(kind.PartitionKey(new T()), Key(id), cancellationToken: ct);
            return result.HasValue ? TableMapper.FromEntity<T>(result.Value!) : null;
        }

        /// <summary>Saves a new row and gives it the next id.</summary>
        public async Task AddAsync<T>(T item, CancellationToken ct = default) where T : class
        {
            var kind = Kinds[typeof(T)];
            kind.SetId(item, await ReserveIdsAsync(kind.Table, 1, ct));
            await Table(kind.Table).AddEntityAsync(ToEntity(kind, item), ct);
        }

        /// <summary>Saves many new rows, in batches of up to 100 per partition.</summary>
        public async Task AddRangeAsync<T>(IList<T> items, CancellationToken ct = default) where T : class
        {
            if (items.Count == 0) return;
            var kind = Kinds[typeof(T)];
            var next = await ReserveIdsAsync(kind.Table, items.Count, ct);
            foreach (var item in items) kind.SetId(item, next++);

            var table = Table(kind.Table);
            foreach (var partition in items.GroupBy(kind.PartitionKey))
                foreach (var chunk in partition.Chunk(100))
                    await table.SubmitTransactionAsync(
                        chunk.Select(i => new TableTransactionAction(TableTransactionActionType.Add, ToEntity(kind, i))), ct);
        }

        public async Task UpdateAsync<T>(T item, CancellationToken ct = default) where T : class
        {
            var kind = Kinds[typeof(T)];
            await Table(kind.Table).UpsertEntityAsync(ToEntity(kind, item), TableUpdateMode.Replace, ct);
        }

        private static TableEntity ToEntity(Kind kind, object item) =>
            TableMapper.ToEntity(item, kind.PartitionKey(item), Key(kind.GetId(item)));

        /// <summary>Reserves <paramref name="count"/> consecutive ids for a table and returns the first.</summary>
        private async Task<long> ReserveIdsAsync(string name, int count, CancellationToken ct)
        {
            var counters = Table(TableNames.Counters);
            for (var attempt = 0; attempt < 25; attempt++)
            {
                try
                {
                    var existing = await counters.GetEntityIfExistsAsync<TableEntity>("counter", name, cancellationToken: ct);
                    if (!existing.HasValue)
                    {
                        await counters.AddEntityAsync(new TableEntity("counter", name) { ["Value"] = (long)count }, ct);
                        return 1;
                    }

                    var counter = existing.Value!;
                    var current = counter.GetInt64("Value") ?? 0;
                    counter["Value"] = current + count;
                    await counters.UpdateEntityAsync(counter, counter.ETag, TableUpdateMode.Replace, ct);
                    return current + 1;
                }
                catch (RequestFailedException ex) when (ex.Status is 409 or 412)
                {
                    // Someone else took the next id at the same time: read the counter again.
                }
            }
            throw new InvalidOperationException($"Could not reserve an id for {name}.");
        }

        // ---------- Sensor readings ----------

        public Task<List<SensorReading>> ReadingsForFieldAsync(int fieldId, DateTime? since = null, CancellationToken ct = default)
        {
            var pk = Key(fieldId);
            var filter = since == null
                ? TableClient.CreateQueryFilter($"PartitionKey eq {pk}")
                : TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RecordedAt ge {new DateTimeOffset(since.Value.ToUniversalTime())}");
            return ListAsync<SensorReading>(filter, ct);
        }

        // ---------- Users (rows written by TableUserStore) ----------

        public async Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct = default)
        {
            var list = new List<ApplicationUser>();
            await foreach (var entity in Table(TableNames.Users).QueryAsync<TableEntity>(cancellationToken: ct))
                list.Add(TableMapper.FromEntity<ApplicationUser>(entity));
            return list;
        }

        /// <summary>Fills in Farm.Farmer and Farm.Agronomist.</summary>
        public async Task AttachUsersAsync(IEnumerable<Farm> farms, CancellationToken ct = default)
        {
            var users = (await ListUsersAsync(ct)).ToDictionary(u => u.Id);
            foreach (var farm in farms)
            {
                farm.Farmer = users.GetValueOrDefault(farm.FarmerId);
                farm.Agronomist = farm.AgronomistId == null ? null : users.GetValueOrDefault(farm.AgronomistId);
            }
        }
    }
}
