using System.Security.Claims;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Services
{
    /// <summary>
    /// Central rule for who may see which farm data:
    /// admins see everything, farmers see the farms they own, agronomists see the farms assigned to them.
    /// </summary>
    public class FarmAccessService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public FarmAccessService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public IQueryable<Farm> VisibleFarms(ClaimsPrincipal user)
        {
            var userId = _userManager.GetUserId(user);
            if (user.IsInRole(AppRoles.Admin)) return _db.Farms;
            if (user.IsInRole(AppRoles.Farmer)) return _db.Farms.Where(f => f.FarmerId == userId);
            if (user.IsInRole(AppRoles.Agronomist)) return _db.Farms.Where(f => f.AgronomistId == userId);
            return _db.Farms.Where(_ => false);
        }

        public IQueryable<Field> VisibleFields(ClaimsPrincipal user)
        {
            var farmIds = VisibleFarms(user).Select(f => f.Id);
            return _db.Fields.Where(f => farmIds.Contains(f.FarmId));
        }

        /// <summary>Only the owning farmer may change a farm's data (fields, devices, readings, images).</summary>
        public async Task<Farm?> GetOwnedFarmAsync(ClaimsPrincipal user, int farmId)
        {
            var userId = _userManager.GetUserId(user);
            return await _db.Farms.FirstOrDefaultAsync(f => f.Id == farmId && f.FarmerId == userId);
        }

        public async Task<Field?> GetOwnedFieldAsync(ClaimsPrincipal user, int fieldId)
        {
            var userId = _userManager.GetUserId(user);
            return await _db.Fields.Include(f => f.Farm)
                .FirstOrDefaultAsync(f => f.Id == fieldId && f.Farm!.FarmerId == userId);
        }

        /// <summary>Latest soil / humidity / drone / recommendation status for each field the user can see.</summary>
        public async Task<List<FieldSummaryViewModel>> GetFieldSummariesAsync(ClaimsPrincipal user)
        {
            var fields = await VisibleFields(user)
                .Include(f => f.Farm).ThenInclude(f => f!.Farmer)
                .OrderBy(f => f.Farm!.Name).ThenBy(f => f.Name)
                .Select(f => new { Field = f, DeviceCount = f.Devices.Count })
                .ToListAsync();
            var ids = fields.Select(f => f.Field.Id).ToList();

            var latestSoil = await _db.SensorReadings
                .Where(r => ids.Contains(r.FieldId) && r.SoilMoisture != null)
                .GroupBy(r => r.FieldId)
                .Select(g => g.OrderByDescending(r => r.RecordedAt).First())
                .ToDictionaryAsync(r => r.FieldId);
            var latestHumidity = await _db.SensorReadings
                .Where(r => ids.Contains(r.FieldId) && r.Humidity != null)
                .GroupBy(r => r.FieldId)
                .Select(g => g.OrderByDescending(r => r.RecordedAt).First())
                .ToDictionaryAsync(r => r.FieldId);
            var latestImage = await _db.DroneImages
                .Where(i => ids.Contains(i.FieldId))
                .GroupBy(i => i.FieldId)
                .Select(g => g.OrderByDescending(i => i.CapturedAt).First())
                .ToDictionaryAsync(i => i.FieldId);
            var recs = await _db.Recommendations
                .Where(r => ids.Contains(r.FieldId))
                .GroupBy(r => r.FieldId)
                .Select(g => new
                {
                    FieldId = g.Key,
                    Open = g.Count(r => !r.IsAcknowledged),
                    Latest = g.OrderByDescending(r => r.CreatedAt).Select(r => (CropHealthStatus?)r.CropHealth).First()
                })
                .ToDictionaryAsync(r => r.FieldId);

            return fields.Select(x => new FieldSummaryViewModel
            {
                FieldId = x.Field.Id,
                FieldName = x.Field.Name,
                FarmName = x.Field.Farm!.Name,
                FarmerName = x.Field.Farm.Farmer?.FullName,
                CropType = x.Field.CropType,
                DeviceCount = x.DeviceCount,
                LatestSoil = latestSoil.GetValueOrDefault(x.Field.Id),
                LatestHumidity = latestHumidity.GetValueOrDefault(x.Field.Id),
                LatestNdvi = latestImage.GetValueOrDefault(x.Field.Id)?.Ndvi,
                LatestCropHealth = recs.GetValueOrDefault(x.Field.Id)?.Latest,
                OpenRecommendations = recs.GetValueOrDefault(x.Field.Id)?.Open ?? 0
            }).ToList();
        }

        /// <summary>Only the agronomist assigned to the farm may advise on its fields.</summary>
        public async Task<Field?> GetAdvisedFieldAsync(ClaimsPrincipal user, int fieldId)
        {
            var userId = _userManager.GetUserId(user);
            return await _db.Fields.Include(f => f.Farm)
                .FirstOrDefaultAsync(f => f.Id == fieldId && f.Farm!.AgronomistId == userId);
        }
    }
}
