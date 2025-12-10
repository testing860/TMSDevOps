using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMS.API.Data.Entities;        // <-- AppUser
using TMS.API.Services;
using TMS.Shared.DTOs;
using System.Linq;
using System.Threading.Tasks;

namespace TMS.API.Controllers
{
    [ApiController]
    [Route("api/token")]
    public class TokenController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly ITokenService _tokenService;

        public TokenController(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            ITokenService tokenService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenService = tokenService;
        }


        // LOGGING IN
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] AuthRequestDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest("Invalid login payload.");

            // Find user by email first
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return Unauthorized("Invalid email or password");

            var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
                return Unauthorized("Invalid email or password");

            var token = _tokenService.GenerateTokenAsync(user);

            return Ok(new AuthResponseDto
            {
                AccessToken = await token,
                DisplayName = user.DisplayName,
                Email = user.Email
            });
        }


        // REGISTERING

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            // 1) Model validation
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToArray();

                return BadRequest(new RegisterResultDto
                {
                    Success = false,
                    Errors = errors
                });
            }

            // 2) Password/ConfirmPassword Match Check
            if (dto.Password != dto.ConfirmPassword)
            {
                return BadRequest(new RegisterResultDto
                {
                    Success = false,
                    Errors = new[] { "Passwords do not match. Please verify that they match exactly." }
                });
            }

            // 3) Name Uniqueness
            var displayNameExists = await _userManager.Users
                .AnyAsync(u => u.DisplayName == dto.DisplayName);

            if (displayNameExists)
            {
                return BadRequest(new RegisterResultDto
                {
                    Success = false,
                    Errors = new[] { "Display name is already taken. Please choose another one." }
                });
            }

            // 4) Check for Email Uniqueness
            var emailExists = await _userManager.FindByEmailAsync(dto.Email);
            if (emailExists != null)
            {
                return BadRequest(new RegisterResultDto
                {
                    Success = false,
                    Errors = new[] { "Email is already registered. Try logging in instead." }
                });
            }

            // 5) Creating User
            var user = new AppUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                DisplayName = dto.DisplayName,
                EmailConfirmed = true // For simplicity, no Confirmation needed rn
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new RegisterResultDto
                {
                    Success = false,
                    Errors = result.Errors.Select(e => e.Description).ToArray()
                });
            }

            // 6) Assign "User" Role & Issue Token
            const string userRole = "User";
            if (!await _userManager.IsInRoleAsync(user, userRole))
            {
                await _userManager.AddToRoleAsync(user, userRole);
            }

            var token = _tokenService.GenerateTokenAsync(user);
            return Ok(new AuthResponseDto
            {
                DisplayName = user.DisplayName,
                Email = user.Email,
                AccessToken = await token
            });
        }
    }
}
