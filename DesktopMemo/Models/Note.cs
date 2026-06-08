using System;
using Newtonsoft.Json;

namespace DesktopMemo.Models
{
    public class Note
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("title")]
        public string Title { get; set; } = "新便笺";

        [JsonProperty("content")]
        public string Content { get; set; } = "";

        [JsonProperty("color")]
        public string Color { get; set; } = "yellow";

        [JsonProperty("x")]
        public double X { get; set; } = 100;

        [JsonProperty("y")]
        public double Y { get; set; } = 100;

        [JsonProperty("width")]
        public double Width { get; set; } = 280;

        [JsonProperty("height")]
        public double Height { get; set; } = 320;

        [JsonProperty("alwaysOnTop")]
        public bool AlwaysOnTop { get; set; } = false;

        [JsonProperty("visible")]
        public bool Visible { get; set; } = true;

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
