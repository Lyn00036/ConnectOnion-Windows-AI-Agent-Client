using System.Collections.Generic;
using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Models.Notifications;

namespace ConnectOnion.WinUIClient.Data;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(NotificationSettings))]
public sealed partial class AppJsonContext : JsonSerializerContext;
