using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    public enum DeviceType
    {
        [Display(Name = "Soil sensor")] SoilSensor = 1,
        [Display(Name = "Humidity sensor")] HumiditySensor = 2,
        [Display(Name = "Drone")] Drone = 3
    }

    public enum CropHealthStatus
    {
        Healthy = 1,
        [Display(Name = "At risk")] AtRisk = 2,
        Critical = 3
    }

    public enum RecommendationPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }
}
