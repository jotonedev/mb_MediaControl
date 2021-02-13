using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using static System.UInt32;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private const int PreviousDelay = 3000; // ms 
        private readonly PluginInfo about = new PluginInfo();

        private readonly object commandLock = new object();
        private InMemoryRandomAccessStream artworkStream;
        private SystemMediaTransportControlsDisplayUpdater displayUpdater;
        private DateTime lastPrevious;
        private MusicBeeApiInterface mbApiInterface;
        private MediaPlayer mediaPlayer;
        private MusicDisplayProperties musicProperties;
        private SystemMediaTransportControls systemMediaControls;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "Media Control";
            about.Description = "Enables MusicBee to interact with the Windows 10 Media Control overlay.";
            about.Author = "Ameer Dawood";
            about.TargetApplication = ""; //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;
            about.VersionMajor = 1; // your plugin version
            about.VersionMinor = 0;
            about.Revision = 2;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            about.ConfigurationPanelHeight =
                0; // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            systemMediaControls.IsEnabled = false;
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            switch (type)
            {
                case NotificationType.TrackChanged:
                    SetDisplayValues();
                    break;
                case NotificationType.TrackChanging:
                case NotificationType.PlayStateChanged:
                    SetPlayerState();
                    break;

                // Only on StartUp
                case NotificationType.PluginStartup:
                    PluginStartUp();
                    break;
            }
        }

        private void PluginStartUp()
        {
            // set up media player to get manual control of SystemMediaTransportControls
            mediaPlayer = new MediaPlayer();
            mediaPlayer.CommandManager.IsEnabled = false;

            // set up SystemMediaTransportControls
            systemMediaControls = mediaPlayer.SystemMediaTransportControls;
            // set flags
            systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Closed; // nothing in list to play
            systemMediaControls.IsEnabled = true; // show it
            systemMediaControls.IsPlayEnabled = true;
            systemMediaControls.IsPauseEnabled = true;
            systemMediaControls.IsStopEnabled = true;
            systemMediaControls.IsPreviousEnabled = true;
            systemMediaControls.IsNextEnabled = true;
            systemMediaControls.IsRewindEnabled = true;
            systemMediaControls.IsFastForwardEnabled = false;

            // Sync musicbee volume with Windows volume
            //mbApiInterface.Player_SetVolume() = systemMediaControls.SoundLevel

            systemMediaControls.ButtonPressed += systemMediaControls_ButtonPressed;
            systemMediaControls.PlaybackPositionChangeRequested +=
                systemMediaControls_PlaybackPositionChangeRequested;
            systemMediaControls.PlaybackRateChangeRequested += systemMediaControls_PlaybackRateChangeRequested;
            systemMediaControls.ShuffleEnabledChangeRequested +=
                systemMediaControls_ShuffleEnabledChangeRequested;
            systemMediaControls.AutoRepeatModeChangeRequested +=
                systemMediaControls_AutoRepeatModeChangeRequested;

            // setup overlay properties
            displayUpdater = systemMediaControls.DisplayUpdater;
            displayUpdater.Type = MediaPlaybackType.Music; // media type
            displayUpdater.AppMediaId = "MusicBee"; // program name
            // setup for display music properties on overlay
            musicProperties = displayUpdater.MusicProperties;
            SetDisplayValues();
        }

        private void systemMediaControls_ButtonPressed(SystemMediaTransportControls smtc,
            SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            lock (commandLock)
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                    case SystemMediaTransportControlsButton.Pause:

                        if (systemMediaControls.PlaybackStatus != MediaPlaybackStatus.Changing)
                            mbApiInterface.Player_PlayPause();

                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        mbApiInterface.Player_Stop();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        mbApiInterface.Player_PlayNextTrack();
                        break;
                    case SystemMediaTransportControlsButton.Rewind:
                    case SystemMediaTransportControlsButton.Previous:
                        if (systemMediaControls.PlaybackStatus != MediaPlaybackStatus.Changing)
                        {
                            // restart song
                            if (DateTime.Now.Subtract(lastPrevious).TotalMilliseconds > PreviousDelay)
                            {
                                mbApiInterface.Player_Stop();
                                mbApiInterface.Player_PlayPause();
                                lastPrevious = DateTime.Now;
                                break;
                            }

                            // play previous track
                            if (DateTime.Now.Subtract(lastPrevious).TotalMilliseconds < PreviousDelay)
                            {
                                mbApiInterface.Player_Stop();
                                mbApiInterface.Player_PlayPreviousTrack();
                                lastPrevious = DateTime.Now;
                            }
                        }

                        break;
                    // TODO: fix
                    case SystemMediaTransportControlsButton.ChannelUp:
                        mbApiInterface.Player_SetVolume(mbApiInterface.Player_GetVolume() + 0.05F);
                        break;
                    case SystemMediaTransportControlsButton.ChannelDown:
                        mbApiInterface.Player_SetVolume(mbApiInterface.Player_GetVolume() - 0.05F);
                        break;
                }
            }
        }

        private void systemMediaControls_PlaybackPositionChangeRequested(
            SystemMediaTransportControls systemMediaTransportControls,
            PlaybackPositionChangeRequestedEventArgs args)
        {
            mbApiInterface.Player_SetPosition(args.RequestedPlaybackPosition.Milliseconds);
        }

        private static void systemMediaControls_PlaybackRateChangeRequested(
            SystemMediaTransportControls systemMediaTransportControls,
            PlaybackRateChangeRequestedEventArgs args)
        {
        }

        private void systemMediaControls_AutoRepeatModeChangeRequested(
            SystemMediaTransportControls systemMediaTransportControls,
            AutoRepeatModeChangeRequestedEventArgs args)
        {
            switch (args.RequestedAutoRepeatMode)
            {
                case MediaPlaybackAutoRepeatMode.Track:
                    mbApiInterface.Player_SetRepeat(RepeatMode.One);
                    break;
                case MediaPlaybackAutoRepeatMode.List:
                    mbApiInterface.Player_SetRepeat(RepeatMode.All);
                    break;
                case MediaPlaybackAutoRepeatMode.None:
                    mbApiInterface.Player_SetRepeat(RepeatMode.None);
                    break;
            }
        }

        private void systemMediaControls_ShuffleEnabledChangeRequested(
            SystemMediaTransportControls systemMediaTransportControls,
            ShuffleEnabledChangeRequestedEventArgs args)
        {
            mbApiInterface.Player_SetShuffle(args.RequestedShuffleEnabled);
        }

        private void SetDisplayValues()
        {
            // displayUpdater.ClearAll();
            if (displayUpdater.Type != MediaPlaybackType.Music) displayUpdater.Type = MediaPlaybackType.Music;
            // SetArtworkThumbnail(null);
            var url = mbApiInterface.NowPlaying_GetFileUrl();
            if (url != null)
            {
                musicProperties.AlbumArtist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.AlbumArtist);
                musicProperties.AlbumTitle = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);

                if (TryParse(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackCount), out var value))
                    musicProperties.AlbumTrackCount = value;

                musicProperties.Artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.MultiArtist);
                musicProperties.Title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(musicProperties.Title))
                    musicProperties.Title = url.Substring(url.LastIndexOfAny(new[] {'/', '\\'}) + 1);

                if (TryParse(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackNo), out value))
                    musicProperties.TrackNumber = value;
                // musicProperties.Genres = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genres).Split(new string[] {"; "}, StringSplitOptions.RemoveEmptyEntries);
                mbApiInterface.Library_GetArtworkEx(url, 0, true, out _, out _, out var imageData);
                SetArtworkThumbnail(imageData);
            }
            else
            {
                SetArtworkThumbnail(null);
            }

            displayUpdater.Update();
        }

        private void SetPlayerState()
        {
            switch (mbApiInterface.Player_GetPlayState())
            {
                case PlayState.Playing:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    if (!systemMediaControls.IsEnabled)
                        systemMediaControls.IsEnabled = true;
                    break;
                case PlayState.Paused:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                case PlayState.Undefined:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    systemMediaControls.IsEnabled = false;
                    break;
                case PlayState.Loading:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Changing;
                    break;
                case PlayState.Stopped:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    break;
            }
        }

        private async void SetArtworkThumbnail(byte[] data)
        {
            if (artworkStream != null)
                artworkStream.Dispose();
            if (data == null)
            {
                artworkStream = null;
                displayUpdater.Thumbnail = null;
            }
            else
            {
                new MemoryStream(data).AsInputStream();

                artworkStream = new InMemoryRandomAccessStream();
                await artworkStream.WriteAsync(data.AsBuffer());
                displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(artworkStream);
                displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(artworkStream);
            }
        }
    }
}