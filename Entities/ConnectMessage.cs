namespace ConnectAnnounce.Entities;

public class ConnectMessage
{
    public string Message { get; set; } = string.Empty;
    public bool AllowCustomMessage { get; set; } = false;
    public List<string> Users { get; set; } = [];
    public List<string> AdminFlag { get; set; } = [];
}