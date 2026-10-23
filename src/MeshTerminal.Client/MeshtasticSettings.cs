using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace MeshTerminal.Client
{
    public enum MeshtasticTransportType { Serial, Tcp, Bluetooth }
    public enum MeshProtocol { Meshtastic, MeshCore }

    /// <summary>Connection-related settings. Add further settings classes/properties to MeshtasticApplicationSettings as the application grows.</summary>
    public sealed class MeshtasticConnectionSettings
    {
        public MeshProtocol Protocol { get; set; }
        public MeshtasticTransportType Transport { get; set; }
        public string SerialPort { get; set; }
        public int SerialBaudRate { get; set; }
        public bool SerialDtrEnable { get; set; }
        public bool SerialRtsEnable { get; set; }
        public string BluetoothDeviceName { get; set; }
        public string BluetoothDeviceId { get; set; }
        public string TcpHost { get; set; }
        public int TcpPort { get; set; }
        public bool AutomaticReconnect { get; set; }
        public int ReconnectIntervalSeconds { get; set; }

        public MeshtasticConnectionSettings()
        {
            Transport = MeshtasticTransportType.Serial;
            SerialPort = Environment.OSVersion.Platform == PlatformID.Win32NT ? "COM5" : "/dev/ttyACM0";
            SerialBaudRate = 115200;
            SerialDtrEnable = true;
            SerialRtsEnable = false;
            BluetoothDeviceId = "";
            TcpHost = "192.168.1.1";
            TcpPort = 4403;
            AutomaticReconnect = true;
            ReconnectIntervalSeconds = 300;
        }
    }

    /// <summary>Terminal color names used by the MeshTerminal appearance settings.</summary>
    public sealed class MeshtasticAppearanceSettings
    {
        public string BackgroundColor { get; set; }
        public string FrameColor { get; set; }
        public string TextColor { get; set; }
        public string SelectedItemColor { get; set; }
        public string SentMessageColor { get; set; }
        public string ReceivedMessageColor { get; set; }
        public string StatusTextColor { get; set; }
        public string StatusBackgroundColor { get; set; }
        public string InputBackgroundColor { get; set; }
        public string InputTextColor { get; set; }
        public string MenuBackgroundColor { get; set; }
        public string MenuTextColor { get; set; }
        public string ButtonBackgroundColor { get; set; }
        public string ButtonTextColor { get; set; }
        public string PageFrameColor { get; set; }
        public string LogoFrameColor { get; set; }
        public string LogoTextColor { get; set; }
        public string EmojiTextColor { get; set; }
        public string MapNodeColor { get; set; }
        public string MapClusterColor { get; set; }
        public string MapGridColor { get; set; }
        public string DaySeparatorColor { get; set; }

        public MeshtasticAppearanceSettings()
        {
            BackgroundColor = "Black"; FrameColor = "Magenta"; TextColor = "Gray"; SelectedItemColor = "BrightYellow";
            SentMessageColor = "BrightYellow"; ReceivedMessageColor = "BrightCyan"; StatusTextColor = "White"; StatusBackgroundColor = "Black";
            InputBackgroundColor = "Black"; InputTextColor = "White"; MenuBackgroundColor = "Blue"; MenuTextColor = "White";
            ButtonBackgroundColor = "Black"; ButtonTextColor = "BrightCyan"; PageFrameColor = "Magenta"; LogoFrameColor = "Magenta"; LogoTextColor = "Green"; EmojiTextColor = "BrightMagenta"; MapNodeColor = "BrightCyan"; MapClusterColor = "BrightMagenta"; MapGridColor = "DarkGray"; DaySeparatorColor = "DarkGray";
        }
    }

    /// <summary>Controls the optional rotating ASCII logo in the MeshTerminal chat view.</summary>
    public sealed class MeshtasticLogoSettings
    {
        public bool ShowLogo { get; set; }
        public bool ShowFrame { get; set; }
        public bool AutomaticRotation { get; set; }
        public bool ShowSelectedEmoji { get; set; }
        public bool AnimateTallLogos { get; set; }
        public bool AnimationPingPong { get; set; }
        public int RotationIntervalSeconds { get; set; }
        public int TallLogoFrameIntervalMilliseconds { get; set; }
        public int AnimationVerticalStepLines { get; set; }
        public int AnimationHorizontalStepCharacters { get; set; }
        public int InnerWidth { get; set; }
        public int InnerHeight { get; set; }
        public string LastLogoFileName { get; set; }

        public MeshtasticLogoSettings() { ShowLogo = true; ShowFrame = false; AutomaticRotation = false; AnimateTallLogos = true; AnimationPingPong = false; RotationIntervalSeconds = 10; TallLogoFrameIntervalMilliseconds = 250; AnimationVerticalStepLines = 8; AnimationHorizontalStepCharacters = 8; InnerWidth = 32; InnerHeight = 8; LastLogoFileName = "107-meshtastic-animation-06.txt"; }
    }

    /// <summary>Defines one command-triggered local chat bot.</summary>
    public class MeshtasticChatBotSettings
    {
        public MeshProtocol Protocol { get; set; }
        public bool Enabled { get; set; }
        public string Command { get; set; }
        public bool CaseSensitive { get; set; }
        public string ExecutablePath { get; set; }
        public int MaximumReplyLength { get; set; }
        public int MaximumParameterCount { get; set; }
        public string ArgumentPrefix { get; set; }
        public string ArgumentSuffix { get; set; }
        public bool ReactToDirectMessages { get; set; }
        public bool FavoritesOnly { get; set; }
        [XmlIgnore]
        public List<int> ReactToChannels { get; set; }

        // XmlSerializer populates List<T> properties instead of replacing lists that
        // were initialized by the constructor. Use an array as the XML-facing value
        // so an explicitly empty selection replaces the default list of all channels.
        [XmlArray("ReactToChannels")]
        [XmlArrayItem("int")]
        public int[] SerializedReactToChannels
        {
            get { return ReactToChannels == null ? null : ReactToChannels.ToArray(); }
            set { ReactToChannels = value == null ? null : value.ToList(); }
        }

        public MeshtasticChatBotSettings()
        {
            Enabled = true; Command = ""; CaseSensitive = false; ExecutablePath = ""; MaximumReplyLength = 200; MaximumParameterCount = 10; ArgumentPrefix = ""; ArgumentSuffix = ""; ReactToDirectMessages = true; FavoritesOnly = false; ReactToChannels = new List<int>();
        }
    }

    /// <summary>Connects one mesh channel to one Telegram chat through a bot token.</summary>
    public sealed class TelegramGatewaySettings
    {
        public string Name { get; set; }
        public MeshProtocol Protocol { get; set; }
        public int ChannelIndex { get; set; }
        public bool Enabled { get; set; }
        public bool ForwardMeshtasticToTelegram { get; set; }
        public bool ForwardTelegramToMeshtastic { get; set; }
        public string BotToken { get; set; }
        public long ChatId { get; set; }

        public TelegramGatewaySettings() { BotToken = ""; ForwardMeshtasticToTelegram = true; ForwardTelegramToMeshtastic = true; }
    }

    public enum TelnetEmojiArtSize { Small, Large }
    public sealed class TelnetServerSettings
    {
        public string Name { get; set; }
        public MeshProtocol Protocol { get; set; }
        public bool Enabled { get; set; }
        public int ChannelIndex { get; set; }
        public int Port { get; set; }
        public string BindAddress { get; set; }
        public bool SendWelcome { get; set; }
        public string WelcomeMessage { get; set; }
        public int HistoryCount { get; set; }
        public bool ShowTimestamps { get; set; }
        public bool ShowSender { get; set; }
        public bool ReadOnly { get; set; }
        public bool EchoInput { get; set; }
        public bool SendEmojiArt { get; set; }
        public TelnetEmojiArtSize EmojiArtSize { get; set; }
        public bool ReplaceEmojisWithNames { get; set; }
        public bool ShowOwnMessages { get; set; }
        public bool SendBell { get; set; }
        public bool RequirePassword { get; set; }
        public string PasswordHash { get; set; }
        public TelnetServerSettings() { Name = "Telnet"; Port = 7023; BindAddress = "127.0.0.1"; SendWelcome = true; WelcomeMessage = "Welcome to {channel}"; HistoryCount = 10; ShowTimestamps = true; ShowSender = true; ShowOwnMessages = true; PasswordHash = ""; }
    }
    public sealed class MeshtasticHttpBotParameter { public string Name { get; set; } public string Value { get; set; } public MeshtasticHttpBotParameter() { Name = ""; Value = ""; } }
    public sealed class MeshtasticHttpBotSettings : MeshtasticChatBotSettings
    {
        public string Url { get; set; }
        public List<MeshtasticHttpBotParameter> QueryParameters { get; set; }
        public MeshtasticHttpBotSettings() { Url = ""; QueryParameters = new List<MeshtasticHttpBotParameter>(); for (var i = 0; i < 5; i++) QueryParameters.Add(new MeshtasticHttpBotParameter()); }
    }

    /// <summary>Actions that notify the local operating system about unread incoming messages.</summary>
    public sealed class MeshtasticAlertSettings
    {
        public bool EnableWindowsBeep { get; set; }
        public bool EnableTerminalBell { get; set; }
        public bool EnableDesktopNotifications { get; set; }
        public bool SuppressMapMessageAlerts { get; set; }
        public bool BlinkLogoForUnreadMessages { get; set; }
        public int RepeatBeepIntervalSeconds { get; set; }
        public bool EnableSoundCooldown { get; set; }
        public int SoundCooldownSeconds { get; set; }
        public bool EnableHttpGet { get; set; }
        public string HttpGetUrl { get; set; }
        public bool EnableExecutable { get; set; }
        public string ExecutablePath { get; set; }

        public MeshtasticAlertSettings()
        {
            EnableWindowsBeep = true; EnableTerminalBell = true; EnableDesktopNotifications = true; BlinkLogoForUnreadMessages = false; RepeatBeepIntervalSeconds = 0; EnableSoundCooldown = true; SoundCooldownSeconds = 5; HttpGetUrl = ""; ExecutablePath = "";
        }
    }

    public sealed class MeshtasticNodeListSettings
    {
        public bool FavoritesOnly { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public string SearchText { get; set; }
        public string SortMode { get; set; }
        public bool SortAscending { get; set; }
        public MeshtasticNodeListSettings() { SearchText = ""; SortMode = "Name"; SortAscending = true; }
    }
    public sealed class MeshtasticMapSettings
    {
        public double? CenterLatitude { get; set; }
        public double? CenterLongitude { get; set; }
        public double MetersPerRow { get; set; }
        public List<string> ActiveOverlayFiles { get; set; }
        public bool ShowKnownNodes { get; set; }
        public bool ShowBackgroundMap { get; set; }
        public bool HopLimitEnabled { get; set; }
        public int HopLimit { get; set; }
        public bool NodeAgeLimitEnabled { get; set; }
        public double NodeAgeLimitHours { get; set; } = 24;
        public string PositionMarkerColor { get; set; } = "BrightYellow";
        public string PositionMarkerSymbol { get; set; } = "+";
        public string PositionMarkerCallSign { get; set; } = "";
        public string PositionMarkerInfo { get; set; } = "";
        public bool ReceivePositionMarkers { get; set; } = true;
        public bool ConfirmPositionMarkerPlacement { get; set; } = true;
        public bool PositionMarkerUseGps { get; set; }
        public bool MapPointSharingEnabled { get; set; }
        public bool ReceiveSharedMapPointDeletions { get; set; }
        public bool CenterMapOnReceivedSharedPoint { get; set; }
        public string MapPointSharingOverlayFile { get; set; }
        public int MapPointSharingChannelIndex { get; set; }
        public uint? PositionTrackNodeNumber { get; set; }
        public bool PositionTrackVisible { get; set; }
        public int PositionTrackFromHoursAgo { get; set; }
        public int PositionTrackToHoursAgo { get; set; }
        public string PositionTrackColor { get; set; }
        public string PositionTrackSymbol { get; set; }
        public MeshtasticMapSettings() { MetersPerRow = 1000d; ShowKnownNodes = true; ShowBackgroundMap = true; HopLimit = 0; ReceiveSharedMapPointDeletions = true; MapPointSharingOverlayFile = ""; MapPointSharingChannelIndex = 0; PositionTrackVisible = true; PositionTrackFromHoursAgo = 0; PositionTrackToHoursAgo = 24; PositionTrackColor = "BrightYellow"; PositionTrackSymbol = "O"; }
    }
    /// <summary>Root object for a readable, application-owned settings file.</summary>
    [XmlRoot("MeshtasticApplicationSettings")]
    public sealed class MeshtasticApplicationSettings
    {
        public string[] RepeaterQuickCommands { get; set; } = new string[] { "ver", "board", "get name", "get radio", "get tx", "clock" };
        public List<RepeaterCredential> RepeaterCredentials { get; set; } = new List<RepeaterCredential>();
        public List<LocalChannel> LocalChannels { get; set; } = new List<LocalChannel>();
        public List<ChannelDraft> ChannelDrafts { get; set; } = new List<ChannelDraft>();
        public MeshtasticConnectionSettings Connection { get; set; }
        public bool EnableNewMessageBeep { get; set; }
        public bool StoreTelemetryData { get; set; }
        public bool StoreTelemetryFavoritesOnly { get; set; }
        public bool StorePositionData { get; set; }
        public bool StorePositionFavoritesOnly { get; set; }
        public bool EnableSerialTrafficLog { get; set; }
        public bool CommunicationLogLoRaOnly { get; set; }
        public int CommunicationLogCapacity { get; set; } = 500;
        public MeshtasticAlertSettings Alerts { get; set; }
        public MeshtasticAppearanceSettings Appearance { get; set; }
        public MeshtasticLogoSettings Logo { get; set; }
        public MeshtasticMapSettings Map { get; set; }
        public MeshtasticNodeListSettings Nodes { get; set; }
        public List<MeshtasticChatBotSettings> ChatBots { get; set; }
        public List<TelegramGatewaySettings> TelegramGateways { get; set; }
        public List<MeshtasticHttpBotSettings> HttpBots { get; set; }
        public List<TelnetServerSettings> TelnetServers { get; set; }

        public MeshtasticApplicationSettings()
        {
            Connection = new MeshtasticConnectionSettings(); Appearance = new MeshtasticAppearanceSettings(); Logo = new MeshtasticLogoSettings(); Map = new MeshtasticMapSettings(); Nodes = new MeshtasticNodeListSettings(); Alerts = new MeshtasticAlertSettings(); ChatBots = new List<MeshtasticChatBotSettings>(); HttpBots = new List<MeshtasticHttpBotSettings>(); TelnetServers = new List<TelnetServerSettings>(); TelegramGateways = new List<TelegramGatewaySettings>(); EnableNewMessageBeep = true; StoreTelemetryData = true; StorePositionData = true;
        }
    }

    public static class MeshtasticSettingsStore
    {
        public static MeshtasticApplicationSettings Load(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A settings file path is required.", "filePath");
            if (!File.Exists(filePath)) return new MeshtasticApplicationSettings();
            var serializer = new XmlSerializer(typeof(MeshtasticApplicationSettings));
            using (var stream = File.OpenRead(filePath))
            {
                var settings = serializer.Deserialize(stream) as MeshtasticApplicationSettings;
                if (settings == null) throw new InvalidDataException("The settings file does not contain MeshtasticApplicationSettings.");
                if (settings.Connection == null) settings.Connection = new MeshtasticConnectionSettings();
                if (settings.Appearance == null) settings.Appearance = new MeshtasticAppearanceSettings();
                NormalizeAppearance(settings.Appearance);
                if (settings.Logo == null) settings.Logo = new MeshtasticLogoSettings();
                if (settings.Map == null) settings.Map = new MeshtasticMapSettings();
                if (settings.Map.HopLimit < 0) settings.Map.HopLimit = 0;
            if (Double.IsNaN(settings.Map.NodeAgeLimitHours) || Double.IsInfinity(settings.Map.NodeAgeLimitHours) || settings.Map.NodeAgeLimitHours <= 0 || settings.Map.NodeAgeLimitHours > 876000) settings.Map.NodeAgeLimitHours = 24;
                if (String.IsNullOrWhiteSpace(settings.Map.PositionTrackColor)) settings.Map.PositionTrackColor = "BrightYellow";
                if (String.IsNullOrWhiteSpace(settings.Map.PositionTrackSymbol)) settings.Map.PositionTrackSymbol = "O";
                if (settings.Map.PositionTrackFromHoursAgo < 0) settings.Map.PositionTrackFromHoursAgo = 0;
                if (settings.Map.PositionTrackToHoursAgo <= settings.Map.PositionTrackFromHoursAgo) settings.Map.PositionTrackToHoursAgo = settings.Map.PositionTrackFromHoursAgo + 24;
                if (settings.Nodes == null) settings.Nodes = new MeshtasticNodeListSettings();
                if (settings.Alerts == null) settings.Alerts = new MeshtasticAlertSettings();
                if (settings.Alerts.RepeatBeepIntervalSeconds < 0) settings.Alerts.RepeatBeepIntervalSeconds = 0;
            if (settings.Alerts.SoundCooldownSeconds < 1 || settings.Alerts.SoundCooldownSeconds > 86400) settings.Alerts.SoundCooldownSeconds = 5;
                if (settings.Logo.RotationIntervalSeconds < 1) settings.Logo.RotationIntervalSeconds = 1;
                if (settings.Logo.TallLogoFrameIntervalMilliseconds < 50) settings.Logo.TallLogoFrameIntervalMilliseconds = 250;
                if (settings.Logo.AnimationVerticalStepLines < 1) settings.Logo.AnimationVerticalStepLines = 8;
                if (settings.Logo.AnimationHorizontalStepCharacters < 1) settings.Logo.AnimationHorizontalStepCharacters = 8;
                if (settings.Logo.InnerWidth < 20) settings.Logo.InnerWidth = 20;
                if (settings.Logo.InnerHeight < 1) settings.Logo.InnerHeight = 1;
                if (settings.ChatBots == null) settings.ChatBots = new List<MeshtasticChatBotSettings>();
                NormalizeChatBots(settings);
                if (settings.TelnetServers == null) settings.TelnetServers = new List<TelnetServerSettings>();
            if (settings.HttpBots == null) settings.HttpBots = new List<MeshtasticHttpBotSettings>(); NormalizeHttpBots(settings);
                EnsureTelegramGateways(settings);
                Validate(settings);
                return settings;
            }
        }

        public static void Save(string filePath, MeshtasticApplicationSettings settings)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A settings file path is required.", "filePath");
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.Connection == null) settings.Connection = new MeshtasticConnectionSettings();
            if (settings.Appearance == null) settings.Appearance = new MeshtasticAppearanceSettings();
            NormalizeAppearance(settings.Appearance);
            if (settings.Logo == null) settings.Logo = new MeshtasticLogoSettings();
            if (settings.Map == null) settings.Map = new MeshtasticMapSettings();
            if (settings.Map.HopLimit < 0) settings.Map.HopLimit = 0;
            if (Double.IsNaN(settings.Map.NodeAgeLimitHours) || Double.IsInfinity(settings.Map.NodeAgeLimitHours) || settings.Map.NodeAgeLimitHours <= 0 || settings.Map.NodeAgeLimitHours > 876000) settings.Map.NodeAgeLimitHours = 24;
            if (String.IsNullOrWhiteSpace(settings.Map.PositionTrackColor)) settings.Map.PositionTrackColor = "BrightYellow";
            if (String.IsNullOrWhiteSpace(settings.Map.PositionTrackSymbol)) settings.Map.PositionTrackSymbol = "O";
            if (settings.Map.PositionTrackFromHoursAgo < 0) settings.Map.PositionTrackFromHoursAgo = 0;
            if (settings.Map.PositionTrackToHoursAgo <= settings.Map.PositionTrackFromHoursAgo) settings.Map.PositionTrackToHoursAgo = settings.Map.PositionTrackFromHoursAgo + 24;
            if (settings.Nodes == null) settings.Nodes = new MeshtasticNodeListSettings();
            if (settings.Alerts == null) settings.Alerts = new MeshtasticAlertSettings();
            if (settings.Alerts.RepeatBeepIntervalSeconds < 0) settings.Alerts.RepeatBeepIntervalSeconds = 0;
            if (settings.Alerts.SoundCooldownSeconds < 1 || settings.Alerts.SoundCooldownSeconds > 86400) settings.Alerts.SoundCooldownSeconds = 5;
            if (settings.Logo.RotationIntervalSeconds < 1) settings.Logo.RotationIntervalSeconds = 1;
            if (settings.Logo.TallLogoFrameIntervalMilliseconds < 50) settings.Logo.TallLogoFrameIntervalMilliseconds = 250;
            if (settings.Logo.AnimationVerticalStepLines < 1) settings.Logo.AnimationVerticalStepLines = 8;
            if (settings.Logo.AnimationHorizontalStepCharacters < 1) settings.Logo.AnimationHorizontalStepCharacters = 8;
            if (settings.Logo.InnerWidth < 20) settings.Logo.InnerWidth = 20;
            if (settings.Logo.InnerHeight < 1) settings.Logo.InnerHeight = 1;
            if (settings.ChatBots == null) settings.ChatBots = new List<MeshtasticChatBotSettings>();
            NormalizeChatBots(settings);
            if (settings.TelnetServers == null) settings.TelnetServers = new List<TelnetServerSettings>();
            if (settings.HttpBots == null) settings.HttpBots = new List<MeshtasticHttpBotSettings>(); NormalizeHttpBots(settings);
            EnsureTelegramGateways(settings);
            Validate(settings);
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var serializer = new XmlSerializer(typeof(MeshtasticApplicationSettings));
            var xml = new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(filePath, xml)) serializer.Serialize(writer, settings);
        }

        private static void Validate(MeshtasticApplicationSettings settings)
        {
            var connection = settings.Connection;
            if (connection.Transport == MeshtasticTransportType.Serial && String.IsNullOrWhiteSpace(connection.SerialPort)) throw new InvalidDataException("SerialPort is required for a serial connection.");
            if (connection.Transport == MeshtasticTransportType.Tcp && String.IsNullOrWhiteSpace(connection.TcpHost)) throw new InvalidDataException("TcpHost is required for a TCP connection.");
            if (connection.SerialBaudRate <= 0) throw new InvalidDataException("SerialBaudRate must be positive.");
            if (connection.TcpPort < 1 || connection.TcpPort > 65535) throw new InvalidDataException("TcpPort must be between 1 and 65535.");
            if (connection.ReconnectIntervalSeconds < 1 || connection.ReconnectIntervalSeconds > 86400) throw new InvalidDataException("ReconnectIntervalSeconds must be between 1 and 86400.");
        }

        private static void NormalizeAppearance(MeshtasticAppearanceSettings appearance)
        {
            if (String.IsNullOrWhiteSpace(appearance.SelectedItemColor)) appearance.SelectedItemColor = "BrightYellow";
            if (String.IsNullOrWhiteSpace(appearance.StatusBackgroundColor)) appearance.StatusBackgroundColor = "Black";
            if (String.IsNullOrWhiteSpace(appearance.LogoFrameColor)) appearance.LogoFrameColor = appearance.FrameColor ?? "Gray";
            if (String.IsNullOrWhiteSpace(appearance.LogoTextColor)) appearance.LogoTextColor = "BrightCyan";
            if (String.IsNullOrWhiteSpace(appearance.EmojiTextColor)) appearance.EmojiTextColor = "BrightMagenta";
            if (String.IsNullOrWhiteSpace(appearance.MapNodeColor)) appearance.MapNodeColor = "BrightCyan";
            if (String.IsNullOrWhiteSpace(appearance.MapClusterColor)) appearance.MapClusterColor = "BrightMagenta";
            if (String.IsNullOrWhiteSpace(appearance.MapGridColor)) appearance.MapGridColor = "DarkGray";
            if (String.IsNullOrWhiteSpace(appearance.DaySeparatorColor)) appearance.DaySeparatorColor = "DarkGray";
        }

        private static void EnsureTelegramGateways(MeshtasticApplicationSettings settings)
        {
            if (settings.TelegramGateways == null) settings.TelegramGateways = new List<TelegramGatewaySettings>();
            // Older configuration saves could contain duplicate channel entries. Keep the
            // configured/enabled entry and normalize the list to exactly one entry per channel.
            settings.TelegramGateways = settings.TelegramGateways
                .Where(gateway => gateway != null && gateway.ChannelIndex >= 0 && ((gateway.Protocol == MeshProtocol.Meshtastic && gateway.ChannelIndex < 8) || (gateway.Protocol == MeshProtocol.MeshCore && gateway.ChannelIndex < 256)))
                .Where(gateway => gateway.Enabled || !String.IsNullOrWhiteSpace(gateway.Name) || !String.IsNullOrWhiteSpace(gateway.BotToken) || gateway.ChatId != 0)
                .GroupBy(gateway => new { gateway.Protocol, gateway.ChannelIndex })
                .Select(group => group.OrderByDescending(gateway => gateway.Enabled).ThenByDescending(gateway => !String.IsNullOrWhiteSpace(gateway.BotToken)).First())
                .OrderBy(gateway => gateway.ChannelIndex)
                .ToList();
            settings.TelegramGateways.Sort((first, second) => first.ChannelIndex.CompareTo(second.ChannelIndex));
        }

        private static void NormalizeChatBots(MeshtasticApplicationSettings settings)
        {
            foreach (var bot in settings.ChatBots.Where(bot => bot != null))
            {
                // Files written by older versions have no channel list; retain their former
                // behavior by enabling all channels and direct messages.
                if (bot.ReactToChannels == null) bot.ReactToChannels = Enumerable.Range(0, 8).ToList();
                bot.ReactToChannels = bot.ReactToChannels.Where(channel => channel >= 0 && channel < (bot.Protocol == MeshProtocol.MeshCore ? 256 : 8)).Distinct().OrderBy(channel => channel).ToList();
            }
        }
        private static void NormalizeHttpBots(MeshtasticApplicationSettings settings)
        {
            foreach (var bot in settings.HttpBots.Where(bot => bot != null))
            {
                if (bot.ReactToChannels == null) bot.ReactToChannels = Enumerable.Range(0, 8).ToList();
                bot.ReactToChannels = bot.ReactToChannels.Where(channel => channel >= 0 && channel < (bot.Protocol == MeshProtocol.MeshCore ? 256 : 8)).Distinct().OrderBy(channel => channel).ToList();
                if (bot.QueryParameters == null) bot.QueryParameters = new List<MeshtasticHttpBotParameter>();
                while (bot.QueryParameters.Count < 5) bot.QueryParameters.Add(new MeshtasticHttpBotParameter());
                if (bot.QueryParameters.Count > 5) bot.QueryParameters.RemoveRange(5, bot.QueryParameters.Count - 5);
            }
        }
    }
}
