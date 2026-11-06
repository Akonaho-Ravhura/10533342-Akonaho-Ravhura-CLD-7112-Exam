using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;

/// <summary>A user who owns or operates a farm.</summary>
public class Farmer : User
{
    [Required, StringLength(150)]
    public string FarmName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal? FarmSizeHectares { get; set; }

    [StringLength(100)]
    public string? PrimaryCrop { get; set; }

    [StringLength(150)]
    public string? FarmLocation { get; set; }
}
