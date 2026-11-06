using System.Security.Cryptography;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventLauscherApi.Tests.Infrastructure.TestEnvironment;

namespace EventLauscherApi.Tests.Controllers;

public sealed class MediaFilesTests
{
    [Fact]
    public async Task UploadStoresBytesAndSha256AndRejectsDuplicateContentWithDifferentName()
    {
        using var env = new TestEnvironment();
        var controller = env.Attach(new MediaFilesController(env.Db));
        byte[] bytes = { 1, 2, 3, 4, 5 };
        using var first = new MemoryStream(bytes);
        var result = Ok(await controller.UploadMediaFile(new FormFile(first, 0, first.Length, "file", "first.png")));
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, result.GetProperty("hash").GetString());
        using var second = new MemoryStream(bytes);
        Assert.IsType<ConflictObjectResult>(await controller.UploadMediaFile(new FormFile(second, 0, second.Length, "file", "second.jpg")));
        await using var reload = env.Reload();
        var stored = await reload.MediaFiles.SingleAsync();
        Assert.Equal(bytes, stored.Data);
        Assert.Equal(expectedHash, stored.Hash);
        Assert.Equal("first.png", stored.FileName);
        Assert.Equal(stored.Id, result.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UploadRejectsMissingAndEmptyFile()
    {
        using var env = new TestEnvironment();
        var controller = env.Attach(new MediaFilesController(env.Db));
        Assert.IsType<BadRequestObjectResult>(await controller.UploadMediaFile(null!));
        using var empty = new MemoryStream();
        Assert.IsType<BadRequestObjectResult>(await controller.UploadMediaFile(new FormFile(empty, 0, 0, "file", "empty.png")));
        Assert.Empty(env.Db.MediaFiles);
    }

    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("photo.unknown", "image/jpeg")]
    public async Task DownloadReturnsOriginalBytesContentTypeAndCacheHeader(string name, string contentType)
    {
        using var env = new TestEnvironment();
        var file = new MediaFile { FileName = name, Data = new byte[] { 5, 4, 3 } };
        env.Db.MediaFiles.Add(file);
        await env.Db.SaveChangesAsync();
        var controller = env.Attach(new MediaFilesController(env.Db));
        var result = Assert.IsType<FileContentResult>(await controller.GetImage(file.Id));
        Assert.Equal(file.Data, result.FileContents);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal("public,max-age=86400", controller.Response.Headers.CacheControl.ToString());
        Assert.IsType<NotFoundResult>(await controller.GetImage(int.MaxValue));
    }

    [Fact]
    public async Task BatchListAndHashLookupReturnExpectedMetadata()
    {
        using var env = new TestEnvironment();
        var selected = new MediaFile { FileName = "selected.png", Hash = "selected-hash", Data = new byte[] { 1, 2, 3 } };
        env.Db.MediaFiles.AddRange(selected, new MediaFile { FileName = "other.jpg", Hash = "other-hash" });
        await env.Db.SaveChangesAsync();
        var controller = env.Attach(new MediaFilesController(env.Db));
        var batch = Ok(controller.GetImages(new[] { selected.Id, int.MaxValue }));
        var item = Assert.Single(batch.EnumerateArray());
        Assert.Equal(selected.Id, item.GetProperty("id").GetInt32());
        Assert.Equal("selected.png", item.GetProperty("fileName").GetString());
        Assert.Equal(Convert.ToBase64String(selected.Data), item.GetProperty("base64").GetString());
        Assert.Empty(Ok(controller.GetImages(Array.Empty<int>())).EnumerateArray());
        var list = Ok(controller.ListFiles());
        Assert.Equal(2, list.GetArrayLength());
        Assert.True(list[0].TryGetProperty("uploadedAt", out _));
        Assert.False(list[0].TryGetProperty("data", out _));
        Assert.Equal(selected.Id, Ok(controller.GetByHash("selected-hash")).GetProperty("id").GetInt32());
        Assert.Equal(-1, Ok(controller.GetByHash("missing")).GetProperty("id").GetInt32());
    }
}
