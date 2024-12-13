using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CheckerServer
{
    public class Action
    {
        [JsonPropertyName("Action")]
        public string action { get; set; }

        [JsonPropertyName("From")]
        public string from { get; set; }

        [JsonPropertyName("To")]
        public string to { get; set; }

        [JsonPropertyName("IsCapture")]
        public bool iscapture { get; set; }
        [JsonPropertyName("capture")]
        public string capture { get; set; }
        [JsonPropertyName("HasCaptureMoves")]
        public bool HasCaptureMoves { get; set; }
    }
}
