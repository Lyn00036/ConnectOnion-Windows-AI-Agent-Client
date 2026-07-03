using System.Collections.Generic;
using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ChatEnvelope))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
