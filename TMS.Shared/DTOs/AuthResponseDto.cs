using System.ComponentModel.DataAnnotations;

namespace TMS.Shared.DTOs; 

public class AuthResponseDto
{
    public string TokenType { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string AccessToken { get; set; }
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; }
}