using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels
{
    /// <summary>A field with its latest IoT status, used on the farmer and agronomist dashboards.</summary>
    public class FieldSummaryViewModel
    {
        public int FieldId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
        public string? FarmerName { get; set; }
        public string? CropType { get; set; }
        public int DeviceCount { get; set; }
        public SensorReading? LatestSoil { get; set; }
        public SensorReading? LatestHumidity { get; set; }
        public decimal? LatestNdvi { get; set; }
        public CropHealthStatus? LatestCropHealth { get; set; }
        public int OpenRecommendations { get; set; }

        /// <summary>Simple rule-of-thumb alerts so agronomists can see which fields need attention.</summary>
        public List<string> Alerts
        {
            get
            {
                var alerts = new List<string>();
                if (LatestSoil?.Nitrogen is < 20) alerts.Add("Low nitrogen");
                if (LatestSoil?.SoilMoisture is < 20) alerts.Add("Dry soil");
                if (LatestSoil?.SoilPh is < 5.5m or > 7.5m) alerts.Add("pH out of range");
                if (LatestSoil?.SoilDensity is > 1.6m) alerts.Add("Compacted soil");
                if (LatestHumidity?.Humidity is > 85) alerts.Add("High humidity (disease risk)");
                if (LatestNdvi is < 0.4m) alerts.Add("Low NDVI");
                return alerts;
            }
        }
    }

    /// <summary>Everything on the field details page.</summary>
    public class FieldDetailsViewModel
    {
        public Field Field { get; set; } = null!;
        public List<IoTDevice> Devices { get; set; } = new();
        public List<SensorReading> SoilReadings { get; set; } = new();
        public List<SensorReading> HumidityReadings { get; set; } = new();
        public List<DroneImage> DroneImages { get; set; } = new();
        public List<Recommendation> Recommendations { get; set; } = new();

        public bool CanEdit { get; set; }       // owning farmer
        public bool CanAdvise { get; set; }     // assigned agronomist

        public decimal? Avg(Func<SensorReading, decimal?> selector, IEnumerable<SensorReading> readings)
        {
            var values = readings.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 2);
        }
    }

    public class FarmListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Location { get; set; }
        public decimal? SizeHectares { get; set; }
        public string FarmerName { get; set; } = string.Empty;
        public string? AgronomistId { get; set; }
        public string? AgronomistName { get; set; }
        public int FieldCount { get; set; }
    }

    public class CreateFieldViewModel
    {
        public int FarmId { get; set; }
        public string? FarmName { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Crop"), StringLength(100)]
        public string? CropType { get; set; }

        [Display(Name = "Area (ha)"), Range(0, 100_000)]
        public decimal? AreaHectares { get; set; }

        [Display(Name = "Planted on"), DataType(DataType.Date)]
        public DateTime? PlantedOn { get; set; }
    }
}
