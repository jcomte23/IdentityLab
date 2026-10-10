using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class RegisterViewModel
{
    [Display(Name = "Full name")]
    [StringLength(100)]
    public string? FullName { get; set; }

    [Required] [EmailAddress] public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}