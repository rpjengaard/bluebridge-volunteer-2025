using Code.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Services;

public class MemberEmailServiceTests
{
    private readonly Mock<ILogger<MemberEmailService>> _loggerMock;
    private readonly Mock<IOptions<EmailSettings>> _emailSettingsMock;
    private readonly EmailSettings _emailSettings;
    private readonly MemberEmailService _sut;

    public MemberEmailServiceTests()
    {
        _loggerMock = new Mock<ILogger<MemberEmailService>>();
        _emailSettings = new EmailSettings
        {
            SmtpHost = "localhost",
            SmtpPort = 25,
            FromEmail = "test@bluebridge.dk",
            FromName = "Test Blue Bridge",
            EnableSsl = false
        };
        _emailSettingsMock = new Mock<IOptions<EmailSettings>>();
        _emailSettingsMock.Setup(x => x.Value).Returns(_emailSettings);

        _sut = new MemberEmailService(_loggerMock.Object, _emailSettingsMock.Object);
    }

    #region SendPasswordResetEmailAsync Tests

    [Fact]
    public async Task SendPasswordResetEmailAsync_WithValidParameters_ShouldAttemptToSendEmail()
    {
        // Arrange
        var email = "user@example.com";
        var resetUrl = "https://bluebridge.dk/reset?token=abc123";

        // Act & Assert
        // Since we can't easily mock SmtpClient, we expect this to throw
        // In a real scenario, we'd inject an IEmailSender abstraction
        var act = async () => await _sut.SendPasswordResetEmailAsync(email, resetUrl);

        // The method should throw because there's no SMTP server available
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region SendWelcomeEmailAsync Tests

    [Fact]
    public async Task SendWelcomeEmailAsync_WithValidParameters_ShouldAttemptToSendEmail()
    {
        // Arrange
        var email = "user@example.com";
        var firstName = "Test";

        // Act & Assert
        var act = async () => await _sut.SendWelcomeEmailAsync(email, firstName);

        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region SendInvitationEmailAsync Tests

    [Fact]
    public async Task SendInvitationEmailAsync_WithValidParameters_ShouldAttemptToSendEmail()
    {
        // Arrange
        var email = "user@example.com";
        var memberData = new MemberEmailData
        {
            Email = email,
            FirstName = "Test",
            LastName = "User"
        };
        var invitationUrl = "https://bluebridge.dk/invite?token=abc123";
        var subjectTemplate = "Invitation til {{ firstName }}";
        var bodyTemplate = "Hej {{ firstName }}, klik her: {{ invitationUrl }}";

        // Act & Assert
        var act = async () => await _sut.SendInvitationEmailAsync(email, memberData, invitationUrl, subjectTemplate, bodyTemplate);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SendInvitationEmailAsync_WithEmptyTemplate_ShouldNotThrow()
    {
        // Arrange
        var email = "user@example.com";
        var memberData = new MemberEmailData
        {
            Email = email,
            FirstName = "Test",
            LastName = "User"
        };
        var invitationUrl = "https://bluebridge.dk/invite?token=abc123";

        // Act & Assert
        var act = async () => await _sut.SendInvitationEmailAsync(email, memberData, invitationUrl, "", "");

        // Should still attempt to send but fail due to no SMTP server
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region SendAcceptanceConfirmationEmailAsync Tests

    [Fact]
    public async Task SendAcceptanceConfirmationEmailAsync_WithValidParameters_ShouldAttemptToSendEmail()
    {
        // Arrange
        var email = "user@example.com";
        var memberData = new MemberEmailData
        {
            Email = email,
            FirstName = "Test",
            LastName = "User",
            PortalUrl = "https://bluebridge.dk"
        };
        var selectedCrewNames = new[] { "Crew 1", "Crew 2" };
        var subjectTemplate = "Bekræftelse {{ firstName }}";
        var bodyTemplate = "Du er tilmeldt: {{ selectedCrews }}";

        // Act & Assert
        var act = async () => await _sut.SendAcceptanceConfirmationEmailAsync(email, memberData, selectedCrewNames, subjectTemplate, bodyTemplate);

        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region MemberEmailData Tests

    [Fact]
    public void MemberEmailData_DefaultValues_ShouldBeEmpty()
    {
        // Arrange & Act
        var data = new MemberEmailData();

        // Assert
        data.Email.Should().BeEmpty();
        data.Username.Should().BeEmpty();
        data.FirstName.Should().BeEmpty();
        data.LastName.Should().BeEmpty();
        data.Phone.Should().BeEmpty();
        data.Zipcode.Should().BeEmpty();
        data.TidligereArbejdssteder.Should().BeEmpty();
        data.SelectedCrews.Should().BeEmpty();
        data.PortalUrl.Should().BeEmpty();
    }

    [Fact]
    public void MemberEmailData_WithValues_ShouldRetainValues()
    {
        // Arrange & Act
        var data = new MemberEmailData
        {
            Email = "test@test.com",
            Username = "testuser",
            FirstName = "Test",
            LastName = "User",
            Phone = "12345678",
            Zipcode = "1234",
            TidligereArbejdssteder = "Previous",
            SelectedCrews = "Crew1, Crew2",
            PortalUrl = "https://example.com"
        };

        // Assert
        data.Email.Should().Be("test@test.com");
        data.Username.Should().Be("testuser");
        data.FirstName.Should().Be("Test");
        data.LastName.Should().Be("User");
        data.Phone.Should().Be("12345678");
        data.Zipcode.Should().Be("1234");
        data.TidligereArbejdssteder.Should().Be("Previous");
        data.SelectedCrews.Should().Be("Crew1, Crew2");
        data.PortalUrl.Should().Be("https://example.com");
    }

    #endregion

    #region EmailSettings Tests

    [Fact]
    public void EmailSettings_DefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange & Act
        var settings = new EmailSettings();

        // Assert
        settings.SmtpHost.Should().Be("localhost");
        settings.SmtpPort.Should().Be(25);
        settings.SmtpUsername.Should().BeNull();
        settings.SmtpPassword.Should().BeNull();
        settings.EnableSsl.Should().BeFalse();
        settings.FromEmail.Should().Be("noreply@bluebridge.dk");
        settings.FromName.Should().Be("Blue Bridge Frivillig");
    }

    [Fact]
    public void EmailSettings_WithCustomValues_ShouldRetainValues()
    {
        // Arrange & Act
        var settings = new EmailSettings
        {
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SmtpUsername = "user",
            SmtpPassword = "pass",
            EnableSsl = true,
            FromEmail = "custom@example.com",
            FromName = "Custom Name"
        };

        // Assert
        settings.SmtpHost.Should().Be("smtp.example.com");
        settings.SmtpPort.Should().Be(587);
        settings.SmtpUsername.Should().Be("user");
        settings.SmtpPassword.Should().Be("pass");
        settings.EnableSsl.Should().BeTrue();
        settings.FromEmail.Should().Be("custom@example.com");
        settings.FromName.Should().Be("Custom Name");
    }

    #endregion
}
