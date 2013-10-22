#region License
// Copyright (c) 2013 Stichting Centrale Discotheek Rotterdam. All rights reserved.
// 
// website: http://www.muziekweb.nl
// e-mail:  info@cdr.nl
// 
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation; either version 2 of the License, or 
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
// 
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA. or 
// visit www.gnu.org.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CDR.LibRTMP;
using Un4seen.Bass;


// Todo:
// testen/uitzoeken wanneer verbinding met server wordt verbroken als je niks doet
//
// Future
// Play not from start but from a give startposition (parameter is implemented
// but funcionality not!)
//
namespace CDR.LibRTMP.Media
{
    /// <summary>
    /// 15 seconds before end of currently playing audio file the next MediaItem will 
    /// start a new Netstream and buffer for 15 seconds
    /// </summary>
    public class Mediaplayer : IDisposable
    {
        private const int BASS_FILEDATA_END = 0;
        private const int BASS_SAMPLE_RATE = 44100;
        private const int BASS_CHANNELS = 2;

        // stereo
        private const int BASS_BITS = 16;
        private const int BASS_MIN_INITIAL_FILLED_BUFFER = 4000;

        protected const int BUFFERTIME_IN_SECOND = 15; // see also NS_OnAssignStream_ID
        protected int PREBUFFERTIME_IN_SECOND = BUFFERTIME_IN_SECOND - 1; // see also NS_OnAssignStream_ID
        protected int MP3_BITRATE = 128; // 128 kilobits per seconds

        private const int AUTO_RECONNECT_IN_SECONDS = 5;
        private int PREVIOUS_CMD_IS_GOTOBEGIN_IN_SEC = 10; // after 10 seconds previous means start playing previous else it means goto previous mediafile

        private string email = string.Empty;
        private string registrationKey = string.Empty;
        private GCHandle? gcHandle = null; // needed because of bass

        private BASS_FILEPROCS bassFileProcs;
        private SYNCPROC bassStalledSync;
        private SYNCPROC bassEndSync;
        private byte bassVolume = 100;

        protected object lockVAR = new object();
        private Thread thread = null;
        private bool threadStarted = false;
        private EventWaitHandle messageQueueWaitEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        private List<MP_Message> messageQueue;
        protected SynchronizationContext synchronizationContext = null;

        private List<Playlist> playlists;
        private Playlist currentPlaylist;
        private PlaylistRepeatMode repeatMode = PlaylistRepeatMode.RepeatNone;
        private int disablePlaylistChangeEvent = 0;

        protected MediaplayerState mediaplayerState = MediaplayerState.Disconnected;

        protected MPButtonState previousButton = MPButtonState.Inactive;
        protected MPButtonState stopButton = MPButtonState.Inactive;
        protected MPButtonState playButton = MPButtonState.Inactive;
        protected MPButtonState pauseButton = MPButtonState.Inactive;
        protected MPButtonState nextButton = MPButtonState.Inactive;

        protected ServerLink rtmpServerLink = null;
        protected DateTime dtLastAutoReconnect = DateTime.MaxValue;
        protected bool lastConnectFailed = false;
        protected NetConnection netConnection = null;
        protected bool netConnectionReady = false;
        protected List<NetStreamHelper> netStreams = null;
        protected int failedPlayCount = 0;
        private bool commandInProgress = false;
        private double audioInBassBuffer = 0.0; // number of seconds of audio data in bass buffer
        private double lastPosition = 0.0;

        // =======================================================================================================
        // The events
        // =======================================================================================================
        /// <summary>
        /// Fired when we are connected to a RTMP media server
        /// </summary>
        public event MP_OnServer OnServerConnect = null;
        /// <summary>
        /// Fired when we lose the connection to the RTMP media server
        /// </summary>
        public event MP_OnServer OnServerDisconnect = null;

        /// <summary>
        /// Fired when the Current medialist has changed
        /// </summary>
        public event PL_OnPlaylistChanged OnPlaylistChanged = null;

        /// <summary>
        /// Fired when the Current MediaItem changes
        /// </summary>
        public event PL_OnMediaItemChanged OnCurrentMediaItemChanged = null;
        /// <summary>
        /// Fired when the Previous changes
        /// </summary>
        public event PL_OnMediaItemChanged OnPreviousMediaItemChanged = null;
        /// <summary>
        /// Fired when the Next changes
        /// </summary>
        public event PL_OnMediaItemChanged OnNextMediaItemChanged = null;

        /// <summary>
        /// Fired when the first item from a playlist starts to play
        /// </summary>
        public event MP_OnPlaylist OnPlaylistStart = null;
        /// <summary>
        /// Fired when the last item from a playlist ends playing.
        /// (if repeat is on then this events won't happen!)
        /// </summary>
        public event MP_OnPlaylist OnPlaylistEnd = null;

        /// <summary>
        /// Fired when a MediaItem start playing
        /// </summary>
        public event MP_OnMediaItem OnMediaItemStartPlay = null;
        /// <summary>
        /// Fired when a MediaItem starts Seek
        /// </summary>
        public event MP_OnMediaItem OnMediaItemSeekStart = null;
        /// <summary>
        /// Fired when a MediaItem ends Seek (and can start playig again)
        /// </summary>
        public event MP_OnMediaItem OnMediaItemSeekEnd = null;
        /// <summary>
        /// Fired when a MediaItem stops playing
        /// </summary>
        public event MP_OnMediaItem OnMediaItemEndPlay = null;
        /// <summary>
        /// Send when we do prebuffering. Should not realy be important to monitor,
        /// but event is available if needed.
        /// </summary>
        public event MP_OnPreBuffer OnPreBuffer = null;
        /// <summary>
        /// Fired when the state of one or more buttons changes
        /// </summary>
        public event MP_OnControleButtonStateChange OnControleButtonStateChange = null;
        /// <summary>
        /// Fired when mediaplayer goes to playmode/pausemode/stopmode
        /// </summary>
        public /* event */ MP_OnStateChangeMediaplayer OnStateChangeMediaplayer = null; // can['t use event because derived class needs access to it because of multithreading issues

        /// <summary>
        /// Generates an event approximately every 500 milliseconds
        /// </summary>
        public event MP_OnTick OnTick = null;
        // =======================================================================================================


        public Mediaplayer(string email = "", string registrationKey = "")
        {
            this.synchronizationContext = SynchronizationContext.Current;

            this.email = email;
            this.registrationKey = registrationKey;

            playlists = new List<Playlist>();
            currentPlaylist = new Playlist();
            playlists.Add(currentPlaylist);
            netStreams = new List<NetStreamHelper>();
            failedPlayCount = 0;

            // Link event so we can expose them to the "MediaPlayer" user
            currentPlaylist.OnPlaylistChanged += new PL_OnPlaylistChanged(DoOnPlaylistChanged);
            currentPlaylist.OnCurrentMediaItemChanged += new PL_OnMediaItemChanged(DoOnCurrentMediaItemChanged);
            currentPlaylist.OnPreviousMediaItemChanged += new PL_OnMediaItemChanged(DoOnPreviousMediaItemChanged);
            currentPlaylist.OnNextMediaItemChanged += new PL_OnMediaItemChanged(DoOnNextMediaItemChanged);

            gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            BassNetInitialize();

            messageQueue = new List<MP_Message>();
            CreateThread();
        }

        public void Close()
        {
            lock (lockVAR)
            {
                netConnectionReady = false;
                if (netConnection != null)
                {
                    MPThread_Disconnect();
                }

                // free BASS 
                Bass.BASS_Free();
                if (gcHandle != null)
                {
                    ((GCHandle)gcHandle).Free();
                    gcHandle = null;
                }
            } //lock

            KillThread();
        }

        #region IDispose implementation

        // Track whether Dispose has been called.
        private bool disposed = false;

        /// <summary>
        /// Implement IDisposable.
        /// Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </summary>
        void IDisposable.Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue 
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose(bool disposing) executes in two distinct scenarios.
        /// If disposing equals true, the method has been called directly
        /// or indirectly by a user's code. Managed and unmanaged resources
        /// can be disposed.
        /// If disposing equals false, the method has been called by the 
        /// runtime from inside the finalizer and you should not reference 
        /// other objects. Only unmanaged resources can be disposed.
        /// </summary>
        /// <param name="disposing"></param>
        private void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                try
                {
                    Close();
                }
                catch
                {
                }
            }

            disposed = true;
        }

        /// <summary>
        /// Use C# destructor syntax for finalization code.
        /// This destructor will run only if the Dispose method 
        /// does not get called.
        /// It gives your base class the opportunity to finalize.
        /// Do not provide destructors in types derived from this class.
        /// </summary>
        ~Mediaplayer()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion


        #region Bass required (callback) functions

        /// <summary>
        /// Initialize the bass library
        /// </summary>
        /// <returns></returns>
        protected bool BassNetInitialize()
        {
            // Keep Bass.Net from putting a popup on the screen
            BassNet.Registration(email, registrationKey);

            if (Bass.BASS_Init(-1, BASS_SAMPLE_RATE, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero)) // 0=console application otherwise this.Handle
            {
#if __IOS__
                //Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_IOS_SPEAKER, 1); 
#endif
                bassFileProcs = new BASS_FILEPROCS(
                    new FILECLOSEPROC(_BassCallback_FileProcUserClose),
                    new FILELENPROC(_BassCallback_FileProcUserLength),
                    new FILEREADPROC(_BassCallback_FileProcUserRead),
                    new FILESEEKPROC(_BassCallback_FileProcUserSeek));
                bassStalledSync = new SYNCPROC(_BassCallback_StalledSync);
                bassEndSync = new SYNCPROC(_BassCallback_EndSync);

                Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_NET_BUFFER, 1000 * 2); // 2 seconds buffer (the default of bass also)

                return true;
            }

            return false;
        }


#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(SYNCPROC))]
#endif
        private static void _BassCallback_StalledSync(int handle, int channel, int data, IntPtr user)
        {
        }

#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(SYNCPROC))]
#endif
        private static void _BassCallback_EndSync(int handle, int channel, int data, IntPtr user)
        {
            if (user != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(user);
                Mediaplayer obj = (Mediaplayer)gcHandle.Target;
                obj.BassCallback_EndSync(handle, channel, data, Guid.Empty);
            }
        }

        private void BassCallback_EndSync(int handle, int channel, int data, Guid newMediaItemGUID)
        {
            // WARNING we are on the BASS or Mediaplayer THREAD HERE!!!!!!! (not always!!)
            NetStreamHelper nsh = null;
            MediaItem preBufferedMediaItem = null;

            int netStreamsCount = 0;
            lock (lockVAR)
            {
                if (netStreams.Count > 0)
                {
                    nsh = netStreams[0];
                    netStreams.RemoveAt(0); // belangrijk als je het juist mediaitem wil selecteren voor afspelen
                    // nsh will be cleanup up by call to CloseAudioStream later in this function

                    // renumber
                    for (int i = 0; i < netStreams.Count; i++)
                    {
                        netStreams[i].IndexNumberInList = i;
                    } //for
                }
                netStreamsCount = netStreams.Count;
                if (netStreamsCount > 0 && newMediaItemGUID.Equals(Guid.Empty))
                {
                    // needed to change currentMediaItem
                    newMediaItemGUID = netStreams[0].Item.GUID;
                    preBufferedMediaItem = (MediaItem)netStreams[0].Item.Clone();
                }
            } //lock

            // is prebuffered netstream ready top be player?
            if (netStreamsCount > 0)
            {
                DoEvent_MP_OnPreBuffer(OnPreBuffer, this, (MediaItem)preBufferedMediaItem, PreBufferState.PrebufferingEndedAndPlaying);
            }

            MP_OnPlaylist onPlaylistEnd = null;
            Playlist playlist = null;
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            MP_OnMediaItem onMediaItemEndPlay = null;
            MediaItem item = null;
            if (nsh != null)
            {
                CloseAudioStream(nsh);
                lock (lockVAR)
                {
                    if (OnMediaItemEndPlay != null)
                    {
                        onMediaItemEndPlay = OnMediaItemEndPlay;
                        item = (MediaItem)nsh.Item.Clone();
                    }
                    if (newMediaItemGUID.Equals(Guid.Empty) && OnPlaylistEnd != null && netStreams.Count == 0)
                    {
                        onPlaylistEnd = OnPlaylistEnd;
                        playlist = (Playlist)currentPlaylist.Clone();
                    }
                    if (newMediaItemGUID.Equals(Guid.Empty) && netStreams.Count == 0)
                    {
                        onStateChangeMediaplayer = OnStateChangeMediaplayer;
                        mediaplayerState = MediaplayerState.Stop;
                    }
                } //lock

                // Fire event that MediaItem ends playing audio (event will only be fired
                // when onMediaItemEndPlay and item are not null.)
                DoEvent_MP_OnMediaItem(onMediaItemEndPlay, this, item);
                // reset (saved) position because we're at the end
                lastPosition = 0.0;

                // Now fire MediaItem events! (must be done here to for right
                // order of events!
                if (!newMediaItemGUID.Equals(Guid.Empty))
                {
                    currentPlaylist.ChangeCurrentMediaItemGUID(newMediaItemGUID, true);
                }

                // Fire event that Playlist play has ended(event will only be fired
                // when onPlaylistEnd is not null.)
                DoEvent_MP_OnPlaylist(onPlaylistEnd, this, playlist);

                // Fire event we go to stop state
                DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);

                // Startup any new audio channel (only audio playing, buffering has already been done)
                StartPlayingAudioChannel();

                // Is there a next MediaItem to play? (and no netstreams queued) 
                if (netStreamsCount <= 0 && !newMediaItemGUID.Equals(Guid.Empty))
                {
                    // Start next netstream, when event "NS_OnAssignStream_ID" is fired
                    // it will be handled further.
                    lock (lockVAR)
                    {
                        NetStreamHelper nth = NewNetStreamHelper(currentPlaylist.GetOrginalMediaItem(newMediaItemGUID));
                        if (nth != null)
                        {
                            netStreams.Add(nth);
                            nth.NetStream.Connect(); // start request for stream_id to start streamning the music data
                        }
                    } //lock
                }
            }

            // Button are probably changed
            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
        }

        /// <summary>
        /// USED when Stream freed
        /// </summary>
#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(FILECLOSEPROC))]
#endif
        private static void _BassCallback_FileProcUserClose(IntPtr user)
        {
            return;
        }

#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(FILELENPROC))]
#endif
        private static long _BassCallback_FileProcUserLength(IntPtr user)
        {
            return 0;
        }

        /// <summary>
        /// Used to bootstrap bass to start playing. It's needs some data so it can detect wat 
        /// type of file it is. After that we can start using "BASS_StreamPutFileData"
        /// to push the remainder of the data to bass. This function will never be called again
        /// </summary>
#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(FILEREADPROC))]
#endif
        private static int _BassCallback_FileProcUserRead(IntPtr buffer, int length, IntPtr user)
        {
            if (user != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(user);
                Mediaplayer obj = (Mediaplayer)gcHandle.Target;
                return obj.BassCallback_FileProcUserRead(buffer, length);
            }

            return 0;
        }

        /// <summary>
        /// See above but now with class context
        /// </summary>
        private int BassCallback_FileProcUserRead(IntPtr buffer, int length)
        {
            int todo = length;
            NetStreamHelper nsh = null;
            lock (lockVAR)
            {
                if (netStreams.Count > 0)
                {
                    nsh = netStreams[netStreams.Count - 1];
                }
            }

            while (nsh != null && todo > 0)
            {
                lock (lockVAR)
                {
                    byte[] byteBuffer = new byte[length];

                    int count = nsh.Buffer.Read(byteBuffer, todo);
                    todo -= count;
                    if (count > 0)
                    {
                        // Nu kopieren in unmanaged data
                        Marshal.Copy(byteBuffer, 0, buffer, count);
                    }
                } //lock

                if (todo > 0)
                {
                    // wait some before trying to get more data to stuff into the buffer
                    Thread.Sleep(100); // wait for more data
                }
            } //while

            return length - todo;
        }

        /// <summary>
        /// NOT USED just here for reference
        /// </summary>
#if __IOS__
        [MonoTouch.MonoPInvokeCallback(typeof(FILESEEKPROC))]
#endif
        private static bool _BassCallback_FileProcUserSeek(long offset, IntPtr user)
        {
            return false;
        }

        #endregion


        #region Thread management

        private void CreateThread()
        {
            // Kill thread if is was already created
            KillThread();
            threadStarted = false;

            // Start de thread
            ThreadStart st = new ThreadStart(StartMessageQueueProcessor); // create a thread and attach to the object
            thread = new Thread(st);
            thread.IsBackground = true; // when true application won't hang on this even when thread doesn't stop
            // For debugging
            thread.Name = "CDR.LibRTMP.Media.Mediaplayer";
            thread.Start();
        }

        private void KillThread()
        {
            try
            {
                if (thread != null)
                {
                    if (threadStarted)
                    {
                        // Stop de thread's
                        threadStarted = false;

                        // Killing yourself is not possible
                        if (Thread.CurrentThread.ManagedThreadId != thread.ManagedThreadId)
                        {
                            // Maak thread wakker
                            messageQueueWaitEvent.Set(); // start de thread mocht de thread in een slaap toestand staan
                            // Wacht max 150 milliseconds om het ding te laten stoppen
                            if (!thread.Join(new TimeSpan(0, 0, 0, 0, 150)))
                            {
                                thread.Abort();
                            }
                            thread = null;
                        }
                    }
                }
            }
            catch { }
        }

        #endregion


        #region Threading functions

        /// <summary>
        /// This is the main Mediaplayer func where the thread spents most of it's time
        /// </summary>
        private void StartMessageQueueProcessor()
        {
            threadStarted = true;
            DateTime dtTimer = DateTime.Now; // used to trigger every 500 milliseconds an event

            while (threadStarted)
            {
                MP_Message message = null;

                try
                {
                    // ======================================================================================================================
                    // fire state change event
                    lock (lockVAR)
                    {
                        if (messageQueue.Count > 0)
                        {
                            // get oldest first
                            message = messageQueue[0];
                            messageQueue.RemoveAt(0);
                        }
                    } //lock

                    if (message != null)
                    {
                        switch (message.MethodCall)
                        {
                            case MP_MethodCall.Connect:
                                MPThread_Connect();
                                break;
                            case MP_MethodCall.Disconnect:
                                MPThread_Disconnect();
                                break;

                            case MP_MethodCall.Play:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Play((long)message.Params[0]);
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (playButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }
                                    message = null;
                                }
                                break;
                            case MP_MethodCall.Pause:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Pause();
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (pauseButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }
                                    message = null;
                                }
                                break;
                            case MP_MethodCall.Stop:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Stop();
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (stopButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }
                                    message = null;
                                }
                                break;
                            case MP_MethodCall.KillPreBufferedMediaItem:
                                MPThread_KillPreBufferedMediaItem();
                                break;
                            case MP_MethodCall.Seek:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Seek((long)message.Params[0]);
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (pauseButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }
                                    message = null;
                                }
                                break;
                            case MP_MethodCall.Next:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Next();
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (nextButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }                                        
                                    message = null;                                    
                                }
                                break;
                            case MP_MethodCall.Previous:
                                if (!netConnectionReady)
                                {
                                    // klaar
                                    break;
                                }
                                if (!commandInProgress)
                                {
                                    commandInProgress = true;
                                    MPThread_Previous();
                                }
                                else
                                {
                                    // Queue it in again, have to wait until netstream id ready!
                                    if (previousButton == MPButtonState.Active)
                                    {
                                        RequeueMessage(message);
                                    }
                                    message = null;
                                }
                                break;
                        } //switch

                        // possible button state changed
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                    }
                    // ======================================================================================================================

                    // ======================================================================================================================
                    // Make sure audio data is regulary send to bass.lib
                    MPThread_FillBassAudioBuffer();
                    // ======================================================================================================================

                    // ======================================================================================================================
                    // Check if we need to start buffering for next MediaItem (we do this 15 seconds before end of current playing MediaItem)
                    // Is there a next MediaItem to play? (and no netstreams queued) 
                    MPThread_CheckForPreBuffering();
                    // ======================================================================================================================

                    // Previous button can be activated when 10seconds have played
                    if (mediaplayerState == Media.MediaplayerState.Playing && previousButton == MPButtonState.Inactive && Position > PREVIOUS_CMD_IS_GOTOBEGIN_IN_SEC)
                    {
                        // PreviousButton will change
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                    }

                    // ======================================================================================================================
                    // Generate OnTick event approximately every 500 milliseconds
                    if ((DateTime.Now - dtTimer).TotalMilliseconds >= 500)
                    {
                        MP_OnTick onTick = null;
                        lock (lockVAR)
                        {
                            onTick = OnTick;
                        } //lock
                        dtTimer = DateTime.Now; // setup for next timer tick
                        // is implemented in place here, because of calling sequence and speed    
                        DoOnTickEvent(onTick, this);
                    }
                    // ======================================================================================================================

                    // ======================================================================================================================
                    // Try every 5 seconds a reconnect attempt
                    if (mediaplayerState == Media.MediaplayerState.Disconnected && (DateTime.Now - dtLastAutoReconnect).TotalSeconds >= AUTO_RECONNECT_IN_SECONDS)
                    {
                        MPThread_Connect();
                    }
                    // ======================================================================================================================

                    // ======================================================================================================================
                    // for when an overriden class also needs to do something in the Mediaplayer thread
                    OnMessageQueueLoop();                    
                    // ======================================================================================================================


                    if (threadStarted && message == null)
                    {
                        // Wait 100 ms, before running again
                        messageQueueWaitEvent.WaitOne(100);
                    }
                }
                catch (Exception e)
                {
                    // log eventuele error in deze thread
                    LibRTMPLogger.LogError(e);
                }
            } //while

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "CDR.LibRTMP.Media.Mediaplayer.StartMessageQueueProcessor exit thread");
        }

        protected virtual void OnMessageQueueLoop()
        {
        }

        private void RecalcMediaplayerState()
        {
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;

            //fixup var "mediaplayerState"
            lock (lockVAR)
            {
                MediaplayerState savedMediaplayerState = mediaplayerState;

                if (netConnection != null && netConnection.Connecting && !netConnection.IsConnected)
                {
                    mediaplayerState = MediaplayerState.Connecting;
                }
                else if (netConnection == null || !netConnection.IsConnected)
                {
                    mediaplayerState = MediaplayerState.Disconnected;
                }
                else if (netStreams.Count == 0)
                {
                    mediaplayerState = MediaplayerState.Stop;
                }
                else if (netStreams.Count > 0)
                {
                    switch (netStreams[0].PlayState)
                    {
                        case NetStreamState.None:
                            mediaplayerState = MediaplayerState.Stop;
                            break;
                        case NetStreamState.Connecting:
                            mediaplayerState = MediaplayerState.Connecting;
                            break;
                        case NetStreamState.Playing:
                        case NetStreamState.Seek:
                            mediaplayerState = MediaplayerState.Playing;
                            break;
                        case NetStreamState.Pause:
                            mediaplayerState = MediaplayerState.Pause;
                            break;
                    } //switch
                }
                if (savedMediaplayerState != mediaplayerState)
                {
                    onStateChangeMediaplayer = OnStateChangeMediaplayer;
                }
            }//lock

            // Fire state event if needed
            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);
        }

        private void MPThread_CheckForPreBuffering()
        {
            // Check if we need to start buffering for next MediaItem (we do this 15 seconds before end of current playing MediaItem)
            // Is there a next MediaItem to play? (and no netstreams queued) 
            MP_OnPreBuffer doOnPreBuffer = null;
            MediaItem preBufferedMediaItem = null;
            PreBufferState preBufferState = PreBufferState.Unknown;
            bool buttonStateChanged = false;

            lock (lockVAR)
            {
                if (netStreams.Count == 1 && !netStreams[0].NextNetStreamStarted)
                {
                    // How long will this stream play?
                    long durationInMS = Convert.ToInt64(netStreams[0].NetStream.Duration * 1000);
                    long position = Convert.ToInt64(this.Position * 1000);
                    if (durationInMS > 0 && position > 0 && (durationInMS - position) <= 15000)
                    {
                        if (currentPlaylist.NextMediaItem != null)
                        {
                            netStreams[0].NextNetStreamStarted = true;
                            buttonStateChanged = true;
                            // Start next netstream, when event "NS_OnAssignStream_ID" is fired
                            // it will be handled further.
                            lock (lockVAR)
                            {
                                NetStreamHelper nth = NewNetStreamHelper(currentPlaylist.GetOrginalMediaItem(currentPlaylist.NextMediaItem.GUID));
                                if (nth != null)
                                {
                                    netStreams.Add(nth);
                                    nth.NetStream.Connect(); // start request for stream_id to start streaming the music data

                                    // Send event prebuffer event
                                    doOnPreBuffer = OnPreBuffer;
                                    preBufferedMediaItem = (MediaItem)nth.Item.Clone();
                                    preBufferState = PreBufferState.PrebufferingStarted;
                                }
                            } //lock
                        }
                    }
                }
            }

            DoEvent_MP_OnPreBuffer(doOnPreBuffer, this, preBufferedMediaItem, preBufferState);

            if (buttonStateChanged)
            {
                // Button are probably changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        /// <summary>
        /// Connect to RTMP server using RTMPServerLink settings
        /// When "Mediaplayer" is connected the "OnServerConnect" event will be fired
        /// 
        /// WARNING if there are changes don't fortget to do this for eMuziek en MuziekwebLusiter 
        /// classes also (have roughly same implementatino with added securirty calls)
        /// </summary>
        protected virtual void MPThread_Connect()
        {
            lastConnectFailed = false;

            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            lock (lockVAR)
            {
                onStateChangeMediaplayer = OnStateChangeMediaplayer;
                mediaplayerState = MediaplayerState.Connecting;
                dtLastAutoReconnect = DateTime.Now;
            }
            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);

            // possible button state changed
            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);

            netConnectionReady = false;
            netStreams.Clear();
            if (netConnection != null)
            {
                netConnection.Close();
                netConnection = null;
            }

            // Important to only do this here. In derived classes this function is overriden to add
            // securtity calls
            netConnection = new NetConnection(null); // force event over NetConnection thread (we will managed them in this class ourself)
            netConnection.OnDisconnect += new NC_OnDisconnect(NC_OnDisconnect);
            netConnection.Connect(rtmpServerLink, NC_OnConnect);
        }

        /// <summary>
        /// Stops playing audio, disconnects any netStream connections and as
        /// last disconnects the NetConnection
        /// </summary>
        protected virtual void MPThread_Disconnect()
        {
            lock (lockVAR)
            {
                // turn it off
                dtLastAutoReconnect = DateTime.MaxValue; // turns autoreconnect off
                netConnectionReady = false;

                foreach (NetStreamHelper nsh in netStreams)
                {
                    nsh.Close();
                } //foreach
                netStreams.Clear();

                if (netConnection != null)
                {
                    NetConnection tmpNC = netConnection;
                    // protect anaginst recusve callback
                    netConnection = null;
                    
                    // this should fire an ondisconnectserver event
                    tmpNC.Close();                    
                }
            } //lock
        }


        /// <summary>
        /// This function is called every 100ms or so to try and keep the bass buffer filled
        /// </summary>
        private void MPThread_FillBassAudioBuffer()
        {
            lock (lockVAR)
            {
                foreach (NetStreamHelper nsh in netStreams)
                {
                    if (nsh.BassHandle != 0)
                    {
                        long bassBufLen = Bass.BASS_StreamGetFilePosition(nsh.BassHandle, BASSStreamFilePosition.BASS_FILEPOS_END);
                        long bassBufPos = Bass.BASS_StreamGetFilePosition(nsh.BassHandle, BASSStreamFilePosition.BASS_FILEPOS_BUFFER);
                        int todo = Convert.ToInt32(bassBufLen - bassBufPos);
                        if (todo > 0)
                        {
                            int count = todo;
                            if (count > 16384)
                            {
                                count = 16384;
                            }
                            byte[] tmpBuffer = new byte[count];
                            count = nsh.Buffer.Read(tmpBuffer, count);
                            if (count > 0)
                            {
                                Bass.BASS_StreamPutFileData(nsh.BassHandle, tmpBuffer, count);
                            }
                        }
                        // Calculate amount of seconds of data in buffer
                        audioInBassBuffer = Bass.BASS_StreamGetFilePosition(nsh.BassHandle, BASSStreamFilePosition.BASS_FILEPOS_BUFFER);
                        audioInBassBuffer = audioInBassBuffer / ((128 * 1024) / 8); //(Bass.BASS_GetConfig(BASSConfig.BASS_CONFIG_NET_BUFFER) / 1000);

                        if (nsh.IsComplete && nsh.Buffer.UsedBytes == 0 && !nsh.BassEndOfFileSend)
                        {
                            byte[] zeroBuffer = new byte[0];
                            int done = Bass.BASS_StreamPutFileData(nsh.BassHandle, zeroBuffer, BASS_FILEDATA_END);
                            nsh.BassEndOfFileSend = true;
                        }
                    }
                } //foreach
            } //lock
        }

        /// <summary>
        /// -1 = start at beginning
        /// </summary>
        /// <param name="startPositionInMS"></param>
        private void MPThread_Play(long startPositionInMS = -1)
        {
            NetStreamHelper nth = null;
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            MP_OnPlaylist onPlaylistStart = null;
            Playlist playlist = null;
            bool buttonStateChanged = false;

            lock (lockVAR)
            {
                if (netConnection != null && netConnection.IsConnected)
                {
                    if (netStreams.Count > 0)
                    {
                        if (netStreams[0].NetStream.PauseIsActive)
                        {
                            // unpause audio playing using bass
                            Bass.BASS_ChannelPlay(netStreams[0].BassHandle, false);
                            // restart streaming again
                            netStreams[0].NetStream.Pause(false);

                            netStreams[0].PlayState = NetStreamState.Playing;
                            buttonStateChanged = true;
                            mediaplayerState = MediaplayerState.Playing;
                            onStateChangeMediaplayer = OnStateChangeMediaplayer;
                        }
                    }
                    else
                    {
                        // Start playing
                        netStreams.Clear();

                        // Simulate MediaItem events changed (oldMediaitem is null!)
                        TriggerMediaItemEvents();
                        buttonStateChanged = true;

                        // Start new netstream, when event "NS_OnAssignStream_ID" is fired
                        // it will be handled further.
                        //lock (lockVAR)
                        {
                            nth = NewNetStreamHelper(currentPlaylist.GetOrginalMediaItem(currentPlaylist.CurrentMediaItem.GUID));
                            if (nth != null)
                            {
                                netStreams.Add(nth);

                                onStateChangeMediaplayer = OnStateChangeMediaplayer;
                                mediaplayerState = MediaplayerState.Playing;
                                onPlaylistStart = OnPlaylistStart;
                                playlist = (Playlist)currentPlaylist.Clone();
                            }
                            else
                            {
                                onStateChangeMediaplayer = OnStateChangeMediaplayer;
                                mediaplayerState = MediaplayerState.Stop;
                            }
                        } //lock
                    }
                }
                else
                {
                    commandInProgress = false;
                }
            } //lock

            // Fire event we go to Play state
            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);
            
            if (buttonStateChanged)
            {
                // possible button state changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }

            // Fire event that Playlist play has started(event will only be fired
            // when onPlaylistStart is not null.)
            DoEvent_MP_OnPlaylist(onPlaylistStart, this, playlist);
            if (nth != null)
            {
                nth.NetStream.Connect(); // start request for stream_id to start streamning the music data
            }
        }

        private void MPThread_Pause()
        {
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            bool buttonStateChanged = false;

            lock (lockVAR)
            {
                if (netConnection != null && netConnection.IsConnected && netStreams.Count > 0)
                {
                    if (netStreams[0].PlayState == NetStreamState.Playing)
                    {
                        // Saved paused position
                        lastPosition = Position;
                        // pause audio playing using bass
                        Bass.BASS_ChannelPause(netStreams[0].BassHandle);
                        // Send server pause command
                        netStreams[0].NetStream.Pause(true);
                        // set state
                        netStreams[0].PlayState = NetStreamState.Pause;

                        onStateChangeMediaplayer = OnStateChangeMediaplayer;
                        mediaplayerState = MediaplayerState.Pause;
                        buttonStateChanged = true;
                    }
                }
                else
                {
                    commandInProgress = false;
                }
            }

            // Fire event we go to pause state
            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);

            if (buttonStateChanged)
            {
                // possible button state changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        private void MPThread_Stop()
        {
            MP_OnPlaylist onPlaylistEnd = null;
            Playlist playlist = null;
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            MP_OnMediaItem onMediaItemEndPlay = null;
            MediaItem item = null;
            bool buttonStateChanged = false;

            lock (lockVAR)
            {
                if (netStreams.Count > 0)
                {
                    NetStreamHelper nsh = netStreams[0];
                    if (OnMediaItemEndPlay != null)
                    {
                        onMediaItemEndPlay = OnMediaItemEndPlay;
                        item = (MediaItem)nsh.Item.Clone();
                    }
                    if (OnPlaylistEnd != null)
                    {
                        onPlaylistEnd = OnPlaylistEnd;
                        playlist = (Playlist)currentPlaylist.Clone();
                    }
                }

                if (netConnection != null && netConnection.IsConnected)
                {
                    onStateChangeMediaplayer = OnStateChangeMediaplayer;
                    mediaplayerState = MediaplayerState.Stop;
                }
                buttonStateChanged = true;

                foreach (NetStreamHelper nsh in netStreams)
                {
                    nsh.Close();
                } //foreach
                netStreams.Clear();
                lastPosition = 0.0;
                commandInProgress = false; // reset it after seek command


                onStateChangeMediaplayer = OnStateChangeMediaplayer;
                mediaplayerState = MediaplayerState.Stop;
            } //lock

            // Fire event that MediaItem ends playing audio (event will only be fired
            // when onMediaItemEndPlay and item are not null.)
            DoEvent_MP_OnMediaItem(onMediaItemEndPlay, this, item);

            // Fire event that Playlist play has ended(event will only be fired
            // when onPlaylistEnd is not null.)
            DoEvent_MP_OnPlaylist(onPlaylistEnd, this, playlist);

            // Fire event we go to stop state
            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);

            if (buttonStateChanged)
            {
                // possible button state changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        private void MPThread_KillPreBufferedMediaItem()
        {
            // for loop not really neccesary (at most there are  max of 2 items)
            for (int i = netStreams.Count - 1; i >= 1; i--)
            {
                NetStreamHelper nsh = null;
                lock (lockVAR)
                {
                    if (i < netStreams.Count)
                    {
                        nsh = netStreams[i];
                        netStreams.RemoveAt(i);
                    }
                } //lock

                if (nsh != null)
                {
                    CloseAudioStream(nsh);
                    DoEvent_MP_OnPreBuffer(OnPreBuffer, this, (MediaItem)nsh.Item.Clone(), PreBufferState.PrebufferingEndedAndCanceled);
                }
            } //for

            lock (lockVAR)
            {
                if (netStreams.Count == 1)
                {
                    // Make sure we can restart a next netstream
                    netStreams[0].NextNetStreamStarted = false;
                }
            } //lock
        }

        private void MPThread_Seek(long positionInMS)
        {
            MP_OnMediaItem onMediaItemSeekStart = null;
            MediaItem item = null;
            bool buttonStateChanged = false;

            lock (lockVAR)
            {
                if (netConnection != null && netConnection.IsConnected && netStreams.Count > 0)
                {
                    if (netStreams[0].PlayState == NetStreamState.Playing)
                    {
                        if (OnMediaItemSeekStart != null)
                        {
                            // We're playing
                            onMediaItemSeekStart = OnMediaItemSeekStart;
                            item = (MediaItem)netStreams[0].Item.Clone();
                        }
                        // Stop playing, close bass handle and reset vars  to default
                        netStreams[0].Stop();
                        // set state
                        netStreams[0].PlayState = NetStreamState.Seek;
                        // Send server seek command
                        netStreams[0].NetStream.Seek(positionInMS);
                        buttonStateChanged = true;
                    }
                }
            }

            commandInProgress = false;

            DoEvent_MP_OnMediaItem(onMediaItemSeekStart, this, item);

            if (buttonStateChanged)
            {
                // possible button state changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        private void MPThread_Next()
        {
            int netStreamsCount = -1;
            lock (lockVAR)
            {
                if (netConnection != null && netConnection.IsConnected)
                {
                    if (netStreams.Count > 0)
                    {
                        if (netStreams[0].PlayState == NetStreamState.Playing || netStreams[0].PlayState == NetStreamState.Pause || netStreams[0].PlayState == NetStreamState.Seek)
                        {
                            Bass.BASS_ChannelPause(netStreams[0].BassHandle);

                            Guid itemGUID = Guid.Empty;
                            if (currentPlaylist.NextMediaItem != null)
                            {
                                itemGUID = currentPlaylist.NextMediaItem.GUID;
                            }
                            // Fake call to stop playing.
                            BassCallback_EndSync(0, 0, 0, itemGUID);
                        }
                    }
                }

                netStreamsCount = netStreams.Count;
                commandInProgress = false;
            } //lock

            // Start playing next item (if there is one)
            if (netStreamsCount == 0 && currentPlaylist.NextMediaItemIndex != -1)
            {
                // Changed to next item.
                currentPlaylist.ChangeCurrentMediaItemIndex(currentPlaylist.NextMediaItemIndex, true);
                // Start playing (at default (old) position or start of stream)
                MPThread_Play(-1);
            }
        }

        private void MPThread_Previous()
        {
            lock (lockVAR)
            {
                if (netConnection != null && netConnection.IsConnected && netStreams.Count > 0)
                {
                    if (netStreams[0].PlayState == NetStreamState.Playing)
                    {
                        Bass.BASS_ChannelPause(netStreams[0].BassHandle);

                        // within 10 seconds of beginning?
                        if (Position > PREVIOUS_CMD_IS_GOTOBEGIN_IN_SEC)
                        {
                            // "MPThread_Seek" function will reset "commandInProgress"
                            // goto beginning of media file
                            MPThread_Seek(0);

                            return;
                        }

                        Guid itemGUID = Guid.Empty;
                        if (currentPlaylist.PreviousMediaItem != null)
                        {
                            itemGUID = currentPlaylist.PreviousMediaItem.GUID;
                        }
                        // Fake call to stop playing and select the next item 
                        // (That's what itemGUID is for)
                        BassCallback_EndSync(0, 0, 0, itemGUID);
                    }
                }

                commandInProgress = false;
            }
        }

        #endregion


        #region Private NetConnection/NetStream events implementations and helper functions

        protected void ResetStateMediaplayer()
        {
            audioInBassBuffer = 0.0; // number of seconds of audio data in bass buffer
            lastPosition = 0.0;
            failedPlayCount = 0;
            commandInProgress = false;
            netConnectionReady = false;
            // drain command queue (use locking?)
            lock (lockVAR)
            {
                messageQueue.Clear();
            } //lock
        }

        /// <summary>
        /// Event fired by NetConnection
        /// </summary>
        protected void NC_OnConnect(object sender, bool success)
        {
            lastConnectFailed = !success;

            if (success)
            {
                netStreams.Clear();
                netConnectionReady = true;

                MP_OnServer onServerConnect = null;
                MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
                lock (lockVAR)
                {
                    onServerConnect = OnServerConnect;
                    onStateChangeMediaplayer = OnStateChangeMediaplayer;
                    mediaplayerState = MediaplayerState.Stop;
                } //lock

                DoEvent_MP_OnServer(onServerConnect, this);

                // Fire event we are in the Connect state
                DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);
            }
            else
            {
                MPThread_Disconnect();

                MP_OnServer onServerDisconnect = null;
                MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
                lock (lockVAR)
                {
                    onServerDisconnect = OnServerDisconnect;
                    onStateChangeMediaplayer = OnStateChangeMediaplayer;
                    mediaplayerState = MediaplayerState.Disconnected;
                    dtLastAutoReconnect = DateTime.Now; // make sure we keep trying to connect

                    ResetStateMediaplayer();
                } //lock

                DoEvent_MP_OnServer(onServerDisconnect, this);
                // Fire event we are in the "None" state
                DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);
            }

            // possible button state changed
            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
        }

        /// <summary>
        /// Fired by NetConnection when Connection is lost
        /// </summary>
        protected virtual void NC_OnDisconnect(object sender)
        {
            MP_OnMediaItem onMediaItemEndPlay = null;
            MediaItem item = null;
            MP_OnPlaylist onPlaylistEnd = null;
            Playlist playlist = null;
            MP_OnStateChangeMediaplayer onStateChangeMediaplayer = null;
            MP_OnServer onServerDisconnect = null;

            lock (lockVAR)
            {
                // we're we playing?
                if (netStreams.Count > 0 && netStreams[0].BassHandle != 0)
                {
                    // We're playing
                    onMediaItemEndPlay = OnMediaItemEndPlay;
                    item = (MediaItem)netStreams[0].Item.Clone();

                    onPlaylistEnd = OnPlaylistEnd;
                    playlist = (Playlist)currentPlaylist.Clone();
                }

                onStateChangeMediaplayer = OnStateChangeMediaplayer;
                mediaplayerState = MediaplayerState.Disconnected;
                if ((DateTime.MaxValue - dtLastAutoReconnect).TotalDays != 0)
                {
                    dtLastAutoReconnect = DateTime.Now; // make sure we keep trying to connect
                }

                ResetStateMediaplayer();

                onServerDisconnect = OnServerDisconnect;
            }

            DateTime saved_dtLastAutoReconnect = dtLastAutoReconnect;
            try
            {
                MPThread_Disconnect();
            }
            finally
            {
                dtLastAutoReconnect = saved_dtLastAutoReconnect;
            }

            // Make sure we fire some event that playing has stopped
            DoEvent_MP_OnMediaItem(onMediaItemEndPlay, this, item);

            DoEvent_MP_OnPlaylist(onPlaylistEnd, this, playlist);

            DoEvent_MP_OnStateChangeMediaplayer(onStateChangeMediaplayer, this, mediaplayerState);

            // possible button state changed
            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);

            DoEvent_MP_OnServer(onServerDisconnect, this);
        }


        /// <summary>
        /// WARNING if there are changes don't fortget to do this for eMuziek en MuziekwebLusiter 
        /// classes also (have roughly same implementatino with added securirty calls)
        /// </summary>
        protected virtual void NS_OnAssignStream_ID(object sender, int stream_id, ref int contentBufferTime)
        {
            contentBufferTime = BUFFERTIME_IN_SECOND * 1000; // 15 seconds of audio buffer

            // Start request for playing, by starting the streaming
            NetStreamHelper nth = FindNetStreamHelper(sender as NetStream);
            if (nth != null)
            {
                // Connect data event
                nth.NetStream.OnStatus += new NS_OnStatus(NS_OnStatus);
                nth.NetStream.OnAudioPacket += new NC_OnMediaPacket(NC_OnMediaPacket);
                nth.NetStream.Play(nth.Item.MediaFile);
            }
        }

        protected void NS_OnStatus(object sender, NetStreamStatusEvent netStreamStatusEvent)
        {
            NetStreamHelper netStreamHelper = FindNetStreamHelper(sender as NetStream);
            if (netStreamHelper != null)
            {
                switch (netStreamStatusEvent.Code)
                {
                    case "NetStream.Play.Stop": // start buffering next mediaitem?
                        if (!netStreamHelper.NetStream.SeekIsActive && !netStreamHelper.IsComplete)
                        {
                            commandInProgress = false; // reset it
                            netStreamHelper.IsComplete = true;
                        }
                        // possible button state changed
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                        break;
                    case "NetStream.Play.OnMetaData":
                        lock (lockVAR)
                        {
                            netStreamHelper.Item.NetStreamDurationInMS = Convert.ToInt64(netStreamHelper.NetStream.Duration * 1000);
                        }
                        break;
                    case "NetStream.Play.Start":
                    case "NetStream.Play.Resume":
                        failedPlayCount = 0; // reset
                        commandInProgress = false; // reset it after Play command
                        // Do not set PlayState from Seek to Playing, otherwise 
                        // call to "StartPlayingAudioChannel" won't start playing audio again!
                        // "StartPlayingAudioChannel" is called from "NC_OnMediaPacket"
                        // so playing will start when there is data in the buffer

                        MP_OnMediaItem onMediaItemSeekEnd = null;
                        MediaItem item = null;
                        lock (lockVAR)
                        {
                            if (netStreams.Count > 0 && netStreams[0].PlayState == NetStreamState.Seek)
                            {
                                if (OnMediaItemSeekEnd != null)
                                {
                                    // We're playing
                                    onMediaItemSeekEnd = OnMediaItemSeekEnd;
                                    item = (MediaItem)netStreams[0].Item.Clone();
                                }
                            }
                        } //lock
                        DoEvent_MP_OnMediaItem(onMediaItemSeekEnd, this, item);
                        // possible button state changed
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                        break;
                    case "NetStream.Seek.Notify":
                        // wait for "NetStream.Play.Start"
                        // Flush buffer once more (because packets could be stored between call en send to remote server
                        if (netStreamHelper.Buffer != null)
                        {
                            netStreamHelper.Buffer.Clear();
                        }
                        // Should already be set to this!
                        netStreamHelper.PlayState = NetStreamState.Seek;
                        // possible button state changed
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                        break;
                    case "NetStream.Pause.Notify":
                        commandInProgress = false; // reset it after seek command
                        // possible button state changed
                        DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
                        break;

                    case "NetStream.Play.Failed": // event when playing fails and we have to skip to the next one
                    case "NetStream.Play.StreamNotFound":
                    case "NetStream.Failed":
                        failedPlayCount++;

                        // Fake status so Next and stop work
                        lock (lockVAR)
                        {
                            if (netStreams.Count == 1)
                            {
                                netStreams[0].Item.SkipBecauseOfError = true;
                                netStreams[0].PlayState = NetStreamState.Playing;
                            }
                            else if (netStreams.Count > 1)
                            {
                                NetStreamHelper nsh = netStreams[netStreams.Count - 1];
                                netStreams.RemoveAt(netStreams.Count - 1);
                                netStreams[netStreams.Count - 1].NextNetStreamStarted = false;

                                nsh.Item.SkipBecauseOfError = true;
                                CloseAudioStream(nsh);

                                MPThread_CheckForPreBuffering();
                                break;
                            }
                        } //lock

                        if (currentPlaylist.NextMediaItemIndex != -1 && failedPlayCount <= 10)
                        {
                            // Continue with next one if there is one!
                            MPThread_Next();
                        }
                        else
                        {
                            // stop playing, 10 consecutive fails 
                            MPThread_Stop();
                        }
                        break;
                } //switch
            }
        }

        protected void NC_OnMediaPacket(object sender, TimeSpan timeStamp, byte[] data)
        {
            NetStreamHelper netStreamHelper = FindNetStreamHelper(sender as NetStream);
            if (netStreamHelper != null)
            {
                /*
                 * Test code to check for byte perfect copy
                 * 
                private System.IO.FileStream _FileStream = null; // put this in the class

                 * 
                if (netStreamHelper.Item.MediaFile == "JK142176-0002")
                {
                    if (_FileStream == null)
                    {
                        Console.WriteLine("!!!!!WRITING!!!!!!!!");
                        _FileStream = new System.IO.FileStream(netStreamHelper.Item.MediaFile + ".MP3", System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    }
                    _FileStream.Write(data, 0, data.Length);
                }
                else if (_FileStream != null)
                {
                    Console.WriteLine("!!!!!CLOSEING!!!!!!!!");
                    _FileStream.Close();
                    _FileStream = null;
                }
                */

                // Store data in buffer
                if (netStreamHelper.Buffer != null)
                {
                    netStreamHelper.Buffer.Write(data);

                    // When index > 0 we are buffering but not playing. Make sure we don't buffer to much data
                    if (netStreamHelper.IndexNumberInList > 0 && !netStreamHelper.NetStream.PauseIsActive)
                    {
                        // Prebuffer
                        // 15 seconds of data? (default for "ContentBufferTime")
                        //if (netStreamHelper.Buffer.UsedBytes >= (128 * 1024 * 8 * 10))
                        if (netStreamHelper.Buffer.UsedBytes >= (MP3_BITRATE * 8 * PREBUFFERTIME_IN_SECOND))
                        {
                            // pause stream! (otherwise stream will take to much memory)
                            netStreamHelper.NetStream.Pause(true);
                            DoEvent_MP_OnPreBuffer(OnPreBuffer, this, (MediaItem)netStreamHelper.Item.Clone(), PreBufferState.PrebufferingReady);
                        }
                    }
                }
                //Console.Write("\r" + netStreamHelper.Item.MediaFile + " ; UsedBytes=" + netStreamHelper.Buffer.UsedBytes.ToString());

                // When no bass channel has been created create it (but there must be enough data in the buffer!)
                if (netStreamHelper.BassHandle == 0 && netStreamHelper.Buffer != null && netStreamHelper.Buffer.UsedBytes >= BASS_MIN_INITIAL_FILLED_BUFFER)
                {
                    // bass needs for mp3 atleast 4000 bytes before is can play
                    // Startup new bassChannel we have enough data in the buffer to start playing
                    netStreamHelper.BassHandle = Bass.BASS_StreamCreateFileUser(BASSStreamSystem.STREAMFILE_BUFFERPUSH, BASSFlag.BASS_DEFAULT, bassFileProcs, GCHandle.ToIntPtr((GCHandle)gcHandle));
                    if (netStreamHelper.BassHandle != 0)
                    {
                        // Set volume for this channel
                        float volume = 100.0f;
                        lock (lockVAR)
                        {
                            volume = Convert.ToSingle(bassVolume) / 100.0f;
                        }
                        Bass.BASS_ChannelSetAttribute(netStreamHelper.BassHandle, BASSAttribute.BASS_ATTRIB_VOL, volume);

                        BASS_CHANNELINFO info = new BASS_CHANNELINFO();
                        Bass.BASS_ChannelGetInfo(netStreamHelper.BassHandle, info);
                        netStreamHelper.SampleRate = info.freq;
                        netStreamHelper.Channels = info.chans;
                        netStreamHelper.SampleSize = 16;
                        if ((info.flags & BASSFlag.BASS_SAMPLE_FLOAT) != 0) // 32-bit floating-point
                        {
                            netStreamHelper.SampleSize = 32;
                        }
                        else if ((info.flags & BASSFlag.BASS_SAMPLE_8BITS) != 0) // 8-bit
                        {
                            netStreamHelper.SampleSize = 8;
                        }

                        // make sure event funcs are called
                        Bass.BASS_ChannelSetSync(netStreamHelper.BassHandle, BASSSync.BASS_SYNC_STALL, 0, bassStalledSync, GCHandle.ToIntPtr((GCHandle)gcHandle));
                        Bass.BASS_ChannelSetSync(netStreamHelper.BassHandle, BASSSync.BASS_SYNC_END, 0, bassEndSync, GCHandle.ToIntPtr((GCHandle)gcHandle));

                        // Make sure the bass buffer is filled with enough data to start playing uninterrupted
                        // lock to make sure "netStreams" is protected (we're here on the thread of NetConnection not of MediaPlayer)
                        lock (lockVAR)
                        {
                            MPThread_FillBassAudioBuffer();
                        }
                    }
                }


                // -------------------------------------------------------------------------------------------------------------
                // Startup playing the sound if it is needed
                StartPlayingAudioChannel();
            }
        }

        private void StartPlayingAudioChannel()
        {
            MP_OnMediaItem onMediaItemStartPlay = null;
            MediaItem item = null;
            bool buttonStateChanged = false;
            lock (lockVAR)
            {
                if (netStreams.Count > 0)
                {
                    if (netStreams[0].PlayState != NetStreamState.Playing && netStreams[0].PlayState != NetStreamState.Pause && netStreams[0].BassHandle != 0)
                    {
                        bool wasSeeking = (netStreams[0].PlayState == NetStreamState.Seek);
                        netStreams[0].PlayState = NetStreamState.Playing;
                        buttonStateChanged = true;
                        // Startup playing the audio, bass is ready for it
                        // And start playing the music!
                        Bass.BASS_ChannelPlay(netStreams[0].BassHandle, false);
                        lastPosition = 0.0;
                        if (netStreams[0].NetStream.PauseIsActive)
                        {
                            netStreams[0].NetStream.Pause(false);
                        }

                        if (!wasSeeking && OnMediaItemStartPlay != null)
                        {
                            onMediaItemStartPlay = OnMediaItemStartPlay;
                            item = (MediaItem)netStreams[0].Item.Clone();
                        }
                    }
                }
            }

            // Fire event that MediaItem start playing audio (event will only be fired
            // when onMediaItemStartPlay and item are not null.)
            DoEvent_MP_OnMediaItem(onMediaItemStartPlay, this, item);

            if (buttonStateChanged)
            {
                //Button state has changed!
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        /// <summary>
        /// Close NetStream object (is allready removed from "netStreams"
        /// </summary>
        /// <param name="nsh"></param>
        private void CloseAudioStream(NetStreamHelper nsh)
        {
            nsh.NetStream.Close();
            nsh.NetStream = null;
            nsh.Buffer = null;
        }

        // ===========================================================================================================================================

        protected NetStreamHelper FindNetStreamHelper(NetStream netStream)
        {
            // find sender in netStreams list!
            // Most of the time it takes only 1 loop to find the right netstream
            NetStreamHelper netStreamHelper = null;
            foreach (NetStreamHelper nth in netStreams)
            {
                if (nth.NetStream.Equals(netStream))
                {
                    netStreamHelper = nth;
                    break;
                }
            } // foreach

            return netStreamHelper;
        }

        private NetStreamHelper NewNetStreamHelper(MediaItem item)
        {
            NetStreamHelper nth = null;
            if (netConnection != null && netConnection.IsConnected)
            {
                nth = new NetStreamHelper();
                nth.NetStream = new NetStream(netConnection, false);
                nth.NetStream.OnAssignStream_ID += new NS_OnAssignStream_ID(NS_OnAssignStream_ID);

                nth.Item = item;
                nth.Buffer = new CircularBlockBuffer(Convert.ToInt32(((128 * 1024) / 8) * 30)); // 30 seconden buffer voor 128kbit mp3 stream (= 480kb)
                // renumber
                for (int i = 0; i < netStreams.Count; i++)
                {
                    netStreams[i].IndexNumberInList = i;
                } //for
                nth.IndexNumberInList = netStreams.Count;
            }

            return nth;
        }

        #endregion


        #region Public interface functions

        /// <summary>
        /// Only valid to set when nothing is playing and stop is called
        /// otherwhise things will go horribly wrong
        /// </summary>
        public ServerLink RTMPServerLink
        {
            get
            {
                return rtmpServerLink;
            }
            set
            {
                MPThread_Disconnect();

                lock (lockVAR)
                {
                    rtmpServerLink = value;
                }
            }
        }

        /// <summary>
        /// Connect to RTMP server using RTMPServerLink settings
        /// When "Mediaplayer" is connected the "OnServerConnect" event will be fired
        /// </summary>
        public void Connect()
        {
            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Connect;

            AddMessageToPump(message);
        }

        /// <summary>
        /// Stops playing audio, disconnects any netStream connections and as
        /// last disconnects the NetConnection
        /// </summary>
        public void Disconnect()
        {
            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Disconnect;

            AddMessageToPump(message);
        }

        /// <summary>
        /// Return state where mediaplayer is in
        /// </summary>
        public MediaplayerState MediaplayerState
        {
            get
            {
                return mediaplayerState;
            }
        }

        /// <summary>
        /// Is the mediaplayer connected to a remote RTMP server
        /// when yes, play command's will work, otherwhise playcommand
        /// won't do anything.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return (netConnection != null && netConnection.IsConnected);
            }
        }

        /// <summary>
        /// returns true when last connect attempt failed
        /// </summary>
        public bool LastConnectFailed
        {
            get
            {
                return lastConnectFailed;
            }
        }

        /// <summary>
        /// Get/Set volume FOR CHANNEL (not the device volume!!) between 0 and 100 (0=off 100=max)
        /// </summary>
        public byte Volume
        {
            get
            {
                byte volume = 100;

                lock (lockVAR)
                {
                    volume = bassVolume;
                    if (netStreams != null && netStreams.Count > 0 && netStreams[0].BassHandle != 0)
                    {
                        float value = 0;

                        if (Bass.BASS_ChannelGetAttribute(netStreams[0].BassHandle, BASSAttribute.BASS_ATTRIB_VOL, ref value))
                        {
                            volume = Convert.ToByte(value * 100);
                        }
                    }
                }

                return volume;
            }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    float newValue = Convert.ToSingle(value) / 100.0f;

                    lock (lockVAR)
                    {
                        if (netStreams != null && netStreams.Count > 0 && netStreams[0].BassHandle != 0)
                        {
                            Bass.BASS_ChannelSetAttribute(netStreams[0].BassHandle, BASSAttribute.BASS_ATTRIB_VOL, newValue);

                            // retrive gto get accurate new value (as per 
                            if (Bass.BASS_ChannelGetAttribute(netStreams[0].BassHandle, BASSAttribute.BASS_ATTRIB_VOL, ref newValue))
                            {
                                value = Convert.ToByte(newValue * 100);
                            }
                        }

                        bassVolume = value; // for new channels we use this value
                    } //lock
                }
            }
        }

        public bool Play(long startPositionInMS = -1)
        {
            if (netConnection == null || !netConnection.IsConnected)
            {
                return false;
            }

            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Play;
            message.Params = new object[] { startPositionInMS };

            AddMessageToPump(message);

            return true;
        }

        public bool Pause()
        {
            if (netConnection == null || !netConnection.IsConnected)
            {
                return false;
            }

            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Pause;

            AddMessageToPump(message);

            return true;
        }

        public bool Stop()
        {
            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Stop;

            AddMessageToPump(message);

            return true;
        }

        public bool Seek(long seekTimeInMS)
        {
            // Allowed?
            if (!IsPlaying)
            {
                return false;
            }

            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Seek;
            message.Params = new object[] { seekTimeInMS };
            AddMessageToPump(message);

            return true;
        }

        /// <summary>
        /// Goto the next track in the playlist
        /// </summary>
        public bool Next()
        {
            // Allowed?
            if (nextButton == MPButtonState.Inactive)
            {
                return false;
            }
            
            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Next;
            AddMessageToPump(message);

            return true;
        }

        /// Goto the previous track in the playlist
        public bool Previous()
        {
            // Allowed?
            if (previousButton == MPButtonState.Inactive)
            {
                return false;
            }

            DoEvent_MP_OnControleButtonStateChange_DisableTemporary(OnControleButtonStateChange, this);

            MP_Message message = new MP_Message();
            message.MethodCall = MP_MethodCall.Previous;
            AddMessageToPump(message);

            return true;
        }

        public bool IsStopped
        {
            get
            {
                lock (lockVAR)
                {
                    if (netStreams == null || netStreams.Count == 0)
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        public bool IsPausing
        {
            get
            {
                lock (lockVAR)
                {
                    if (netStreams != null && netStreams.Count > 0)
                    {
                        return (netStreams[0].PlayState == NetStreamState.Pause);
                    }

                    return false;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (lockVAR)
                {
                    if (netStreams != null && netStreams.Count > 0)
                    {
                        return (netStreams[0].PlayState == NetStreamState.Playing || netStreams[0].PlayState == NetStreamState.Connecting || netStreams[0].PlayState == NetStreamState.Seek);
                    }

                    return false;
                }
            }
        }

        public bool IsSeeking
        {
            get
            {
                lock (lockVAR)
                {
                    if (netStreams != null && netStreams.Count > 0)
                    {
                        return (netStreams[0].PlayState == NetStreamState.Seek);
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// Time in seconds
        /// </summary>
        public double Position
        {
            get
            {
                lock (lockVAR)
                {
                    NetStreamHelper nsh = ActiveNetStreamHelper();
                    if (nsh != null)
                    {
                        // save last position, for pause mode and when we suddenly lose rtmp server connection
                        lastPosition = Bass.BASS_ChannelBytes2Seconds(nsh.BassHandle, GetPositionByte(nsh));
                        return lastPosition;
                    }
                    if (currentPlaylist.CurrentMediaItem != null)
                    {
                        return lastPosition;
                    }
                } //lock

                return 0.0;
            }
        }

        /// <summary>
        /// Time in seconds
        /// </summary>
        public double Duration
        {
            get
            {
                return (DurationInMS / 1000.0);
            }
        }

        /// <summary>
        /// Time in milliseconds
        /// </summary>
        public double DurationInMS
        {
            get
            {
                lock (lockVAR)
                {
                    NetStreamHelper nsh = ActiveNetStreamHelper();
                    if (nsh != null)
                    {                            
                        return (double)nsh.Item.DurationInMS;
                    }
                    else
                    {
                        MediaItem item = currentPlaylist.CurrentMediaItem;
                        if (item != null)
                        {
                            return (double)item.DurationInMS;
                        }
                    }
                } //lock

                return 0.0;
            }
        }

        /// <summary>
        /// Returns number of seconds that are buffered
        /// </summary>
        public double Buffered
        {
            // kbps (kilobit = 1000 !! NOT 1024 see wikipedia: http://en.wikipedia.org/wiki/Kilobit)

            get
            {
                lock (lockVAR)
                {
                    NetStreamHelper nsh = ActiveNetStreamHelper();
                    if (nsh != null)
                    {
                        // We need to now the mp3 bitrate to calculate the seconds!
                        return audioInBassBuffer + (nsh.Buffer.UsedBytes / ((128 * 1000) / 8));
                    }
                } //lock

                return 0.0;
            }
        }

        /// <summary>
        /// get/set Mediaplayer Repeatmode
        /// </summary>
        public PlaylistRepeatMode RepeatMode
        {
            get
            {
                return repeatMode;
            }
            set
            {
                repeatMode = value;
                foreach (Playlist playlist in playlists)
                {
                    playlist.ChangeRepeatMode(repeatMode);
                } //foreach

                // Button are probably changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        /// <summary>
        /// Is shufflemode turned on or not
        /// </summary>
        public bool ShuffleMode
        {
            get
            {
                return currentPlaylist.ShuffleMode;
            }
            set
            {
                currentPlaylist.ChangeShuffleMode(value);

                // Button are probably changed
                DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
            }
        }

        /// <summary>
        /// The currentPlaylist. Warning the Mediaplayer uses this extensively in 
        /// it's own thread!
        /// </summary>
        public Playlist CurrentPlaylist
        {
            get
            {
                return currentPlaylist;
            }
        }

        /// <summary>
        /// Fires the 3 events:
        ///   OnCurrentMediaItemChanged
        ///   OnPreviousMediaItemChanged
        ///   OnNextMediaItemChanged
        ///   
        /// the parameter oldMediaItem is always null 
        /// </summary>
        public void TriggerMediaItemEvents()
        {
            MediaItem currentMediaItem = null;
            MediaItem nextMediaItem = null;
            MediaItem previousMediaItem = null;
            lock (lockVAR)
            {
                currentMediaItem = currentPlaylist.CurrentMediaItem;
                nextMediaItem = currentPlaylist.NextMediaItem;
                previousMediaItem = currentPlaylist.PreviousMediaItem;
            } //lock

            // fire the events
            DoOnCurrentMediaItemChanged(this, null, currentMediaItem);
            DoOnPreviousMediaItemChanged(this, null, previousMediaItem);
            DoOnNextMediaItemChanged(this, null, nextMediaItem);
        }

        public void TriggerButtonStateEvent()
        {
            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this, true);
        }

        /// <summary>
        /// Get MediaItem position in CurrentPlaylist or -1 when not found,
        /// need for "InsertMediaItem(int atPosition, MediaItem item)"
        /// </summary>
        public int MediaItemPosition(MediaItem item)
        {
            return currentPlaylist.GetMediaItemIndex(item);
        }

        /// <summary>
        /// Insert a MediaItem at the specified location.
        /// </summary>
        public bool InsertMediaItem(int atPosition, MediaItem item)
        {
            MediaItem preBufferedItem = null;
            lock (lockVAR)
            {
                if (netStreams.Count > 1)
                {
                    preBufferedItem = netStreams[1].Item;
                }
            } //lock

            bool result = currentPlaylist.InsertMediaItem(atPosition, item);

            MediaItem nextBufferedItem = currentPlaylist.NextMediaItem;

            if (preBufferedItem != null && nextBufferedItem != null && item.GUID.Equals(nextBufferedItem.GUID))
            {
                MP_Message message = new MP_Message();
                message.MethodCall = MP_MethodCall.KillPreBufferedMediaItem;
                AddMessageToPump(message);
            }

            return result;
        }

        /// <summary>
        /// Insert a MediaItem at the specified location.
        /// </summary>
        public bool InsertMediaItem(PlaylistPosition position, MediaItem item)
        {
            return InsertMediaItem(currentPlaylist.AtPositionInsertMediaItem(position), item);
        }

        /// <summary>
        /// Remove and MediaItem from the playlist
        /// </summary>
        public bool RemoveMediaItem(int index)
        {
            MediaItem removeItem = null;
            MediaItem currentItem = null;
            MediaItem preBufferedItem = null;
            lock (lockVAR)
            {
                removeItem = currentPlaylist[index];
                if (netStreams.Count > 0)
                {
                    currentItem = netStreams[0].Item;
                }
                if (netStreams.Count > 1)
                {
                    preBufferedItem = netStreams[1].Item;
                }
            } //lock

            if (currentItem != null && removeItem != null && currentItem.GUID.Equals(removeItem.GUID))
            {
                // stop playing!!
                Stop();
                Thread.Sleep(100);
            }

            bool result = currentPlaylist.RemoveMediaItem(index);

            if (preBufferedItem != null && preBufferedItem.GUID.Equals(removeItem.GUID))
            {
                MP_Message message = new MP_Message();
                message.MethodCall = MP_MethodCall.KillPreBufferedMediaItem;
                AddMessageToPump(message);
            }

            return result;
        }

        /// <summary>
        /// Remove and MediaItem from the playlist
        /// </summary>
        public bool RemoveMediaItem(Guid GUID)
        {
            return RemoveMediaItem(currentPlaylist.GetMediaItemIndex(GUID));
        }

        /// <summary>
        /// Remove and MediaItem from the playlist
        /// </summary>
        public bool RemoveMediaItem(MediaItem item)
        {
            return RemoveMediaItem(item.GUID);
        }

        /// <summary>
        /// Can be null when there is no previous item.
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        public MediaItem PreviousMediaItem
        {
            get
            {
                return currentPlaylist.PreviousMediaItem;
            }
        }

        /// <summary>
        /// Can be null when there is no currentmediaitem
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        public MediaItem CurrentMediaItem
        {
            get
            {
                return currentPlaylist.CurrentMediaItem;
            }
        }

        /// <summary>
        /// Can be null when there is no next item.
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        public MediaItem NextMediaItem
        {
            get
            {
                return currentPlaylist.NextMediaItem;
            }
        }

        /// <summary>
        /// Remove all MediaItems from the playlist
        /// </summary>
        public void ClearPlaylist()
        {
            Stop();
            Thread.Sleep(100);

            currentPlaylist.Clear();
        }

        /// <summary>
        /// Number of MediaItems in the playlist
        /// </summary>
        public int PlaylistCount
        {
            get
            {
                return currentPlaylist.Count;
            }
        }

        /// <summary>
        /// Change the to be played MediaItem
        /// </summary>
        public bool ChangeCurrentMediaItemIndex(int newIndex, bool addCurrentToPreviousHistory)
        {
            return currentPlaylist.ChangeCurrentMediaItemIndex(newIndex, addCurrentToPreviousHistory);
        }

        /// <summary>
        /// Change the to be played MediaItem
        /// </summary>
        public bool ChangeCurrentMediaItem(MediaItem newItem, bool addCurrentToPreviousHistory)
        {
            return currentPlaylist.ChangeCurrentMediaItem(newItem, addCurrentToPreviousHistory);
        }

        public MPButtonState PreviousButton
        {
            get
            {
                return previousButton;
            }
        }

        public MPButtonState StopButton
        {
            get
            {
                return stopButton;
            }
        }

        public MPButtonState PlayButton
        {
            get
            {
                return playButton;
            }
        }

        public MPButtonState PauseButton
        {
            get
            {
                return pauseButton;
            }
        }

        public MPButtonState NextButton
        {
            get
            {
                return nextButton;
            }
        }

        public int PlaylistBlockChangeEvent()
        {
            disablePlaylistChangeEvent++;
            return disablePlaylistChangeEvent;
        }

        public int PlaylistUnblockChangeEvent()
        {
            if (disablePlaylistChangeEvent > 0)
            {
                disablePlaylistChangeEvent--;
            }
            return disablePlaylistChangeEvent;
        }

        #endregion


        #region Event generation

        private void DoOnPlaylistChanged(object sender, Playlist playlist)
        {
            if (OnPlaylistChanged != null && disablePlaylistChangeEvent == 0)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { OnPlaylistChanged, this, playlist };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }
    
        private void DoOnCurrentMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (OnCurrentMediaItemChanged != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { OnCurrentMediaItemChanged, this, oldMediaItem, newMediaItem };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }

            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
        }

        private void DoOnPreviousMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (OnPreviousMediaItemChanged != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { OnPreviousMediaItemChanged, this, oldMediaItem, newMediaItem };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }

            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
        }

        private void DoOnNextMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (OnNextMediaItemChanged != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { OnNextMediaItemChanged, this, oldMediaItem, newMediaItem };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }

            DoEvent_MP_OnControleButtonStateChange(OnControleButtonStateChange, this);
        }

        protected void DoEvent_MP_OnStateChangeMediaplayer(MP_OnStateChangeMediaplayer doEvent, object sender, MediaplayerState state)
        {
            if (doEvent != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender, state };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        private void DoEvent_MP_OnServer(MP_OnServer doEvent, object sender)
        {
            if (doEvent != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        private void DoEvent_MP_OnPlaylist(MP_OnPlaylist doEvent, object sender, Playlist playlist)
        {
            if (doEvent != null && playlist != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender, playlist };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        private void DoEvent_MP_OnMediaItem(MP_OnMediaItem doEvent, object sender, MediaItem mediaItem)
        {
            if (doEvent != null && mediaItem != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender, mediaItem };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        private void DoEvent_MP_OnPreBuffer(MP_OnPreBuffer doEvent, object sender, MediaItem mediaItem, PreBufferState state)
        {
            if (doEvent != null && mediaItem != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender, mediaItem, state };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        private void DoEvent_MP_OnControleButtonStateChange(MP_OnControleButtonStateChange doEvent, object sender, bool forceEvent = false)
        {
            MPButtonState oldPreviousButton = previousButton;
            MPButtonState oldStopButton = stopButton;
            MPButtonState oldPlayButton = playButton;
            MPButtonState oldPauseButton = pauseButton;
            MPButtonState oldnextButton = nextButton;

            // Determine the buttonstates
            lock (lockVAR)
            {
                if (netConnection == null || !netConnection.IsConnected || currentPlaylist.Count <= 0 || commandInProgress)
                {
                    // we're not connected to a server, of nothing to play
                    previousButton = MPButtonState.Inactive;
                    stopButton = MPButtonState.Inactive;
                    playButton = MPButtonState.Inactive;
                    pauseButton = MPButtonState.Inactive;
                    nextButton = MPButtonState.Inactive;
                }
                else if (netStreams.Count == 0)
                {
                    // we're connected to server, but not playing
                    previousButton = MPButtonState.Inactive;
                    stopButton = MPButtonState.Inactive;
                    playButton = MPButtonState.Active;
                    pauseButton = MPButtonState.Inactive;
                    nextButton = MPButtonState.Inactive;
                }
                else if (netStreams.Count > 0)
                {
                    // we're playing music of somekind
                    previousButton = MPButtonState.Inactive;
                    // previous is also active after 10 seconds have played
                    if (currentPlaylist.PreviousMediaItemIndex != -1 || Position > PREVIOUS_CMD_IS_GOTOBEGIN_IN_SEC)
                    {
                        previousButton = MPButtonState.Active;
                    }

                    nextButton = MPButtonState.Inactive;
                    if (currentPlaylist.NextMediaItemIndex != -1)
                    {
                        nextButton = MPButtonState.Active;
                    }

                    switch (netStreams[0].PlayState)
                    {
                        case NetStreamState.None: // ?????????
                            stopButton = MPButtonState.Inactive;
                            playButton = MPButtonState.Inactive;
                            pauseButton = MPButtonState.Inactive;

                            previousButton = MPButtonState.Inactive;
                            nextButton = MPButtonState.Inactive;
                            break;
                        case NetStreamState.Connecting:
                            stopButton = MPButtonState.Inactive;
                            playButton = MPButtonState.Inactive;
                            pauseButton = MPButtonState.Inactive;

                            previousButton = MPButtonState.Inactive;
                            nextButton = MPButtonState.Inactive;
                            break;
                        case NetStreamState.Playing:
                            stopButton = MPButtonState.Active;
                            playButton = MPButtonState.Inactive;
                            pauseButton = MPButtonState.Active;
                            break;
                        case NetStreamState.Seek:
                            stopButton = MPButtonState.Inactive;
                            playButton = MPButtonState.Inactive;
                            pauseButton = MPButtonState.Inactive;

                            previousButton = MPButtonState.Inactive;
                            nextButton = MPButtonState.Inactive;
                            break;
                        case NetStreamState.Pause:
                            stopButton = MPButtonState.Active;
                            playButton = MPButtonState.Active;
                            pauseButton = MPButtonState.Inactive;
                            break;
                    } //switch
                }
            } //lock

            /*
            Console.WriteLine("--");
            Console.WriteLine("previous:" + previousButton.ToString());
            Console.WriteLine("stop:" + stopButton.ToString());
            Console.WriteLine("play:" + playButton.ToString());
            Console.WriteLine("pause:" + pauseButton.ToString());
            Console.WriteLine("next:" + nextButton.ToString());
            */

            // when state of one or more buttons is changed fire an event!
            if (forceEvent || oldPreviousButton != previousButton || oldStopButton != stopButton || oldPlayButton != playButton || oldPauseButton != pauseButton || oldnextButton != nextButton)
            {
                if (doEvent != null)
                {
                    MP_Params param = new MP_Params();
                    param.Params = new object[] { doEvent, sender, previousButton, stopButton, playButton, pauseButton, nextButton };

                    SynchronizationContext sc;
                    lock (lockVAR)
                    {
                        sc = synchronizationContext;
                    } //lock
                    if (sc != null)
                    {
                        sc.Post(HandleOnEventCallUserCode, param);
                    }
                    else
                    {
                        HandleOnEventCallUserCode(param);
                    }
                }
            }
        }

        /// <summary>
        /// Fire event to diable button states temporary, will be activated after command has done something
        /// </summary>
        /// <param name="doEvent"></param>
        /// <param name="sender"></param>
        private void DoEvent_MP_OnControleButtonStateChange_DisableTemporary(MP_OnControleButtonStateChange doEvent, object sender)
        {
            // Disable all button states (will be recalculated when normal event 
            // is fired
            previousButton = MPButtonState.Inactive;
            stopButton = MPButtonState.Inactive;
            playButton = MPButtonState.Inactive;
            pauseButton = MPButtonState.Inactive;
            nextButton = MPButtonState.Inactive;

            if (doEvent != null)
            {
                MP_Params param = new MP_Params();
                param.Params = new object[] { doEvent, sender, MPButtonState.Inactive, MPButtonState.Inactive, MPButtonState.Inactive, MPButtonState.Inactive, MPButtonState.Inactive };

                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    sc.Post(HandleOnEventCallUserCode, param);
                }
                else
                {
                    HandleOnEventCallUserCode(param);
                }
            }
        }

        /// <summary>
        /// Fire OnTick event on the right thread
        /// </summary>
        private bool inOnTickEvent = false;
        private void DoOnTickEvent(MP_OnTick onTick, object sender)
        {
            if (inOnTickEvent || onTick == null)
            {
                return;
            }

            inOnTickEvent = true;
            try
            {
                SynchronizationContext sc;
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                } //lock
                if (sc != null)
                {
                    // Always use "Send" so we can protect against overrunning
                    sc.Send(new SendOrPostCallback(delegate(object state)
                    {
                        try
                        {
                            // This is on other thread
                            (state as MP_OnTick)(sender);
                        }
                        catch (Exception e)
                        {
                            LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.Media.Mediaplayer.DoOnTickEvent] Catched unhandled user exception: {0}", e.ToString()));
                        }
                    }), onTick);
                }
                else
                {
                    onTick(this);
                }
            }
            finally
            {
                inOnTickEvent = false;
            }
        }


        /// <summary>
        /// Send the event to the user
        /// </summary>
        /// <param name="state"></param>
        protected virtual void HandleOnEventCallUserCode(object state)
        {
            try
            {
                if (!(state is MP_Params))
                {
                    return;
                }
                MP_Params param = state as MP_Params;

                if (param.Params[0] is MP_OnServer)
                {
                    (param.Params[0] as MP_OnServer)(param.Params[1]);
                }
                else if (param.Params[0] is PL_OnMediaItemChanged)
                {
                    (param.Params[0] as PL_OnMediaItemChanged)(param.Params[1], (MediaItem)param.Params[2], (MediaItem)param.Params[3]);
                }
                else if (param.Params[0] is MP_OnPlaylist)
                {
                    (param.Params[0] as MP_OnPlaylist)(param.Params[1], (Playlist)param.Params[2]);
                }
                else if (param.Params[0] is PL_OnPlaylistChanged)
                {
                    (param.Params[0] as PL_OnPlaylistChanged)(param.Params[1], (Playlist)param.Params[2]);
                }
                else if (param.Params[0] is MP_OnMediaItem)
                {
                    (param.Params[0] as MP_OnMediaItem)(param.Params[1], (MediaItem)param.Params[2]);
                }
                else if (param.Params[0] is MP_OnPreBuffer)
                {
                    (param.Params[0] as MP_OnPreBuffer)(param.Params[1], (MediaItem)param.Params[2], (PreBufferState)param.Params[3]);
                }
                else if (param.Params[0] is MP_OnStateChangeMediaplayer)
                {
                    (param.Params[0] as MP_OnStateChangeMediaplayer)(param.Params[1], (MediaplayerState)param.Params[2]);
                }
                else if (param.Params[0] is MP_OnControleButtonStateChange)
                {
                    (param.Params[0] as MP_OnControleButtonStateChange)(param.Params[1], (MPButtonState)param.Params[2], (MPButtonState)param.Params[3], (MPButtonState)param.Params[4], (MPButtonState)param.Params[5], (MPButtonState)param.Params[6]);
                }
                else if (param.Params[0] is MP_OnTick)
                {
                    (param.Params[0] as MP_OnTick)(param.Params[1]);
                }
            }
            catch (Exception e)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.Media.Mediaplayer.HandleOnEventCallUserCode] Catched unhandled user exception: {0}", e.ToString()));
            }
        }

        #endregion


        #region Helper functions

        /// <summary>
        /// Caller must lock this against multithread access
        /// </summary>
        private NetStreamHelper ActiveNetStreamHelper()
        {
            lock (lockVAR)
            {
                if (netStreams != null && netStreams.Count > 0)
                {
                    return netStreams[0];
                }
            } //lock

            return null;
        }

        private long GetPositionByte(NetStreamHelper nsh)
        {
            if (nsh != null)
            {
                long position = Bass.BASS_ChannelGetPosition(nsh.BassHandle);
                // calculate delta
                if (nsh.NetStream.DeltaTimeStampInMS != 0)
                {
                    position += Convert.ToInt64((nsh.NetStream.DeltaTimeStampInMS / 1000) * nsh.SampleRate * nsh.Channels * (nsh.SampleSize / 8));
                }

                // make it relative to the "song" we are playing
                return position;
            }

            return -1;
        }


        /// <summary>
        /// Add new message to queue. Thread safe
        /// </summary>
        private void AddMessageToPump(MP_Message message)
        {
            lock (lockVAR)
            {
                if (messageQueue != null)
                {
                    messageQueue.Add(message);
                }
            } //lock
        }

        /// <summary>
        /// Requeue message (eg when a command is allready being processed
        /// we cannot do other things until command is completly processed)
        /// </summary>
        private void RequeueMessage(MP_Message message)
        {
            lock (lockVAR)
            {
                if (messageQueue != null && message != null)
                {
                    messageQueue.Insert(0, message);
                }
            } //lock
        }

        #endregion


        #region Private classes and enumerations

        protected class MP_Params
        {
            public object[] Params;
        }

        protected enum MP_MethodCall
        {
            None = 0,
            Connect,
            Disconnect,
            Play,
            Pause,
            Stop,
            KillPreBufferedMediaItem,
            Seek,
            Next,
            Previous
        }

        protected class MP_Message
        {
            public MP_MethodCall MethodCall;
            public object[] Params;
        }

        protected enum NetStreamState
        {
            None = 0,
            Connecting,
            Playing,
            Pause,
            Seek
        }

        protected class NetStreamHelper
        {
            public int IndexNumberInList = -1;
            public bool NextNetStreamStarted = false;
            public NetStream NetStream;
            public NetStreamState PlayState = NetStreamState.None;
            public bool IsComplete = false;

            public int BassHandle = 0;
            public int SampleRate = 0;
            public int Channels = 0;
            public int SampleSize = 0;
            public bool BassEndOfFileSend = false;

            public MediaItem Item;
            public CircularBlockBuffer Buffer; // not very memory efficient if used in this way!!

            /// <summary>
            /// Stop playing, close bass stream, reset vars except netstream!
            /// </summary>
            public void Stop()
            {
                if (BassHandle != 0)
                {
                    // Stop playing audio
                    Bass.BASS_ChannelStop(BassHandle);
                    // Free resources
                    Bass.BASS_StreamFree(BassHandle);
                }

                PlayState = NetStreamState.None;
                Buffer.Clear();

                IsComplete = false;

                BassHandle = 0;
                SampleRate = 0;
                Channels = 0;
                SampleSize = 0;
                BassEndOfFileSend = false;
            }

            public void Close()
            {
                Stop();
                if (NetStream != null)
                {
                    NetStream.Close();
                    NetStream = null;
                }
                Buffer = null;
            }
        }

        #endregion
    }



    public enum MediaplayerState
    {
        Disconnected = 0,
        Connecting,
        Stop,
        Playing,
        Pause,
    }

    public enum PreBufferState
    {
        Unknown = 0,                    // is not used, but is the default value for this enum
        PrebufferingStarted,            // send when we start buffering
        PrebufferingReady,              // send when x(15) seconds of data has been buffered
        PrebufferingEndedAndPlaying,    // MediaItem is now playing 
        PrebufferingEndedAndCanceled    // MediaItem is canceled (eg other MediaItem was inserted in playlist and will be played next, this item got canceled because of that)
    }

    public enum MPButtonState
    {
        Active = 0,
        Inactive
    }


    // Mediaplayer delegates

    public delegate void MP_OnServer(object sender);

    public delegate void MP_OnPlaylist(object sender, Playlist playlist);
    public delegate void MP_OnMediaItem(object sender, MediaItem item);
    public delegate void MP_OnStateChangeMediaplayer(object sender, MediaplayerState state);

    public delegate void MP_OnPreBuffer(object sender, MediaItem mediaItem, PreBufferState state);

    public delegate void MP_OnControleButtonStateChange(object sender, MPButtonState previousButton, MPButtonState stopButton, MPButtonState playButton, MPButtonState pauseButton, MPButtonState nextButton);

    public delegate void MP_OnTick(object sender);
}
