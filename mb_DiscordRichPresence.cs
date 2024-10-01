using DiscordRPC;
using DiscordRPC.Logging;
using EpikLastFMApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public class CurrentSongInfo
    {
        public string Artist { get; set; }
        public string Track { get; set; }
        public string Album { get; set; }
        public bool Playing { get; set; }
        public int Index { get; set; }
        public int TotalTracks { get; set; }
        public string ImageUrl { get; set; }
        public string YearStr { get; set; }
        public string Url { get; set; }
    }

    public partial class Plugin
    {
        public const string ImageSize = "medium"; // small, medium, large, extralarge, mega

        private readonly PluginInfo _about = new PluginInfo
        {
            PluginInfoVersion = PluginInfoVersion,
            Name = "Discord Rich Presence",
            Description = "Sets currently playing song as Discord Rich Presence",
            Author = "Harmon758 + Kuunikal + BriannaFoxwell + Cynosphere + yui + Invalid",
            TargetApplication = "", // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            Type = PluginType.General,
            VersionMajor = 2, // your plugin version
            VersionMinor = 0,
            Revision = 05, // this how you do it?
            MinInterfaceVersion = MinInterfaceVersion,
            MinApiRevision = MinApiRevision,
            ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents,
            ConfigurationPanelHeight = 48, // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
        };
        
        private readonly DiscordRpcClient _rpcClient = new DiscordRpcClient("519949979176140821")
        {
            Logger = new ConsoleLogger { Level = LogLevel.Warning },
        };

        private readonly LastFMApi _fmApi = new LastFMApi("cba04ed41dff8bfb9c10835ee747ba94"); // LastFM Api key taken from MusicBee

        private readonly Dictionary<string, string> _albumArtCache = new Dictionary<string, string>();

        private MusicBeeApiInterface _mbApi;
        private Plugin.Configuration _config = new Plugin.Configuration();
        private Plugin.Configuration _newConfig = new Plugin.Configuration();

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _mbApi = new MusicBeeApiInterface();
            _mbApi.Initialise(apiInterfacePtr);

            if (!_rpcClient.IsInitialized)
                _rpcClient.Initialize();

            return _about;
        }

        private async Task FetchArtAsync(string track, string artist, string albumArtist, string album)
        {
            string key = $"{albumArtist}_{album}";

            if (!_albumArtCache.ContainsKey(key))
            {
                string mainArtist = albumArtist.Split(new[] { ", ", "; " }, StringSplitOptions.None)[0];

                string url = await _fmApi.AlbumGetInfoAsync(AlbumGetInfo_FindAlbumImg, album, mainArtist);

                if (string.IsNullOrEmpty(url))
                    url = await _fmApi.AlbumGetInfoAsync(AlbumGetInfo_FindAlbumImg, album, albumArtist);

                if (string.IsNullOrEmpty(url))
                    url = await _fmApi.AlbumGetInfoAsync(AlbumGetInfo_FindAlbumImg, album, artist, track);

                if (string.IsNullOrEmpty(url))
                    url = await _fmApi.AlbumSearchAsync(AlbumSearch_FindAlbumImg, album, mainArtist);

                if (string.IsNullOrEmpty(url))
                    url = await _fmApi.AlbumSearchAsync(AlbumSearch_FindAlbumImg, album);

                if (string.IsNullOrEmpty(url))
                    _albumArtCache.Add(key, "unknown");
                else
                    _albumArtCache.Add(key, url);
            }
        }

        private string AlbumSearch_FindAlbumImg(JObject json, string artistRequest, string albumRequest)
        {
            Dictionary<string, string> imageList = new Dictionary<string, string>();
            JArray albums = (json as dynamic).results.albummatches.album;

            foreach (dynamic album in albums)
            {
                string artist = album.artist;
                bool artistUnknown = string.IsNullOrWhiteSpace(artistRequest) || string.IsNullOrWhiteSpace(artist);
                bool isVarious = artistRequest.ToLower() == "va" || artistRequest.ToLower() == "various artists";

                if (artist.ToLower() == artistRequest.ToLower() || artistUnknown || isVarious)
                {
                    string name = album.name;
                    JArray images = album.image;

                    bool foundAlbum = name == albumRequest
                        || name.ToLower() == albumRequest.ToLower()
                        || name.ToLower().Replace(" ", "") == albumRequest.ToLower().Replace(" ", "");
                    bool foundArtist = artist.ToLower() == artistRequest.ToLower();

                    if (foundAlbum || foundArtist || (isVarious && foundAlbum))
                    {
                        foreach (dynamic image in images)
                        {
                            string url = image["#text"];
                            string size = image["size"];
                            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(size))
                                imageList.Add(size, url);
                        }
                        if (imageList.Count > 0)
                            break;
                    }
                }
            }

            if (imageList.Count == 0)
                return null;

            return imageList.ContainsKey(ImageSize) ? imageList[ImageSize] : imageList.Values.Last();
        }

        private string AlbumGetInfo_FindAlbumImg(JObject json)
        {
            Dictionary<string, string> imageList = new Dictionary<string, string>();
            JArray images = (json as dynamic).album.image;

            foreach (dynamic image in images)
            {
                string url = image["#text"];
                string size = image["size"];
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(size))
                    imageList.Add(size, url);
            }

            if (imageList.Count == 0)
                return null;

            return imageList.ContainsKey(ImageSize) ? imageList[ImageSize] : imageList.Values.Last();
        }

        private void UpdatePresence(CurrentSongInfo songInfo)
        {
            string year = null;

            if (_config.ShowYear && songInfo.YearStr.Length > 0)
            {
                if (DateTime.TryParse(songInfo.YearStr, out DateTime result))
                    year = result.Year.ToString();
                else
                    if (songInfo.YearStr.Length == 4)
                        if (DateTime.TryParseExact(songInfo.YearStr, "yyyy", null, System.Globalization.DateTimeStyles.None, out result))
                            year = result.Year.ToString();
            }

            string bitrate = _mbApi.NowPlaying_GetFileProperty(FilePropertyType.Bitrate);
            string codec = _mbApi.NowPlaying_GetFileProperty(FilePropertyType.Kind);

            RichPresence presence = new RichPresence
            {
                State = $"by {songInfo.Artist}",
                Details = $"{songInfo.Track} [{_mbApi.NowPlaying_GetFileProperty(FilePropertyType.Duration)}]",
                Type = ActivityType.Listening,
                Party = new Party
                {
                    Size = songInfo.Index,
                    Max = songInfo.TotalTracks,
                },
                Timestamps = new Timestamps(),
                Assets = new Assets
                {
                    LargeImageKey = songInfo.ImageUrl != "" && songInfo.ImageUrl != "unknown" ? songInfo.ImageUrl : "albumart",
                    LargeImageText = $"{songInfo.Album}" + ((year != null && _config.ShowYear) ? $" ({year})" : ""),
                    SmallImageKey = songInfo.Playing ? "playing" : "paused",
                    SmallImageText = $"{bitrate.Replace("k", "kbps")} [{codec}]",
                },
            };

            long now = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            long duration = _mbApi.NowPlaying_GetDuration() / 1000;
            long end = now + duration;

            if (songInfo.Playing)
            {
                long pos = _mbApi.Player_GetPosition() / 1000;
                presence.Timestamps.Start = new DateTime(1970, 1, 1).AddSeconds(now - pos);

                if (duration != -1)
                    presence.Timestamps.End = new DateTime(1970, 1, 1).AddSeconds(end - pos);

                if (songInfo.Url.StartsWith("http"))
                {
                    presence.Buttons = new[]
                    {
                        new DiscordRPC.Button
                        {
                            Label = "Listen to stream",
                            Url = songInfo.Url,
                        }
                    };
                }
            }

            _rpcClient.SetPresence(presence);
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = _mbApi.Setting_GetPersistentStoragePath();
            // panelHandle will only be set if you set about.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if about.ConfigurationPanelHeight is set to 0, you can display your own popup window
            if (panelHandle != IntPtr.Zero)
            {
                _newConfig = (Configuration) _config.Clone();

                Panel configPanel = (Panel) Panel.FromHandle(panelHandle);

                CheckBox showYear = new CheckBox
                {
                    Text = "Show year next to album",
                    Height = 16,
                    ForeColor = Color.FromArgb(
                        _mbApi.Setting_GetSkinElementColour(
                            SkinElement.SkinInputPanelLabel, ElementState.ElementStateDefault, ElementComponent.ComponentForeground
                        )
                    ),
                    Checked = _newConfig.ShowYear,
                };
                showYear.CheckedChanged += (sender, _args) =>  _newConfig.ShowYear = (sender as CheckBox).Checked;

                /*
                CheckBox showPausedTime = new CheckBox
                {
                    Text = "Show time when paused",
                    Height = 16,
                    ForeColor = Color.FromArgb(
                        _mbApi.Setting_GetSkinElementColour(
                            SkinElement.SkinInputPanelLabel, ElementState.ElementStateDefault, ElementComponent.ComponentForeground
                        )
                    ),
                    Checked = _newConfig.ShowPausedTime,
                };
                showPausedTime.CheckedChanged += (sender, _args) =>  _newConfig.ShowPausedTime = (sender as CheckBox).Checked;
                */

                Label customArtworkUrlLabel = new Label
                {
                    Height = 16,
                    Width = 128,
                    ForeColor = Color.FromArgb(
                        _mbApi.Setting_GetSkinElementColour(
                            SkinElement.SkinInputPanelLabel, ElementState.ElementStateDefault, ElementComponent.ComponentForeground
                        )
                    ),
                    Text = "Custom Artwork URL",
                    Top = 24,
                    TextAlign = ContentAlignment.MiddleLeft,
                };

                TextBox customArtworkUrl = (TextBox) _mbApi.MB_AddPanel(configPanel, PluginPanelDock.TextBox);
                customArtworkUrl.Height = 16;
                customArtworkUrl.Width = 192;
                customArtworkUrl.ForeColor = Color.FromArgb(
                    _mbApi.Setting_GetSkinElementColour(
                        SkinElement.SkinInputPanelLabel, ElementState.ElementStateDefault, ElementComponent.ComponentForeground
                    )
                );
                customArtworkUrl.Text = _newConfig.CustomArtworkUrl;
                customArtworkUrl.Top = 24;
                customArtworkUrl.Left = customArtworkUrlLabel.Width;
                customArtworkUrl.TextChanged += (sender, _args) => _newConfig.CustomArtworkUrl = (sender as TextBox).Text;

                configPanel.Controls.AddRange(new Control[] { showYear, customArtworkUrlLabel, customArtworkUrl });
            }
            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = _mbApi.Setting_GetPersistentStoragePath();
            _config = _newConfig;
            SaveConfig(dataPath);
        }

        private void SaveConfig(string dataPath)
        {
            DataContractSerializer dataContractSerializer = new DataContractSerializer(typeof(Plugin.Configuration));
            FileStream fileStream = new FileStream(Path.Combine(dataPath, "mb_DiscordRichPresence.xml"), FileMode.Create);
            dataContractSerializer.WriteObject(fileStream, _config);
            fileStream.Close();
        }

        private void LoadConfig(string dataPath)
        {
            DataContractSerializer dataContractSerializer = new DataContractSerializer(typeof(Plugin.Configuration));
            FileStream fileStream = new FileStream(Path.Combine(dataPath, "mb_DiscordRichPresence.xml"), FileMode.Open);
            _config = (Plugin.Configuration) dataContractSerializer.ReadObject(fileStream);
            fileStream.Close();
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            _rpcClient.ClearPresence();
            _rpcClient.Dispose();
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        { }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            string bitrate = _mbApi.NowPlaying_GetFileProperty(FilePropertyType.Bitrate);
            string artist = _mbApi.NowPlaying_GetFileTag(MetaDataType.Artist);
            string albumArtist = _mbApi.NowPlaying_GetFileTag(MetaDataType.AlbumArtist);
            string trackTitle = _mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
            string album = _mbApi.NowPlaying_GetFileTag(MetaDataType.Album);
            string year = _mbApi.NowPlaying_GetFileTag(MetaDataType.Year);
            string url = _mbApi.NowPlaying_GetFileUrl();
            int position = _mbApi.Player_GetPosition();

            string originalArtist = artist;

            string[] tracks = null;
            _mbApi.NowPlayingList_QueryFilesEx(null, ref tracks);
            int index = Array.IndexOf(tracks, url);

            // Check if there isn't an artist for the current song. If so, replace it with "(unknown artist)".
            if (string.IsNullOrEmpty(artist))
            {
                if (!string.IsNullOrEmpty(albumArtist))
                    artist = albumArtist;
                else
                    artist = "(unknown artist)";
            }

            if (artist.Length > 128)
            {
                if (!string.IsNullOrEmpty(albumArtist) && albumArtist.Length <= 128)
                    artist = albumArtist;
                else
                    artist = artist.Substring(0, 122) + "...";
            }

            if (type == NotificationType.PluginStartup)
                LoadConfig(_mbApi.Setting_GetPersistentStoragePath());

            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                case NotificationType.PlayStateChanged:
                case NotificationType.TrackChanged:
                    bool isPlaying = _mbApi.Player_GetPlayState() == PlayState.Playing;

                    Task.Run(async () =>
                    {
                        try
                        {
                            string imageUrl = "";
                            if (_config.CustomArtworkUrl != "")
                                imageUrl = _config.CustomArtworkUrl + "?" + (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                            else
                            {
                                await FetchArtAsync(trackTitle, originalArtist, albumArtist, album);

                                imageUrl = _albumArtCache[$"{albumArtist}_{album}"];
                            }

                            UpdatePresence(new CurrentSongInfo
                            {
                                Artist = artist,
                                Track = trackTitle,
                                Album = album,
                                Playing = isPlaying,
                                Index = index + 1,
                                TotalTracks = tracks.Length,
                                ImageUrl = imageUrl,
                                YearStr = year,
                                Url = url,
                            });
                        }
                        catch (Exception err)
                        {
                            Console.WriteLine(err);
                        }
                    });
                    break;
            }
        }

        public class Configuration : ICloneable
        {
            public bool ShowYear { get; set; } = true;
            public string CustomArtworkUrl { get; set; } = "";
            //public bool ShowPausedTime { get; set; } = false;

            public object Clone() => MemberwiseClone();
        }
    }
}
