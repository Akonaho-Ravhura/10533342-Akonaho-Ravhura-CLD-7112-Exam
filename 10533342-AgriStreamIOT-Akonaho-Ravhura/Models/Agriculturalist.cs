using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;

/// <summary>An agricultural specialist who provides expertise to farmers.</summary>
public class Agriculturalist : User
{
    [Required, StringLength(150)]
    public string Specialization { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Organization { get; set; }

    [StringLength(100)]
    public string? Qualification { get; set; }
}
