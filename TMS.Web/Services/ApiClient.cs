using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using TMS.Shared.DTOs;

namespace TMS.Web.Services
{
    public class ApiClient
    {
        private readonly HttpClient _http;

        public ApiClient(HttpClient http)
        {
            _http = http;
        }

        // ============================
        // AUTH ENDPOINTS
        // ============================

        public async Task<AuthResponseDto?> LoginAsync(string email, string password)
        {
            var dto = new AuthRequestDto
            {
                Email = email,
                Password = password
            };
            var resp = await _http.PostAsJsonAsync("api/token/login", dto);

            if (!resp.IsSuccessStatusCode)
                return null;

            return await resp.Content.ReadFromJsonAsync<AuthResponseDto>();
        }



        public async Task<AuthResponseDto?> RegisterAsync(string email, string password)
        {
            var dto = new AuthRequestDto
            {
                Email = email,
                Password = password
            };
            var resp = await _http.PostAsJsonAsync("api/token/register", dto);

            if (!resp.IsSuccessStatusCode)
                return null;

            return await resp.Content.ReadFromJsonAsync<AuthResponseDto>();
        }

        // ============================
        // USER ENDPOINTS
        // ============================

        public async Task<List<UserDto>> GetAllUsersAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<UserDto>>("api/users") ?? new List<UserDto>();
            }
            catch (HttpRequestException)
            {
                return new List<UserDto>();
            }
        }

        // ============================
        // TASK ENDPOINTS
        // ============================

        public async Task<List<TaskDto>> GetAllTasksAsync()
        {
            return await _http.GetFromJsonAsync<List<TaskDto>>("api/tasks") ?? new();
        }

        public async Task<List<TaskDto>> GetMyTasksAsync()
        {
            return await _http.GetFromJsonAsync<List<TaskDto>>("api/tasks/me") ?? new();
        }

        public async Task<TaskDto?> GetTaskByIdAsync(int id)
        {
            return await _http.GetFromJsonAsync<TaskDto?>($"api/tasks/{id}");
        }

        public async Task<bool> CreateTaskAsync(TaskDto task)
        {
            var response = await _http.PostAsJsonAsync("api/tasks", task);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateTaskAsync(TaskDto task)
        {
            var response = await _http.PutAsJsonAsync($"api/tasks/{task.Id}", task);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteTaskAsync(int id)
        {
            var response = await _http.DeleteAsync($"api/tasks/{id}");
            return response.IsSuccessStatusCode;
        }

        // ============================
        // ASSIGNMENT ENDPOINTS
        // ============================

        public async Task<bool> AssignToTaskAsync(int taskId)
        {
            var response = await _http.PostAsync($"api/tasks/{taskId}/assign", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UnassignFromTaskAsync(int taskId)
        {
            var response = await _http.PostAsync($"api/tasks/{taskId}/unassign", null);
            return response.IsSuccessStatusCode;
        }

        // Admin Assignment Endpoints
        public async Task<bool> AssignUserToTaskAsync(int taskId, string userId)
        {
            var response = await _http.PostAsync($"api/tasks/{taskId}/assign/{userId}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UnassignUserFromTaskAsync(int taskId, string userId)
        {
            var response = await _http.PostAsync($"api/tasks/{taskId}/unassign/{userId}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> CanCurrentUserEditTaskAsync(int taskId)
        {
            var response = await _http.GetAsync($"api/tasks/{taskId}/can-edit");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<bool>();
            }
            return false;
        }
    }
}