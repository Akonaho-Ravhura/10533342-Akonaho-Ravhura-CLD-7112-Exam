using System.Security.Claims;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Services
{
    /// <summary>
    /// Central rule for who may see which farm data:
    /// admins see everything, farmers see the farms they own, agronomists see the farms assigned to them.
    /// </summary>
    public class FarmAccessService
    {
        private readonly AgriDataStore _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public FarmAccessService(AgriDataStore db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        /// <summary>Farms the user can see, with Farmer and Agronomist filled in.</summary>
        public async Task<List<Farm>> VisibleFarmsAsync(ClaimsPrincipal user)
        {
            var userId = _userManager.GetUserId(user);
            var farms = await _db.ListAsync<Farm>();
            var visible = user.IsInRole(AppRoles.Admin) ? farms
                : user.IsInRole(AppRoles.Farmer) ? farms.Where(f => f.FarmerId == userId).ToList()
                : user.IsInRole(AppRoles.Agronomist) ? farms.Where(f => f.AgronomistId == userId).ToList()
                : new List<Farm>();
            if (visible.Count > 0) await _db.AttachUsersAsync(visible);
            return visible;
        }

        /// <summary>Fields the user can see, each with its Farm (and the farm's farmer and agronomist) filled in.</summary>
        public async Task<List<Field>> VisibleFieldsAsync(ClaimsPrincipal user)
        {
            var farms = (await VisibleFarmsAsync(user)).ToDictionary(f => f.Id);
            if (farms.Count == 0) return new List<Field>();
            var fields = (await _db.ListAsync<Field>()).Where(f => farms.ContainsKey(f.FarmId)).ToList();
            foreach (var field in fields) field.Farm = farms[field.FarmId];
            return fields;
        }

        /// <summary>Only the owning farmer may change a farm's data (fields, devices, readings, images).</summary>
        public async Task<Farm?> GetOwnedFarmAsync(ClaimsPrincipal user, int farmId)
        {
            var farm = await _db.GetAsync<Farm>(farmId);
            return farm != null && farm.FarmerId == _userManager.GetUserId(user) ? farm : null;
        }

        public async Task<Field?> GetOwnedFieldAsync(ClaimsPrincipal user, int fieldId)
        {
            var field = await GetFieldWithFarmAsync(fieldId);
            return field?.Farm?.FarmerId == _userManager.GetUserId(user) ? field : null;
        }

        /// <summary>Only the agronomist assigned to the farm may advise on its fields.</summary>
        public async Task<Field?> GetAdvisedFieldAsync(ClaimsPrincipal user, int fieldId)
        {
            var field = await GetFieldWithFarmAsync(fieldId);
            return field?.Farm?.AgronomistId == _userManager.GetUserId(user) ? field : null;
        }

        private async Task<Field?> GetFieldWithFarmAsync(int fieldId)
        {
            var field = await _db.GetAsync<Field>(fieldId);
            if (field != null) field.Farm = await _db.GetAsync<Farm>(field.FarmId);
            return field;
        }

        /// <summary>Latest soil / humidity / drone / recommendation status for each field the user can see.</summary>
        public async Task<List<FieldSummaryViewModel>> GetFieldSummariesAsync(ClaimsPrincipal user)
        {
            var fields = (await VisibleFieldsAsync(user))
                .OrderBy(f => f.Farm!.Name).ThenBy(f => f.Name).ToList();
            if (fields.Count == 0) return new List<FieldSummaryViewModel>();
            var ids = fields.Select(f => f.Id).ToHashSet();

            var deviceCounts = (await _db.ListAsync<IoTDevice>())
                .Where(d => ids.Contains(d.FieldId))
                .GroupBy(d => d.FieldId).ToDictionary(g => g.Key, g => g.Count());

            var readings = new List<SensorReading>();
            foreach (var id in ids) readings.AddRange(await _db.ReadingsForFieldAsync(id));
            var latestSoil = readings.Where(r => r.SoilMoisture != null).GroupBy(r => r.FieldId)
                .ToDictionary(g => g.Key, g => g.MaxBy(r => r.RecordedAt)!);
            var latestHumidity = readings.Where(r => r.Humidity != null).GroupBy(r => r.FieldId)
                .ToDictionary(g => g.Key, g => g.MaxBy(r => r.RecordedAt)!);

            var latestImage = (await _db.ListAsync<DroneImage>())
                .Where(i => ids.Contains(i.FieldId)).GroupBy(i => i.FieldId)
                .ToDictionary(g => g.Key, g => g.MaxBy(i => i.CapturedAt)!);

            var recs = (await _db.ListAsync<Recommendation>())
                .Where(r => ids.Contains(r.FieldId)).GroupBy(r => r.FieldId)
                .ToDictionary(g => g.Key, g => new
                {
                    Open = g.Count(r => !r.IsAcknowledged),
                    Latest = (CropHealthStatus?)g.MaxBy(r => r.CreatedAt)!.CropHealth
                });

            return fields.Select(f => new FieldSummaryViewModel
            {
                FieldId = f.Id,
                FieldName = f.Name,
                FarmName = f.Farm!.Name,
                FarmerName = f.Farm.Farmer?.FullName,
                CropType = f.CropType,
                DeviceCount = deviceCounts.GetValueOrDefault(f.Id),
                LatestSoil = latestSoil.GetValueOrDefault(f.Id),
                LatestHumidity = latestHumidity.GetValueOrDefault(f.Id),
                LatestNdvi = latestImage.GetValueOrDefault(f.Id)?.Ndvi,
                LatestCropHealth = recs.GetValueOrDefault(f.Id)?.Latest,
                OpenRecommendations = recs.GetValueOrDefault(f.Id)?.Open ?? 0
            }).ToList();
        }
    }
}
