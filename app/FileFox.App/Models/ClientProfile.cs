using System.Text.Json.Serialization;

namespace FileFox.Models;

/// <summary>
/// לקוח מוכר מראש. Keywords הם כינויים/וריאציות של השם שיחפשו בתוכן/שם הקובץ כדי לזהות שהמסמך שייך ללקוח הזה.
/// </summary>
public class ClientProfile
{
    public string Name { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    [JsonIgnore]
    public string FolderName => string.IsNullOrWhiteSpace(Name) ? "Unknown Client" : Name;
}
