using System;
using Newtonsoft.Json;

namespace Dynamo.Graph.Workspaces.Locking
{
    /// <summary>
    /// Serializable metadata stored in a graph lock sidecar file.
    /// </summary>
    internal sealed class GraphLockInfo
    {
        [JsonProperty("schemaVersion")]
        internal int SchemaVersion { get; set; }

        [JsonProperty("sessionId")]
        internal Guid SessionId { get; set; }

        [JsonProperty("graphPath")]
        internal string GraphPath { get; set; }

        [JsonProperty("machineName")]
        internal string MachineName { get; set; }

        [JsonProperty("processId")]
        internal int ProcessId { get; set; }

        [JsonProperty("processStartUtc")]
        internal DateTime ProcessStartUtc { get; set; }

        [JsonProperty("lastHeartbeatUtc")]
        internal DateTime LastHeartbeatUtc { get; set; }
    }
}
