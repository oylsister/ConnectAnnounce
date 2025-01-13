namespace ConnectAnnounce.Entities;

public class ClientData(string steamAuth, string name, string ip, string country)
{
    public string SteamAuth { get; set; } = steamAuth;
    public string PlayerName { get; set; } = name;
    public string IPAddress { get; set; } = ip;
    public string Country { get; set; } = country;
}