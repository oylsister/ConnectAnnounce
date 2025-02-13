using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConnectAnnounce.Database;
using ConnectAnnounce.Entities;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VipCoreApi;

namespace ConnectAnnounce;

public class ConnectAnnounce(ILogger<ConnectAnnounce> logger) : BasePlugin
{
    public override string ModuleName => "Connect Announce";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "Oylsister";

    private readonly ILogger<ConnectAnnounce> _logger = logger;
    private MessageDatabase? _database;

    public Dictionary<string, ConnectMessage>? ConnectMessageList = [];
    public static string ConfigDir = Path.Combine(Application.RootDirectory, "configs/ConnectAnnounce/");

    IVipCoreApi? _vipCoreApi;
    private PluginCapability<IVipCoreApi> PluginCapability { get; } = new("vipcore:core");

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);

        _database = new MessageDatabase(this, _logger);
        _database.DatabaseOnLoad().Wait();

        if(!Directory.Exists(ConfigDir))
        {
            Directory.CreateDirectory(ConfigDir);
            _logger.LogWarning("[Load] Directory {path} is not existed, creating a new one.", ConfigDir);
        }

        var connectMsgPath = Path.Combine(ConfigDir, "settings.jsonc");

        if(!File.Exists(connectMsgPath))
        {
            _logger.LogWarning("[Load] setting.jsonc is not found!, Creating a new one.");
            CreateSettingFile();
        }

        ConnectMessageList = JsonConvert.DeserializeObject<Dictionary<string, ConnectMessage>>(File.ReadAllText(connectMsgPath));

        if(ConnectMessageList == null)
        {
            _logger.LogCritical("[Load] ConnectMessageList is null!");
            return;
        }
    }

    public override void Unload(bool hotReload)
    {
        DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        _database?.DatabaseOnUnload();
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _vipCoreApi = PluginCapability.Get();

        if (_vipCoreApi == null)
            return;
    }

    [CommandHelper(1, "css_joinmsg <message>", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_joinmsg")]
    public void JoinMessageCommand(CCSPlayerController? client, CommandInfo info)
    {
        if(client == null)
            return;

        var args = info.ArgString;

        if(args.Length > 256)
        {
            info.ReplyToCommand($" {ChatColors.Green}[ConnectAnnounce]{ChatColors.Default} Message maximum length is {ChatColors.Olive}256{ChatColors.Default}! (Your: {ChatColors.Red}{args.Length}{ChatColors.Default})");
            return;
        }

        var auth = client.AuthorizedSteamID?.SteamId64;

        if(auth == null)
        {
            info.ReplyToCommand($" {ChatColors.Green}[ConnectAnnounce]{ChatColors.Default} Your steamid is not valid!");
            return;
        }

        Task.Run(async () => {

            if(_database == null)
            {
                _logger.LogError("[JoinMessageCommand] Database is null!");
                return;
            }

            await _database.InsertPlayerMessage(auth.Value, args);

            Server.NextFrame(() => {
                var newMessage = ReplaceColorTag(args);
                client.PrintToChat($" {ChatColors.Green}[ConnectAnnounce]{ChatColors.Default} You have set join message to {newMessage}{ChatColors.Default}. (Message won't display in case if you don't have privilege in that time.)");
            });
        });
    }

    public void CreateSettingFile()
    {
        var connectMsgPath = Path.Combine(ConfigDir, "settings.jsonc");

        var file = File.CreateText(connectMsgPath);
        
        var message = new ConnectMessage();
        message.Message = "{BLUE}Player {DARKBLUE}{PLAYERNAME} {DEFAULT}({GREY}{STEAMAUTH}{DEFAULT}) has connected from {DARKBLUE}{COUNTRY}.";
        message.AllowCustomMessage = false;
        message.AdminFlag = [];

        var tempList = new Dictionary<string, ConnectMessage>
        {
            { "default", message }
        };

        var jsonString = JsonConvert.SerializeObject(tempList, Formatting.Indented);

        file.WriteLine(jsonString);
        file.Close();
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        AnnouncePlayerConnect(@event.Userid);
        return HookResult.Continue;
    }

    public void AnnouncePlayerConnect(CCSPlayerController? client)
    {
        if(client == null)
            return;

        if(ConnectMessageList == null)
        {
            _logger.LogCritical("[AnnouncePlayerConnect] ConnectMessageList is null");
            return;
        }

        var playerIP = NativeAPI.GetPlayerIpAddress(client.Slot);
        var ipAddress = playerIP.Split(':')[0];
        var country = GetCountry(ipAddress); 
        var steamAuth = client.AuthorizedSteamID?.SteamId2 ?? "Unknown SteamID";

        var data = new ClientData(steamAuth, client.PlayerName, playerIP, country);
        var key = GetUserMatchAuthorizeKey(client);

        if(key == null)
        {
            _logger.LogError("[AnnouncePlayerConnect] Connect message data is not valid!");
            return;
        }

        var found = ConnectMessageList.TryGetValue(key, out var initMessage);

        if(!found)
        {
            _logger.LogError("[AnnouncePlayerConnect] Connect message data is not found!");
            return;
        }

        var message = initMessage?.Message;

        if(message == null)
        {
            _logger.LogError("[AnnouncePlayerConnect] Connect message string is null!");
            return;
        }

        var colorDone = ReplaceColorTag(message);
        var tagDone = ReplaceSpecialTag(colorDone, data);

        var steamid = client.AuthorizedSteamID?.SteamId64;

        var sb = new StringBuilder(tagDone);

        Task.Run(async () => 
        {
            string? message = string.Empty;

            if(_database != null && steamid != null)
                message = await _database.GetPlayerMessage(steamid.Value);

            if(message != null)
            {
                var newMessage = ReplaceColorTag(message);
                var extend = "\x01: " + newMessage;
                sb.Append(extend);
            }

            // we have to give them a space or else first color tag will not work.
            Server.NextFrame(() => Server.PrintToChatAll($" {sb}"));
        });
    }

    public string? GetUserMatchAuthorizeKey(CCSPlayerController client)
    {
        if(ConnectMessageList == null)
        {
            _logger.LogCritical("[GetUserMatchAuthorize] ConnectMessageList is null!");
            return null;
        }

        var steamid = client.AuthorizedSteamID?.SteamId64;

        // we search for user first
        if(steamid.HasValue)
        {
            foreach(var data in ConnectMessageList)
            {
                if(data.Value.Users.Contains(steamid.Value.ToString()))
                    return data.Key;
            }
        }

        var adminFlag = AdminManager.GetPlayerAdminData(client)?.GetAllFlags();

        if (adminFlag != null && adminFlag.Count > 0)
        {
            // if it's root then we straight up finding the one that has a root access.
            if (adminFlag.Contains("@css/root"))
            {
                foreach (var data in ConnectMessageList)
                {
                    if (data.Value.AdminFlag.Contains("@css/root"))
                        return data.Key;
                }
            }

            // we don't need else, in case root connect message is not setting up so they can get it from other admin setup instead.
            foreach (var flag in adminFlag)
            {
                foreach (var data in ConnectMessageList)
                {
                    if (data.Value.AdminFlag.Contains(flag))
                        return data.Key;
                }
            }
        }

        if (_vipCoreApi?.IsClientVip(client) ?? false)
        {
            foreach (var data in ConnectMessageList)
            {
                if (data.Value.VIPOnly)
                    return data.Key;
            }
        }

        // against all odd and found nothing? then just use default one.
        return "default";
    }

    public string ReplaceColorTag(string message)
    {
        foreach (var item in ColorTag) 
        { 
            message = Regex.Replace(message, Regex.Escape(item.Key), item.Value, RegexOptions.IgnoreCase); 
        }

        return message;
    }

    public string ReplaceSpecialTag(string message, ClientData data)
    {
        foreach(var item in SpecialTag)
        {
            if(item == "{PLAYERNAME}" && message.Contains(item, StringComparison.OrdinalIgnoreCase))
                message = message.Replace(item, data.PlayerName);

            if(item == "{STEAMAUTH}" && message.Contains(item, StringComparison.OrdinalIgnoreCase))
                message = message.Replace(item, data.SteamAuth);

            if(item == "{PLAYERIP}" && message.Contains(item, StringComparison.OrdinalIgnoreCase))
                message = message.Replace(item, data.IPAddress);

            if(item == "{COUNTRY}" && message.Contains(item, StringComparison.OrdinalIgnoreCase))
                message = message.Replace(item, data.Country);
        }

        return message;
    }

    public string GetCountry(string ipAddress)
    {
        var geo = Path.Combine(ModuleDirectory, "GeoLite2-City.mmdb");

        if(!File.Exists(geo))
            return "Unknown Country";

        using (var reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-City.mmdb")))
        {
            var response = reader.TryCity(ipAddress, out var city);

            if(!response)
                return "Unknown Country";

            if(city?.Country.Name == null)
                return "Unknown Country";

            return city.Country.Name;
        }
    }

    public static Dictionary<string, string> ColorTag = new Dictionary<string, string>
    {
        { "{DEFAULT}", "\x01" },
        { "{DARKRED}", "\x02" },
        { "{PURPLE}", "\x03" },
        { "{GREEN}", "\x04" },
        { "{OLIVE}", "\x05" },
        { "{LIME}", "\x06" },
        { "{RED}", "\x07" },
        { "{GREY}", "\x08" },
        { "{YELLOW}", "\x09" },
        { "{GOLD}", "\x10" },
        { "{ORANGE}", "\x10" },
        { "{SILVER}", "\x0A" },
        { "{BLUE}", "\x0B" },
        { "{DARKBLUE}", "\x0C" },
        { "{ORCHID}", "\x0E" },
        { "{MAGENTA}", "\x0E" },
        { "{LIGHTRED}", "\x0F" },
    };

    public static List<string> SpecialTag = new List<string> {
        "{PLAYERNAME}", "{STEAMAUTH}", "{PLAYERIP}", "{COUNTRY}"
    };
}
