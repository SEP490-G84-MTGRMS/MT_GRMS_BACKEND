using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using Xunit;

namespace MT_GRMS.UnitTests;

public class MediaUploadTests
{
    private readonly Mock<ICloudinaryService> _mockCloudinaryService;
    private readonly Mock<ILogger<MediaController>> _mockLogger;
    private readonly MediaController _controller;

    public MediaUploadTests()
    {
        _mockCloudinaryService = new Mock<ICloudinaryService>();
        _mockLogger = new Mock<ILogger<MediaController>>();
        _controller = new MediaController(_mockCloudinaryService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task UploadImage_WithNullRequestOrFile_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.UploadImage(new ImageUploadRequest { File = null }, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_FILE", apiResponse.ErrorCode);
    }

    [Fact]
    public async Task UploadImage_WithEmptyFile_ReturnsBadRequest()
    {
        // Arrange
        var emptyFileMock = new Mock<IFormFile>();
        emptyFileMock.Setup(f => f.Length).Returns(0);
        emptyFileMock.Setup(f => f.FileName).Returns("empty.jpg");

        // Act
        var result = await _controller.UploadImage(
            new ImageUploadRequest { File = emptyFileMock.Object, Folder = "mtgrms/products" },
            CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_FILE", apiResponse.ErrorCode);
    }

    [Fact]
    public async Task UploadImage_WithFileSizeExceeding5MB_ReturnsBadRequest()
    {
        // Arrange
        var largeFileMock = new Mock<IFormFile>();
        largeFileMock.Setup(f => f.Length).Returns(6 * 1024 * 1024); // 6MB > 5MB limit
        largeFileMock.Setup(f => f.FileName).Returns("large-poster.png");

        // Act
        var result = await _controller.UploadImage(
            new ImageUploadRequest { File = largeFileMock.Object, Folder = "mtgrms/products" },
            CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("FILE_TOO_LARGE", apiResponse.ErrorCode);
        Assert.Contains("5MB", apiResponse.Message);
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("script.exe")]
    [InlineData("archive.zip")]
    [InlineData("image_without_extension")]
    public async Task UploadImage_WithInvalidExtension_ReturnsBadRequest(string fileName)
    {
        // Arrange
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.FileName).Returns(fileName);

        // Act
        var result = await _controller.UploadImage(
            new ImageUploadRequest { File = fileMock.Object, Folder = "mtgrms/products" },
            CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("INVALID_FORMAT", apiResponse.ErrorCode);
    }

    [Theory]
    [InlineData("product.jpg")]
    [InlineData("banner.jpeg")]
    [InlineData("item.png")]
    [InlineData("thumbnail.webp")]
    public async Task UploadImage_WithValidImageFile_ReturnsOkWithImageUrl(string fileName)
    {
        // Arrange
        var content = "fake image file binary stream";
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(bytes.Length);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);

        var expectedDto = new ImageUploadResponseDto
        {
            Url = "https://res.cloudinary.com/dt8l0vdws/image/upload/v12345/mtgrms/products/sample.jpg",
            PublicId = "mtgrms/products/sample",
            Format = "jpg",
            Bytes = bytes.Length,
            Width = 800,
            Height = 800
        };

        _mockCloudinaryService
            .Setup(s => s.UploadImageAsync(It.IsAny<Stream>(), fileName, "mtgrms/products", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.UploadImage(
            new ImageUploadRequest { File = fileMock.Object, Folder = "mtgrms/products" },
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<ImageUploadResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(expectedDto.Url, apiResponse.Data.Url);
        Assert.Equal(expectedDto.PublicId, apiResponse.Data.PublicId);
    }

    [Fact]
    public async Task UploadImage_WhenServiceThrowsInvalidOperationException_Returns500WithError()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("image data");
        var stream = new MemoryStream(bytes);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(bytes.Length);
        fileMock.Setup(f => f.FileName).Returns("photo.jpg");
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);

        _mockCloudinaryService
            .Setup(s => s.UploadImageAsync(It.IsAny<Stream>(), "photo.jpg", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cloudinary credentials missing"));

        // Act
        var result = await _controller.UploadImage(
            new ImageUploadRequest { File = fileMock.Object, Folder = "mtgrms/products" },
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(objectResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("UPLOAD_FAILED", apiResponse.ErrorCode);
    }
}
