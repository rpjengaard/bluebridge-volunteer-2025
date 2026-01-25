using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Security;

namespace Tests.Services;

public class MemberImpersonationServiceTests
{
    private readonly Mock<IMemberManager> _memberManagerMock;
    private readonly Mock<IMemberSignInManager> _memberSignInManagerMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<MemberImpersonationService>> _loggerMock;
    private readonly Mock<ISession> _sessionMock;
    private readonly Dictionary<string, byte[]> _sessionStorage;
    private readonly MemberImpersonationService _sut;

    public MemberImpersonationServiceTests()
    {
        _memberManagerMock = new Mock<IMemberManager>();
        _memberSignInManagerMock = new Mock<IMemberSignInManager>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<MemberImpersonationService>>();
        _sessionMock = new Mock<ISession>();
        _sessionStorage = new Dictionary<string, byte[]>();

        // Setup session mock
        _sessionMock.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<byte[]>()))
            .Callback<string, byte[]>((key, value) => _sessionStorage[key] = value);
        _sessionMock.Setup(s => s.TryGetValue(It.IsAny<string>(), out It.Ref<byte[]?>.IsAny))
            .Returns((string key, out byte[]? value) =>
            {
                var exists = _sessionStorage.TryGetValue(key, out var storedValue);
                value = storedValue;
                return exists;
            });
        _sessionMock.Setup(s => s.Remove(It.IsAny<string>()))
            .Callback<string>(key => _sessionStorage.Remove(key));

        var httpContextMock = new Mock<HttpContext>();
        httpContextMock.Setup(x => x.Session).Returns(_sessionMock.Object);
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContextMock.Object);

        _sut = new MemberImpersonationService(
            _memberManagerMock.Object,
            _memberSignInManagerMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region StartImpersonationAsync Tests

    [Fact]
    public async Task StartImpersonationAsync_WithNullEmail_ShouldReturnFalse()
    {
        // Act
        var result = await _sut.StartImpersonationAsync(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StartImpersonationAsync_WithEmptyEmail_ShouldReturnFalse()
    {
        // Act
        var result = await _sut.StartImpersonationAsync("");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StartImpersonationAsync_WithNonExistentMember_ShouldReturnFalse()
    {
        // Arrange
        _memberManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((MemberIdentityUser?)null);

        // Act
        var result = await _sut.StartImpersonationAsync("nonexistent@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StartImpersonationAsync_WithValidMember_ShouldReturnTrue()
    {
        // Arrange
        var userMock = new Mock<MemberIdentityUser>();
        userMock.Setup(x => x.Email).Returns("member@example.com");

        var currentUserMock = new Mock<MemberIdentityUser>();
        currentUserMock.Setup(x => x.Email).Returns("admin@example.com");

        _memberManagerMock.Setup(x => x.FindByEmailAsync("member@example.com"))
            .ReturnsAsync(userMock.Object);
        _memberManagerMock.Setup(x => x.GetCurrentMemberAsync())
            .ReturnsAsync(currentUserMock.Object);

        // Act
        var result = await _sut.StartImpersonationAsync("member@example.com");

        // Assert
        result.Should().BeTrue();
        _memberSignInManagerMock.Verify(x => x.SignInAsync(It.IsAny<MemberIdentityUser>(), It.IsAny<bool>()), Times.Once);
    }

    #endregion

    #region StopImpersonationAsync Tests

    [Fact]
    public async Task StopImpersonationAsync_WhenNotImpersonating_ShouldNotThrow()
    {
        // Act
        var act = async () => await _sut.StopImpersonationAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region IsImpersonating Tests

    [Fact]
    public void IsImpersonating_WhenNoHttpContext_ShouldReturnFalse()
    {
        // Arrange
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var sut = new MemberImpersonationService(
            _memberManagerMock.Object,
            _memberSignInManagerMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        // Act
        var result = sut.IsImpersonating();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsImpersonating_WhenNotSet_ShouldReturnFalse()
    {
        // Act
        var result = _sut.IsImpersonating();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetImpersonatedMemberEmail Tests

    [Fact]
    public void GetImpersonatedMemberEmail_WhenNoHttpContext_ShouldReturnNull()
    {
        // Arrange
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var sut = new MemberImpersonationService(
            _memberManagerMock.Object,
            _memberSignInManagerMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        // Act
        var result = sut.GetImpersonatedMemberEmail();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetImpersonatedMemberEmail_WhenNotSet_ShouldReturnNull()
    {
        // Act
        var result = _sut.GetImpersonatedMemberEmail();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetOriginalAdminEmail Tests

    [Fact]
    public void GetOriginalAdminEmail_WhenNoHttpContext_ShouldReturnNull()
    {
        // Arrange
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var sut = new MemberImpersonationService(
            _memberManagerMock.Object,
            _memberSignInManagerMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        // Act
        var result = sut.GetOriginalAdminEmail();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetOriginalAdminEmail_WhenNotSet_ShouldReturnNull()
    {
        // Act
        var result = _sut.GetOriginalAdminEmail();

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
