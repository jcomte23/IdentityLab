namespace IdentityLab.Mvc.Models;

public class ManageIndexViewModel
{
    public string? FullName { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public bool HasPassword { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public int RecoveryCodesLeft { get; set; }
    public IReadOnlyList<string> ExternalLogins { get; set; } = [];
}
