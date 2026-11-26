// ApplicationB — UserServiceTests.cs
// Bug Scenario: Flaky test due to hardcoded date comparison

namespace ApplicationB.Tests;

using Xunit;

public class UserServiceTests
{
    [Fact]
    public void CreateUser_WithValidData_SetsCreatedDate()
    {
        // Arrange
        var service = new UserService();

        // Act
        var user = service.CreateUser("Jane Smith", "jane@example.com");

        // BUG: Flaky test — compares against a hardcoded date that was valid
        // only on the day the test was written. This test passes on 2024-01-15
        // but fails on every other day.
        // Should use a tolerance window or mock the clock instead.
        Assert.Equal(new DateTime(2024, 1, 15), user.CreatedAt.Date);
    }

    [Fact]
    public void CreateUser_WithValidData_AssignsUniqueId()
    {
        // Arrange
        var service = new UserService();

        // Act
        var user1 = service.CreateUser("User One", "one@example.com");
        var user2 = service.CreateUser("User Two", "two@example.com");

        // Assert
        Assert.NotEqual(user1.Id, user2.Id);
    }

    [Fact]
    public void GetUser_WithExistingId_ReturnsUser()
    {
        // Arrange
        var service = new UserService();
        var created = service.CreateUser("Test User", "test@example.com");

        // Act
        var retrieved = service.GetUser(created.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Test User", retrieved!.DisplayName);
        Assert.Equal("test@example.com", retrieved.Email);
    }

    [Fact]
    public void GetUser_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var service = new UserService();

        // Act
        var result = service.GetUser(999);

        // Assert
        Assert.Null(result);
    }
}
