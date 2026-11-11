using DuoWall.Core.Gallery;
using DuoWall.Core.Models;
using Xunit;

namespace DuoWall.Tests;

public sealed class UltraWideProviderTests
{
    [Fact]
    public void ParseGalleryTilesHtml_ParsesNormalAndVaultItemsAccurately()
    {
        const string sampleHtml = @"
<div class=""gallery-item"" data-filename=""uwshot-6300.jpg"">
  <a href=""wallpaper?image=uwshot-6300.jpg"" style=""display:contents"">
    <img src=""resizecachethumbs.php?image=uwshot-6300.jpg&width=400"" alt=""Mechanical scorpion, steampunk design"" loading=""lazy"">
  </a>
</div>
<div class=""gallery-item"" data-filename=""vault-99.jpg"">
  <a href=""wallpaper?image=vault-99.jpg"">
    <img src=""vault_image.php?image=vault-99.jpg&width=400"" alt=""Club Exclusive Nebula"" loading=""lazy"">
  </a>
</div>";

        var tagIndex = new Dictionary<string, List<string>>
        {
            ["uwshot-6300.jpg"] = new() { "Steampunk", "Animals" }
        };

        var items = UltraWideWallpapersProvider.ParseGalleryTilesHtml(sampleHtml, tagIndex);

        Assert.Equal(2, items.Count);
        Assert.Equal("uwshot-6300.jpg", items[0].FileName);
        Assert.False(items[0].RequiresAuthentication);
        Assert.Contains("Steampunk", items[0].Tags);
        Assert.Equal("https://ultrawidewallpapers.net/wallpapers/329/highres/uwshot-6300.jpg", items[0].HighResUrl);

        Assert.Equal("vault-99.jpg", items[1].FileName);
        Assert.True(items[1].RequiresAuthentication);
        Assert.Equal(string.Empty, items[1].HighResUrl);
    }

    [Fact]
    public void ParseDetailPageHtml_ExtractsTitleDescriptionTagsAndDimensions()
    {
        const string detailHtml = @"
<html><head>
<script type=""application/ld+json"">
{""@type"":""ImageObject"",""contentUrl"":""https://ultrawidewallpapers.net/wallpapers/329/highres/uwshot-6300.jpg""}
</script>
</head><body>
<div class=""wp-page"">
  <h1>Ultrawide Wallpaper #6300</h1>
  <div class=""wp-image"">
    <img src=""resizecache.php?image=uwshot-6300.jpg&amp;width=1700"" width=""1700"" height=""478"">
  </div>
  <div class=""wp-tags"">
    <a href=""gallery?tags=Animals"">Animals</a>
    <a href=""gallery?tags=Steampunk"">Steampunk</a>
  </div>
  <p class=""wp-desc"">Detailed mechanical scorpion in desert sunset.</p>
</div></body></html>";

        var item = new WallpaperItem { FileName = "uwshot-6300.jpg", Id = "uwshot-6300.jpg" };
        UltraWideWallpapersProvider.ParseDetailPageHtml(detailHtml, item);

        Assert.Equal("Ultrawide Wallpaper #6300", item.Title);
        Assert.Equal("Detailed mechanical scorpion in desert sunset.", item.Description);
        Assert.Equal(2, item.Tags.Count);
        Assert.Equal(7680, item.Width);
        Assert.Equal(2160, item.Height);
    }
}
