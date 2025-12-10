namespace TMS.Shared.DTOs;

public class RegisterResultDto
{
    public bool Success { get; set; }
    public IEnumerable<string> Errors { get; set; } = Enumerable.Empty<string>();
}