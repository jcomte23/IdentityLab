using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.ViewModels.Manage;

public class DeleteAccountViewModel
{
    // True for accounts with a password; accounts created with Google confirm by typing their username.
    public bool RequirePassword { get; set; }

    // Shown in the page so the user knows what to type; always set by the controller, never read from the form.
    public string UserName { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Type your username to confirm")]
    public string? Confirmation { get; set; }
}
