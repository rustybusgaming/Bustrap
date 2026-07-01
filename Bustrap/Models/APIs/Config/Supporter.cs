namespace Bustrap.Models.APIs.Config
{
    public class Supporter
    {
        [JsonPropertyName("imageAsset")]
        public string ImageAsset { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        public string Image =>
            !string.IsNullOrEmpty(ImageAsset)
                ? (ImageAsset.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? ImageAsset
                    : $"https://raw.githubusercontent.com/BloxstrapLabs/config/main/assets/{ImageAsset}")
                : "https://raw.githubusercontent.com/BloxstrapLabs/config/main/assets/placeholder.png";
    }
}
