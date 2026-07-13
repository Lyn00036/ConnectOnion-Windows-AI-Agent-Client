using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.UnitTests.Models;

public sealed class ChatAttachmentTests
{
    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1572864, "1.5 MB")]
    public void FormatSize_FormatsBoundaryValues(long bytes, string expected)
    {
        Assert.Equal(expected, ChatAttachment.FormatSize(bytes));
    }

    [Fact]
    public void Status_WhenFailed_RaisesStatusAndDerivedPropertyNotifications()
    {
        var attachment = new ChatAttachment();
        var changed = new List<string?>();
        attachment.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        attachment.Status = AttachmentStatus.Failed;

        Assert.True(attachment.IsFailed);
        Assert.Equal([nameof(ChatAttachment.Status), nameof(ChatAttachment.IsFailed)], changed);
    }

    [Fact]
    public void Error_WhenSet_RaisesErrorAndHasErrorNotifications()
    {
        var attachment = new ChatAttachment();
        var changed = new List<string?>();
        attachment.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        attachment.Error = "upload failed";

        Assert.True(attachment.HasError);
        Assert.Equal([nameof(ChatAttachment.Error), nameof(ChatAttachment.HasError)], changed);
    }

    [Fact]
    public void PreviewUri_IsOnlyAvailableForLocalImages()
    {
        var image = new ChatAttachment
        {
            Kind = AttachmentKind.Image,
            LocalCachePath = Path.GetFullPath("preview.png"),
        };
        var file = new ChatAttachment
        {
            Kind = AttachmentKind.File,
            LocalCachePath = Path.GetFullPath("document.pdf"),
        };

        Assert.Equal(new Uri(image.LocalCachePath!).AbsoluteUri, image.PreviewUri);
        Assert.Null(file.PreviewUri);
    }
}
