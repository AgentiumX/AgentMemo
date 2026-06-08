using Newtonsoft.Json;

namespace DesktopMemo.Models
{
    public class AppSettings
    {
        [JsonProperty("apiPort")]
        public int ApiPort { get; set; } = 19527;

        [JsonProperty("defaultColor")]
        public string DefaultColor { get; set; } = "yellow";

        [JsonProperty("autoStart")]
        public bool AutoStart { get; set; } = false;

        [JsonProperty("blurIntensity")]
        public int BlurIntensity { get; set; } = 80;

        [JsonProperty("fontSize")]
        public double FontSize { get; set; } = 14;
    }
}
