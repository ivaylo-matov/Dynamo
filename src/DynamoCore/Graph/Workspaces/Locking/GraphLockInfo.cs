using System;
using Newtonsoft.Json;

namespace Dynamo.Graph.Workspaces.Locking
{
    internal sealed class GraphLockInfo
    {
        [JsonProperty("schemaVersion")]
        internal int SchemaVersion { get; set; }

        [JsonProperty("sessionId")]
        internal Guid SessionId { get; set; }

        [JsonProperty("graphPath")]
        internal string GraphPath { get; set; }

        [JsonProperty("userName")]
        internal string UserName { get; set; }

        [JsonProperty("machineName")]
        internal string MachineName { get; set; }

        [JsonProperty("processId")]
        internal int ProcessId { get; set; }

        [JsonProperty("processStartUtc")]
        internal DateTime ProcessStartUtc { get; set; }

        [JsonProperty("dynamoVersion")]
        internal string DynamoVersion { get; set; }

        [JsonProperty("dynamoMajorMinor")]
        internal string DynamoMajorMinor { get; set; }

        [JsonProperty("acquiredUtc")]
        internal DateTime AcquiredUtc { get; set; }

        [JsonProperty("lastHeartbeatUtc")]
        internal DateTime LastHeartbeatUtc { get; set; }
    }
}
