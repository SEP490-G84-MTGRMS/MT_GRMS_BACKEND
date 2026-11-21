using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Infrastructure.Services;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests.Services;

public class EmailAndStorageServiceTests
{
    [Fact]
    public async Task EmailService_DevMode_NoSmtpCredentials_ShouldLogAndReturnFalse()
    {
        var settings = Options.Create(new EmailSettings
        {
            SmtpHost = "mail.oxmail.vn",
            SmtpPort = 587,
            SmtpUser = "",
            SmtpPass = ""
        });

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var service = new EmailService(settings, NullLogger<EmailService>.Instance, mockEnv.Object);

        await service.SendPasswordResetOtpAsync("test@example.com", "Test User", "123456");
        var loginResult = await service.SendLoginOtpAsync("test@example.com", "Test User", "123456");
        Assert.False(loginResult);

        var regResult1 = await service.SendRegistrationOtpAsync("test@example.com", "Test User", "My Mart", "123456");
        Assert.False(regResult1);

        var regResult2 = await service.SendRegistrationOtpAsync("test@example.com", "", "", "123456");
        Assert.False(regResult2);
    }

    [Fact]
    public async Task EmailService_ProductionMode_NoSmtpCredentials_ShouldReturnFalseWithoutConsoleLog()
    {
        var settings = Options.Create(new EmailSettings
        {
            SmtpHost = "mail.oxmail.vn",
            SmtpPort = 587,
            SmtpUser = null,
            SmtpPass = null
        });

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var service = new EmailService(settings, NullLogger<EmailService>.Instance, mockEnv.Object);

        var loginResult = await service.SendLoginOtpAsync("test@example.com", "Test User", "123456");
        Assert.False(loginResult);
    }

    [Fact]
    public async Task EmailService_WithConfiguredSmtp_ShouldDispatchBackgroundEmailAndReturnTrue()
    {
        var settings = Options.Create(new EmailSettings
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = 2525,
            SmtpUser = "user@test.com",
            SmtpPass = "secretpass",
            SenderEmail = "no-reply@test.com",
            SenderName = "THViet System",
            EnableSsl = false
        });

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var service = new EmailService(settings, NullLogger<EmailService>.Instance, mockEnv.Object);

        var result = await service.SendLoginOtpAsync("recipient@example.com", "Recipient Name", "654321");
        Assert.True(result);
    }

    [Fact]
    public async Task CloudinaryService_UnconfiguredCredentials_ThrowsInvalidOperationException()
    {
        var settings = Options.Create(new CloudinarySettings
        {
            CloudName = "",
            ApiKey = "",
            ApiSecret = ""
        });

        var service = new CloudinaryService(settings, NullLogger<CloudinaryService>.Instance);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var exUpload = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadImageAsync(stream, "sample.png"));
        Assert.Contains("chưa được cấu hình", exUpload.Message);

        var deleteResult = await service.DeleteImageAsync("some-public-id");
        Assert.False(deleteResult);
    }

    [Fact]
    public async Task CloudinaryService_ConfiguredCredentials_EmptyPublicId_ReturnsFalse()
    {
        var settings = Options.Create(new CloudinarySettings
        {
            CloudName = "test-cloud",
            ApiKey = "1234567890",
            ApiSecret = "secret_key"
        });

        var service = new CloudinaryService(settings, NullLogger<CloudinaryService>.Instance);

        Assert.False(await service.DeleteImageAsync(""));
        Assert.False(await service.DeleteImageAsync("   "));
        Assert.False(await service.DeleteImageAsync(null!));
    }

    [Fact]
    public async Task CloudinaryDataUrlHelper_AllBranches_ShouldWork()
    {
        // 1. IsDataImageUrl
        Assert.False(CloudinaryDataUrlHelper.IsDataImageUrl(null));
        Assert.False(CloudinaryDataUrlHelper.IsDataImageUrl(""));
        Assert.False(CloudinaryDataUrlHelper.IsDataImageUrl("   "));
        Assert.False(CloudinaryDataUrlHelper.IsDataImageUrl("https://res.cloudinary.com/demo.jpg"));
        Assert.True(CloudinaryDataUrlHelper.IsDataImageUrl("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        Assert.True(CloudinaryDataUrlHelper.IsDataImageUrl("  DATA:IMAGE/JPEG;BASE64,/9j/4AAQSkZJRg=="));

        // 2. ResolveImageUrlAsync: null / whitespace
        Assert.Null(await CloudinaryDataUrlHelper.ResolveImageUrlAsync(null, null, "folder", "prefix"));
        Assert.Equal("", await CloudinaryDataUrlHelper.ResolveImageUrlAsync(null, "", "folder", "prefix"));
        Assert.Equal("   ", await CloudinaryDataUrlHelper.ResolveImageUrlAsync(null, "   ", "folder", "prefix"));

        // 3. ResolveImageUrlAsync: non data URL
        var normalUrl = "https://images.unsplash.com/photo-1";
        Assert.Equal(normalUrl, await CloudinaryDataUrlHelper.ResolveImageUrlAsync(null, normalUrl, "folder", "prefix"));

        // 4. ResolveImageUrlAsync: data URL but cloudinaryService is null
        var validDataUrlPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        Assert.Equal(validDataUrlPng, await CloudinaryDataUrlHelper.ResolveImageUrlAsync(null, validDataUrlPng, "folder", "prefix"));

        // 5. ResolveImageUrlAsync: cache hit with non-null service
        var mockCloud = new Mock<ICloudinaryService>();
        var cache = new Dictionary<string, string> { [validDataUrlPng] = "https://cached-url.jpg" };
        var cachedResult = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, validDataUrlPng, "folder", "prefix", uploadedCache: cache);
        Assert.Equal("https://cached-url.jpg", cachedResult);

        // 6. ResolveImageUrlAsync: invalid comma
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, "data:image/png;base64", "folder", "prefix"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, "data:image/png;base64,", "folder", "prefix"));

        // 7. ResolveImageUrlAsync: invalid base64 content
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, "data:image/png;base64,@@@invalid_base64@@@", "folder", "prefix"));

        // 8. ResolveImageUrlAsync: valid png upload
        mockCloud.Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), It.Is<string>(s => s.EndsWith(".png")), "folder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cloud.com/uploaded.png", PublicId = "uploaded" });

        var newCache = new Dictionary<string, string>();
        var uploadedPng = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, validDataUrlPng, "folder", "prefix", uploadedCache: newCache);
        Assert.Equal("https://cloud.com/uploaded.png", uploadedPng);
        Assert.Equal("https://cloud.com/uploaded.png", newCache[validDataUrlPng]);

        // 9. ResolveImageUrlAsync: valid webp & jpg extensions
        var validDataUrlWebp = "data:image/webp;base64,UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==";
        mockCloud.Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), It.Is<string>(s => s.EndsWith(".webp")), "folder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cloud.com/uploaded.webp", PublicId = "uploaded_webp" });
        var uploadedWebp = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, validDataUrlWebp, "folder", "prefix");
        Assert.Equal("https://cloud.com/uploaded.webp", uploadedWebp);

        var validDataUrlJpg = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";
        mockCloud.Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), It.Is<string>(s => s.EndsWith(".jpg")), "folder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cloud.com/uploaded.jpg", PublicId = "uploaded_jpg" });
        var uploadedJpg = await CloudinaryDataUrlHelper.ResolveImageUrlAsync(mockCloud.Object, validDataUrlJpg, "folder", "prefix");
        Assert.Equal("https://cloud.com/uploaded.jpg", uploadedJpg);

        // 10. ResolveImageUrlsAsync
        Assert.Empty(await CloudinaryDataUrlHelper.ResolveImageUrlsAsync(mockCloud.Object, null, "folder", "prefix"));
        var listUrls = new List<string> { "", "  ", validDataUrlPng, "https://raw.jpg" };
        var resolvedList = await CloudinaryDataUrlHelper.ResolveImageUrlsAsync(mockCloud.Object, listUrls, "folder", "prefix");
        Assert.Equal(2, resolvedList.Count);
        Assert.Contains("https://cloud.com/uploaded.png", resolvedList);
        Assert.Contains("https://raw.jpg", resolvedList);
    }

    [Fact]
    public async Task SmtpSupplierOrderEmailSender_UnconfiguredHostOrFrom_LogsWarningWithoutSending()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SMTP_HOST"] = "",
                ["SMTP_FROM"] = ""
            })
            .Build();

        var sender = new SmtpSupplierOrderEmailSender(config, NullLogger<SmtpSupplierOrderEmailSender>.Instance);

        var msg = new SupplierOrderEmailMessage
        {
            To = "supplier@vendor.com",
            Cc = new List<string> { "cc1@vendor.com" },
            Subject = "Đơn đặt hàng PO-01",
            BodyText = "Chi tiết đơn hàng...",
            AttachmentBytes = new byte[] { 1, 2, 3 },
            AttachmentFileName = "PO-01.xlsx"
        };

        // Does not throw, logs fallback
        await sender.SendSupplierOrderAsync(msg);
    }

    [Fact]
    public void EmailCcHelper_Validations_ShouldCoverAllCases()
    {
        // 1. IsValidEmail
        Assert.False(EmailCcHelper.IsValidEmail(""));
        Assert.False(EmailCcHelper.IsValidEmail("   "));
        Assert.False(EmailCcHelper.IsValidEmail(new string('a', 201) + "@test.com"));
        Assert.False(EmailCcHelper.IsValidEmail("bad\nemail@test.com"));
        Assert.False(EmailCcHelper.IsValidEmail("bad\remail@test.com"));
        Assert.False(EmailCcHelper.IsValidEmail("not-an-email"));
        Assert.False(EmailCcHelper.IsValidEmail("a@b"));
        Assert.True(EmailCcHelper.IsValidEmail("test.user@company.com.vn"));

        // 2. NormalizeCc
        Assert.Empty(EmailCcHelper.NormalizeCc(null, "main@to.com"));
        Assert.Empty(EmailCcHelper.NormalizeCc(new[] { "", "   " }, "main@to.com"));

        // Invalid email throws
        Assert.Throws<ArgumentException>(() =>
            EmailCcHelper.NormalizeCc(new[] { "invalid-email" }, "main@to.com"));

        // Same as To throws
        Assert.Throws<ArgumentException>(() =>
            EmailCcHelper.NormalizeCc(new[] { "main@to.com" }, "MAIN@TO.COM"));

        // Exceeds maxCc throws
        var manyEmails = new List<string>();
        for (int i = 0; i < 5; i++) manyEmails.Add($"user{i}@test.com");
        Assert.Throws<ArgumentException>(() =>
            EmailCcHelper.NormalizeCc(manyEmails, "main@to.com", maxCc: 3));

        // Deduplication & success
        var validCc = EmailCcHelper.NormalizeCc(new[] { "dup@test.com", "DUP@TEST.COM", "other@test.com" }, "main@to.com");
        Assert.Equal(2, validCc.Count);
        Assert.Equal("dup@test.com", validCc[0]);
        Assert.Equal("other@test.com", validCc[1]);

        // 3. ToJson & FromJson
        var json = EmailCcHelper.ToJson(new[] { "one@test.com", "two@test.com" });
        Assert.Contains("one@test.com", json);

        var listFromJson = EmailCcHelper.FromJson(json);
        Assert.Equal(2, listFromJson.Count);

        Assert.Empty(EmailCcHelper.FromJson(null));
        Assert.Empty(EmailCcHelper.FromJson("   "));
        Assert.Empty(EmailCcHelper.FromJson("invalid-json-{"));
    }

    [Fact]
    public void RawPrinterHelper_NonExistentPrinter_ReturnsFalse()
    {
        var dummyBytes = Encoding.UTF8.GetBytes("ESC @ TEST PRINT");
        var result = RawPrinterHelper.SendBytesToPrinter("NON_EXISTENT_PRINTER_9999", dummyBytes);
        Assert.False(result);
    }
}
