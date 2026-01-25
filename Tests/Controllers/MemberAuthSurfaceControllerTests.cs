using Code.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Web.Controllers;
using Web.ViewModels;

namespace Tests.Controllers;

public class MemberAuthSurfaceControllerTests
{
    private readonly Mock<IMemberAuthService> _authServiceMock;
    private readonly Mock<IMemberEmailService> _emailServiceMock;
    private readonly Mock<IPublishedContentQuery> _publishedContentQueryMock;
    private readonly Mock<IUmbracoContextAccessor> _umbracoContextAccessorMock;
    private readonly Mock<IUmbracoDatabaseFactory> _databaseFactoryMock;
    private readonly Mock<ServiceContext> _serviceContextMock;
    private readonly Mock<AppCaches> _appCachesMock;
    private readonly Mock<IProfilingLogger> _profilingLoggerMock;
    private readonly Mock<IPublishedUrlProvider> _publishedUrlProviderMock;

    public MemberAuthSurfaceControllerTests()
    {
        _authServiceMock = new Mock<IMemberAuthService>();
        _emailServiceMock = new Mock<IMemberEmailService>();
        _publishedContentQueryMock = new Mock<IPublishedContentQuery>();
        _umbracoContextAccessorMock = new Mock<IUmbracoContextAccessor>();
        _databaseFactoryMock = new Mock<IUmbracoDatabaseFactory>();
        _serviceContextMock = new Mock<ServiceContext>();
        _appCachesMock = new Mock<AppCaches>();
        _profilingLoggerMock = new Mock<IProfilingLogger>();
        _publishedUrlProviderMock = new Mock<IPublishedUrlProvider>();
    }

    private MemberAuthSurfaceController CreateController()
    {
        var controller = new MemberAuthSurfaceController(
            _umbracoContextAccessorMock.Object,
            _databaseFactoryMock.Object,
            _serviceContextMock.Object,
            _appCachesMock.Object,
            _profilingLoggerMock.Object,
            _publishedUrlProviderMock.Object,
            _authServiceMock.Object,
            _emailServiceMock.Object,
            _publishedContentQueryMock.Object);

        // Setup controller context
        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.TempData = tempData;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Setup Url helper
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.IsLocalUrl(It.IsAny<string>())).Returns(true);
        controller.Url = urlHelper.Object;

        return controller;
    }

    #region HandleLogin Tests

    [Fact]
    public async Task HandleLogin_WithInvalidModelState_ShouldRedirectToLogin()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new LoginViewModel { Email = "", Password = "" };

        // Act
        var result = await controller.HandleLogin(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Contain("/login");
        controller.TempData["LoginError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleLogin_WithSuccessfulLogin_ShouldRedirectToDashboard()
    {
        // Arrange
        var controller = CreateController();
        var model = new LoginViewModel { Email = "test@example.com", Password = "password" };
        _authServiceMock.Setup(x => x.LoginAsync(model.Email, model.Password, model.RememberMe))
            .ReturnsAsync(new LoginResult(true, false, false));
        _publishedContentQueryMock.Setup(x => x.ContentAtRoot())
            .Returns(Array.Empty<IPublishedContent>());

        // Act
        var result = await controller.HandleLogin(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
    }

    [Fact]
    public async Task HandleLogin_WithReturnUrl_ShouldRedirectToReturnUrl()
    {
        // Arrange
        var controller = CreateController();
        var model = new LoginViewModel
        {
            Email = "test@example.com",
            Password = "password",
            ReturnUrl = "/dashboard"
        };
        _authServiceMock.Setup(x => x.LoginAsync(model.Email, model.Password, model.RememberMe))
            .ReturnsAsync(new LoginResult(true, false, false));

        // Act
        var result = await controller.HandleLogin(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Be("/dashboard");
    }

    [Fact]
    public async Task HandleLogin_WithFailedLogin_ShouldRedirectToLoginWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new LoginViewModel { Email = "test@example.com", Password = "wrongpassword" };
        _authServiceMock.Setup(x => x.LoginAsync(model.Email, model.Password, model.RememberMe))
            .ReturnsAsync(new LoginResult(false, false, false, "Invalid credentials"));

        // Act
        var result = await controller.HandleLogin(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["LoginError"].Should().Be("Invalid credentials");
    }

    #endregion

    #region HandleLogout Tests

    [Fact]
    public async Task HandleLogout_ShouldCallLogoutAndRedirect()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.HandleLogout();

        // Assert
        _authServiceMock.Verify(x => x.LogoutAsync(), Times.Once);
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Be("/");
    }

    #endregion

    #region HandleSignup Tests

    [Fact]
    public async Task HandleSignup_WithInvalidModelState_ShouldRedirectToSignup()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new SignupViewModel();

        // Act
        var result = await controller.HandleSignup(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Be("/signup");
        controller.TempData["SignupError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleSignup_WithExistingEmail_ShouldRedirectToSignupWithError()
    {
        // Arrange
        var controller = CreateController();
        var model = new SignupViewModel
        {
            Email = "existing@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            FirstName = "Test",
            LastName = "User"
        };
        _authServiceMock.Setup(x => x.MemberExistsAsync(model.Email))
            .ReturnsAsync(true);

        // Act
        var result = await controller.HandleSignup(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["SignupError"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleSignup_WithSuccessfulSignup_ShouldRedirectToDashboard()
    {
        // Arrange
        var controller = CreateController();
        var model = new SignupViewModel
        {
            Email = "new@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            FirstName = "Test",
            LastName = "User"
        };
        _authServiceMock.Setup(x => x.MemberExistsAsync(model.Email))
            .ReturnsAsync(false);
        _authServiceMock.Setup(x => x.SignupAsync(
            model.Email, model.Password, model.FirstName, model.LastName,
            model.Phone, model.Birthdate, model.Zipcode, model.CrewWishes))
            .ReturnsAsync(new SignupResult(true, Array.Empty<string>()));
        _publishedContentQueryMock.Setup(x => x.ContentAtRoot())
            .Returns(Array.Empty<IPublishedContent>());

        // Act
        var result = await controller.HandleSignup(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
    }

    [Fact]
    public async Task HandleSignup_WithFailedSignup_ShouldRedirectToSignupWithErrors()
    {
        // Arrange
        var controller = CreateController();
        var model = new SignupViewModel
        {
            Email = "new@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            FirstName = "Test",
            LastName = "User"
        };
        _authServiceMock.Setup(x => x.MemberExistsAsync(model.Email))
            .ReturnsAsync(false);
        _authServiceMock.Setup(x => x.SignupAsync(
            model.Email, model.Password, model.FirstName, model.LastName,
            model.Phone, model.Birthdate, model.Zipcode, model.CrewWishes))
            .ReturnsAsync(new SignupResult(false, new[] { "Error 1", "Error 2" }));

        // Act
        var result = await controller.HandleSignup(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["SignupError"].Should().NotBeNull();
    }

    #endregion

    #region HandleForgotPassword Tests

    [Fact]
    public async Task HandleForgotPassword_WithInvalidModelState_ShouldRedirectToLogin()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("Email", "Required");
        var model = new ForgotPasswordViewModel();

        // Act
        var result = await controller.HandleForgotPassword(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirectResult = result as RedirectResult;
        redirectResult!.Url.Should().Be("/login?forgot=true");
    }

    [Fact]
    public async Task HandleForgotPassword_WithValidEmail_ShouldAlwaysShowSuccess()
    {
        // Arrange
        var controller = CreateController();
        var model = new ForgotPasswordViewModel { Email = "test@example.com" };
        _authServiceMock.Setup(x => x.GeneratePasswordResetTokenAsync(model.Email))
            .ReturnsAsync("reset-token");

        // Setup Url helper for action
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.Action(It.IsAny<UrlActionContext>()))
            .Returns("https://example.com/reset?token=abc");
        controller.Url = urlHelper.Object;

        // Act
        var result = await controller.HandleForgotPassword(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["ForgotPasswordSuccess"].Should().Be(true);
    }

    [Fact]
    public async Task HandleForgotPassword_WithNonExistentEmail_ShouldStillShowSuccess()
    {
        // Arrange - prevents email enumeration
        var controller = CreateController();
        var model = new ForgotPasswordViewModel { Email = "nonexistent@example.com" };
        _authServiceMock.Setup(x => x.GeneratePasswordResetTokenAsync(model.Email))
            .ReturnsAsync((string?)null);

        // Act
        var result = await controller.HandleForgotPassword(model);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        controller.TempData["ForgotPasswordSuccess"].Should().Be(true);
    }

    #endregion

    #region ResetPassword Tests

    [Fact]
    public void ResetPassword_ShouldReturnViewWithModel()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = controller.ResetPassword("test@example.com", "token123");

        // Assert
        result.Should().BeOfType<ViewResult>();
        var viewResult = result as ViewResult;
        viewResult!.Model.Should().BeOfType<ResetPasswordViewModel>();
        var model = viewResult.Model as ResetPasswordViewModel;
        model!.Email.Should().Be("test@example.com");
        model.Token.Should().Be("token123");
    }

    #endregion

    #region HandleResetPassword Tests

    [Fact]
    public async Task HandleResetPassword_WithInvalidModelState_ShouldReturnView()
    {
        // Arrange
        var controller = CreateController();
        controller.ModelState.AddModelError("NewPassword", "Required");
        var model = new ResetPasswordViewModel();

        // Act
        var result = await controller.HandleResetPassword(model);

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task HandleResetPassword_WithSuccessfulReset_ShouldRedirectToConfirmation()
    {
        // Arrange
        var controller = CreateController();
        var model = new ResetPasswordViewModel
        {
            Email = "test@example.com",
            Token = "token123",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };
        _authServiceMock.Setup(x => x.ResetPasswordAsync(model.Email, model.Token, model.NewPassword))
            .ReturnsAsync(new PasswordResetResult(true, Array.Empty<string>()));

        // Act
        var result = await controller.HandleResetPassword(model);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirectResult = result as RedirectToActionResult;
        redirectResult!.ActionName.Should().Be("ResetPasswordConfirmation");
    }

    [Fact]
    public async Task HandleResetPassword_WithFailedReset_ShouldReturnViewWithErrors()
    {
        // Arrange
        var controller = CreateController();
        var model = new ResetPasswordViewModel
        {
            Email = "test@example.com",
            Token = "invalid-token",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };
        _authServiceMock.Setup(x => x.ResetPasswordAsync(model.Email, model.Token, model.NewPassword))
            .ReturnsAsync(new PasswordResetResult(false, new[] { "Invalid token" }));

        // Act
        var result = await controller.HandleResetPassword(model);

        // Assert
        result.Should().BeOfType<ViewResult>();
        controller.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region ResetPasswordConfirmation Tests

    [Fact]
    public void ResetPasswordConfirmation_ShouldReturnView()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = controller.ResetPasswordConfirmation();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    #endregion
}
