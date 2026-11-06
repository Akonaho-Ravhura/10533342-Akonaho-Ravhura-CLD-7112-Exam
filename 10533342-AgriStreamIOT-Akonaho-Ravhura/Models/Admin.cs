using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;

/// <summary>An administrator responsible for managing the AgriStream application.</summary>
public class Admin : User
{
    public bool IsSuperAdmin { get; set; }

    [StringLength(100)]
    public string? Department { get; set; }
}
