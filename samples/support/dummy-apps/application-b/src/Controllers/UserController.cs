// ApplicationB — UserController.cs
// Bug Scenario: SQL injection vulnerability in user search endpoint

namespace ApplicationB.Controllers;

using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly string _connectionString;

    public UserController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpGet("search")]
    public IActionResult SearchUsers([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query cannot be empty.");

        // BUG: SQL injection vulnerability — user input is concatenated directly
        // into the SQL query string without parameterization.
        // An attacker could pass: query = "'; DROP TABLE Users; --"
        var sql = "SELECT Id, DisplayName, Email FROM Users WHERE DisplayName LIKE '%" + query + "%'";

        var results = new List<UserSearchResult>();
        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new UserSearchResult
            {
                Id = reader.GetInt32(0),
                DisplayName = reader.GetString(1),
                Email = reader.GetString(2)
            });
        }

        return Ok(results);
    }

    [HttpGet("{userId}")]
    public IActionResult GetUserProfile(int userId)
    {
        // This endpoint uses parameterized queries correctly
        var sql = "SELECT Id, DisplayName, Email, AvatarUrl, Bio FROM Users WHERE Id = @UserId";

        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return NotFound();

        return Ok(new UserProfile
        {
            Id = reader.GetInt32(0),
            DisplayName = reader.GetString(1),
            Email = reader.GetString(2),
            AvatarUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
            Bio = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }
}
