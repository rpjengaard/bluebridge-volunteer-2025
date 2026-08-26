using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Tests.Services;

public class MemberAuthServiceTests
{
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<IMemberSignInManager> _memberSignInManagerMock;
    private readonly Mock<IMemberService> _memberServiceMock;
    private readonly Mock<IContentService> _contentServiceMock;
    private readonly Mock<ILogger<MemberAuthService>> _loggerMock;
    private readonly MemberAuthService _sut;

    public MemberAuthServiceTests()
    {
        _memberManagerMock = new Mock<IMemberManager>();
        _memberSignInManagerMock = new Mock<IMemberSignInManager>();
        _memberServiceMock = new Mock<IMemberService>();
        _contentServiceMock = new Mock<IContentService>();
        _loggerMock = new Mock<ILogger<MemberAuthService>>();

        _sut = new MemberAuthService(
            _memberManagerMock.Object,
            _memberSignInManagerMock.Object,
            _memberServiceMock.Object,
            _contentServiceMock.Object,
            _loggerMock.Object);
    }

    #region LoginAsync Tests

    [Fact]
    public async Task LoginAsync_WithNullEmail_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.LoginAsync(null!, "password", false);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoginAsync_WithEmptyEmail_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.LoginAsync("", "password", false);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoginAsync_WithNullPassword_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.LoginAsync("test@example.com", null!, false);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ShouldReturnFailure()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.LoginAsync("nonexistent@example.com", "password", false);

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task LoginAsync_WithLockedOutUser_ShouldReturnLockedOut()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(userMock.Object);
        _memberSignInManagerMock.Setup(x => x.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(SignInResult.LockedOut);

        // Act
        var result = await _sut.LoginAsync("test@example.com", "password", false);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.IsLockedOut.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WithNotAllowedUser_ShouldReturnNotAllowed()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(userMock.Object);
        _memberSignInManagerMock.Setup(x => x.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(SignInResult.NotAllowed);

        // Act
        var result = await _sut.LoginAsync("test@example.com", "password", false);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.IsNotAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldSucceed()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(userMock.Object);
        _memberSignInManagerMock.Setup(x => x.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(SignInResult.Success);

        // Act
        var result = await _sut.LoginAsync("test@example.com", "password", false);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.IsLockedOut.Should().BeFalse();
        result.IsNotAllowed.Should().BeFalse();
    }

    #endregion

    #region LogoutAsync Tests

    [Fact]
    public async Task LogoutAsync_ShouldCallSignOut()
    {
        // Act
        await _sut.LogoutAsync();

        // Assert
        _memberSignInManagerMock.Verify(x => x.SignOutAsync(), Times.Once);
    }

    #endregion

    #region SignupAsync Tests

    [Fact]
    public async Task SignupAsync_WithNullEmail_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync(null!, "Password123!", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SignupAsync_WithEmptyEmail_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("", "Password123!", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SignupAsync_WithShortPassword_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("test@example.com", "short", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SignupAsync_WithPasswordMissingDigit_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("test@example.com", "PasswordABC!", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("tal"));
    }

    [Fact]
    public async Task SignupAsync_WithPasswordMissingUppercase_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("test@example.com", "password123!", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("stort bogstav"));
    }

    [Fact]
    public async Task SignupAsync_WithPasswordMissingLowercase_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("test@example.com", "PASSWORD123!", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("lille bogstav"));
    }

    [Fact]
    public async Task SignupAsync_WithPasswordMissingSpecialChar_ShouldReturnFailure()
    {
        // Act
        var result = await _sut.SignupAsync("test@example.com", "Password123", "Test", "User", null, null, null, null);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("specialtegn"));
    }

    #endregion

    #region MemberExistsAsync Tests

    [Fact]
    public async Task MemberExistsAsync_WithNullEmail_ShouldReturnFalse()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.MemberExistsAsync(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MemberExistsAsync_WithNonExistentEmail_ShouldReturnFalse()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync("nonexistent@example.com"))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.MemberExistsAsync("nonexistent@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MemberExistsAsync_WithExistingEmail_ShouldReturnTrue()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(userMock.Object);

        // Act
        var result = await _sut.MemberExistsAsync("existing@example.com");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region GeneratePasswordResetTokenAsync Tests

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithNonExistentEmail_ShouldReturnNull()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.GeneratePasswordResetTokenAsync("nonexistent@example.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithExistingEmail_ShouldReturnToken()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(userMock.Object);
        _memberManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(It.IsAny<MemberIdentityUser>()))
            .ReturnsAsync("reset-token");

        // Act
        var result = await _sut.GeneratePasswordResetTokenAsync("existing@example.com");

        // Assert
        result.Should().Be("reset-token");
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_WithNonExistentEmail_ShouldReturnFailure()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.ResetPasswordAsync("nonexistent@example.com", "token", "NewPassword123!");

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResetPasswordAsync_WithShortPassword_ShouldReturnFailure()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        _memberManagerMock.Setup(x => x.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(userMock.Object);

        // Act
        var result = await _sut.ResetPasswordAsync("existing@example.com", "token", "short");

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    #endregion

    #region LoginResult Model Tests

    [Fact]
    public void LoginResult_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var successResult = new LoginResult(true, false, false, null);
        var lockedResult = new LoginResult(false, true, false, "Locked out");
        var notAllowedResult = new LoginResult(false, false, true, "Not allowed");

        // Assert
        successResult.Succeeded.Should().BeTrue();
        successResult.IsLockedOut.Should().BeFalse();
        successResult.IsNotAllowed.Should().BeFalse();

        lockedResult.Succeeded.Should().BeFalse();
        lockedResult.IsLockedOut.Should().BeTrue();
        lockedResult.ErrorMessage.Should().Be("Locked out");

        notAllowedResult.IsNotAllowed.Should().BeTrue();
    }

    #endregion

    #region SignupResult Model Tests

    [Fact]
    public void SignupResult_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var successResult = new SignupResult(true, Array.Empty<string>());
        var failureResult = new SignupResult(false, new[] { "Error 1", "Error 2" });

        // Assert
        successResult.Succeeded.Should().BeTrue();
        successResult.Errors.Should().BeEmpty();

        failureResult.Succeeded.Should().BeFalse();
        failureResult.Errors.Should().HaveCount(2);
    }

    #endregion

    #region PasswordResetResult Model Tests

    [Fact]
    public void PasswordResetResult_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var successResult = new PasswordResetResult(true, Array.Empty<string>());
        var failureResult = new PasswordResetResult(false, new[] { "Error" });

        // Assert
        successResult.Succeeded.Should().BeTrue();
        failureResult.Succeeded.Should().BeFalse();
        failureResult.Errors.Should().HaveCount(1);
    }

    #endregion
}
