using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class ImageContentTests
{
    [Fact]
    public void FromUrl_SetsUrlAndDetail()
    {
        // Act
        var image = ImageContent.FromUrl("https://test.example.com/img.png", "high");

        // Assert
        image.Url.Should().Be("https://test.example.com/img.png");
        image.Detail.Should().Be("high");
        image.Data.Should().BeNull();
    }

    [Fact]
    public void FromBytes_SetsDataAndMediaType()
    {
        // Arrange
        var bytes = new byte[] { 9, 8, 7 };

        // Act
        var image = ImageContent.FromBytes(bytes, "image/png");

        // Assert
        image.Data.Should().Equal(bytes);
        image.MediaType.Should().Be("image/png");
    }

    [Fact]
    public void FromBytes_EmptyData_Throws()
    {
        // Act
        Action act = () => ImageContent.FromBytes([], "image/png");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromStream_ReadsAllBytes()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var stream = new MemoryStream(bytes);

        // Act
        var image = ImageContent.FromStream(stream, "image/gif");

        // Assert
        image.Data.Should().Equal(bytes);
        image.MediaType.Should().Be("image/gif");
    }

    [Fact]
    public void FromFile_InfersMediaTypeFromExtension()
    {
        // Arrange
        var path = WriteTempFile(".png", new byte[] { 1, 2, 3 });
        try
        {
            // Act
            var image = ImageContent.FromFile(path);

            // Assert
            image.MediaType.Should().Be("image/png");
            image.Data.Should().Equal(new byte[] { 1, 2, 3 });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromFile_UnknownExtensionWithoutMediaType_Throws()
    {
        // Arrange
        var path = WriteTempFile(".dat", new byte[] { 1 });
        try
        {
            // Act
            Action act = () => ImageContent.FromFile(path);

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*media type*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromFile_MissingFile_Throws()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");

        // Act
        Action act = () => ImageContent.FromFile(path);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ResolveUrl_WithData_ReturnsDataUri()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var image = ImageContent.FromBytes(bytes, "image/png");

        // Act & Assert
        image.ResolveUrl().Should().Be($"data:image/png;base64,{Convert.ToBase64String(bytes)}");
    }

    [Fact]
    public void ResolveUrl_WithUrlOnly_ReturnsUrl()
    {
        // Arrange
        var image = ImageContent.FromUrl("https://test.example.com/img.png");

        // Act & Assert
        image.ResolveUrl().Should().Be("https://test.example.com/img.png");
    }

    [Fact]
    public void ResolveUrl_WithDataButNullMediaType_FallsBackToOctetStream()
    {
        // Arrange — bytes present but MediaType is null/empty
        var bytes = new byte[] { 0xDE, 0xAD };
        var image = new ImageContent { Data = bytes };

        // Act & Assert
        image.ResolveUrl().Should().StartWith("data:application/octet-stream;base64,");
    }

    private static string WriteTempFile(string extension, byte[] contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"img-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, contents);
        return path;
    }
}
