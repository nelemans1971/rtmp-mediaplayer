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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CDR.LibRTMP
{
    /*
    Two stream played after eachther without a seek in between
    (MP3:JK142176-0001-sample.MP3 Len=29936 & MP3:JK142176-0002-sample.MP3 Len=29936)

    These are the event received:

    (start van [MP3:JK142176-0001-sample.MP3])
    NetStream.Play.Reset      (TimeStamp = 0)
    NetStream.Play.Start      (TimeStamp = 0)
    NetStream.Data.Start      (TimeStamp = 0)
    NetStream.Play.Switch     (TimeStamp = 0)

    (Next mediafile is received [MP3:JK142176-0002-sample.MP3])

    NetStream.Play.Switch     (TimeStamp = 29910)
    NetStream.Play.Start      (TimeStamp = 29910)
    NetStream.Data.Start      (TimeStamp = 29910)
    NetStream.Play.Switch     (TimeStamp = 29910)

    (All data send nothing more in the playlist)

    NetStream.Play.Complete   (TimeStamp = 59820)
    NetStream.Play.Stop       (TimeStamp = 59820)

    -----------------------------------------------------------------------------------------

    Two streams are played, with a seek to 15000ms in the playlist
    (MP3:JK142176-0001-sample.MP3 Len=29936 & MP3:JK142176-0002-sample.MP3 Len=29936)

    NetStream.Play.Reset      (TimeStamp = 0)
    NetStream.Play.Start      (TimeStamp = 0)
    NetStream.Data.Start      (TimeStamp = 0)
    NetStream.Play.Switch     (TimeStamp = 0)

    (Seek command is send to rmp server)
    NetStream.Seek.Notify     (Timestamp=15000)
    NetStream.Play.Start      (Timestamp=15000)
    NetStream.Data.Start      (Timestamp=15000)
    NetStream.Play.Switch     (Timestamp=15000)

    (Mediafile is send [MP3:JK142176-0002-sample.MP3])

    NetStream.Play.Switch     (Timestamp=29968)
    NetStream.Play.Start      (Timestamp=29968)
    NetStream.Data.Start      (Timestamp=30093)
    NetStream.Play.Switch     (Timestamp=30093)

    NetStream.Play.Complete   (Timestamp=60003)
    NetStream.Play.Stop       (Timestamp=60003)


    Remarks:
     * The timestamps aren't exactly the same. Probably because of packet sizes
     * but because of this the last timestamp of a mediafile is also slightly different
     * This probably means the last timestamp is not the size of the complete mediafile
    */


    /// <summary>
    /// The netstream class is just sugar coating, because all the work is mainly done in
    /// the NetConnection class.
    /// </summary>
    public class NetStream : IDisposable
    {
        private const int INITIAL_AUDIO_STREAMBUFFER = 512 * 1024;

        private static int nextCommandChannel = 8;

        protected object lockVAR = new object();
        protected NetConnection netConnection;

        // ----------------------------------------------
        // All var are initialized by InitVars()
        // ----------------------------------------------
        private int stream_id = -1; // -1=no stream associated with this object, 0=command channel so also not used
        private int mediaChannel = -1; // -1=nochannel
        private int commandChannel = -1; // -1=nochannel

        private int contentBufferTime = -1; // buffer time of content data in milliseconds, will be set to 15000 when connectie is made (ig streamID is valid)

        private bool liveStream = true; // default is false, controle message 0x04 is send when it's recorded
        private long audioDatarate = 0;
        private long videoDatarate = 0;
        private long combinedTracksLength = 0;
        private AudioMetaData audioMetaData;

        public TimeSpan TimeStamp = TimeSpan.Zero;
        public RTMPPacket LastMediaPacket = null;
        private MemoryStream msAudioBuffer = null; // sse InitVars()
        private MemoryStream msVideoBuffer = null; // sse InitVars()
        private byte[] lastAudioPacket = null;
        private List<RTMPPacket> savedPackets = null; // Used after pause to recover from not found byte alignment
        private bool atBeginOfAudio = true; // for compress audio we need a minimum of data before you can begin to play (mp3 for example)

        private bool autoPlay = false; // only for user to start playing when data is received
        private object tag = null; // for user

        protected long mediaBytesReceived = 0;
        protected int blockMediaPackets = 0; // when playlist is running and we reset the list ("NetStream.Play.Switch") we wait until we get a "NetStream.Play.Start" before we deblock
        private bool syncAfterPauseNeeded = false; // used to match up data packet after first packet arrives after an unpause (dirty fix to match up buffers byte wize)
        private bool pauseIsActive = false;
        private bool seekIsActive = false;
        private uint deltaTimeStampInMS = 0; // eg when a seek is done timestamps start at 0, but normaly you want to now the real position
        private long metaDataDurationInMS = 0; // Duration of stream in milliseconds returned by Metadata                


        // Events are not done by InitVars()
        // ---------------------------------
        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NS_OnTick OnTick = null;

        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NS_OnAssignStream_ID OnAssignStream_ID = null;
        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NS_OnStatus OnStatus = null;
        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NS_OnID3 OnID3 = null;
        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NC_OnMediaPacket OnAudioPacket = null;
        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NC_OnMediaPacket OnVideoPacket = null;

        /// <summary>
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NS_OnPauseStream OnPauseStream = null;
        

        public NetStream()
        {
            InitVars();
        }

        public NetStream(NetConnection connection, bool autoConnect = true)
        {
            InitVars();
            netConnection = connection;

            if (autoConnect)
            {
                NetStreamInitialize();
            }
        }

        virtual protected bool NetStreamInitialize()
        {
            // InitVars is already called by empty constructor!
            if (netConnection != null)
            {
                audioMetaData = new AudioMetaData();
                audioMetaData.Clear();

                if (netConnection != null)
                {
                    // Set buffer time
                    netConnection.SendPing(3, 0, 300); // why everytime on stream_id 0??
                    // Create Stream for this NetStream
                    netConnection.CreateStream(this);

                    return true;
                }
            }

            return false;
        }

        private void InitVars()
        {
            lock (lockVAR)
            {
                stream_id = -1; // -1=no stream associated with this object, 0=command channel so also not used
                mediaChannel = -1; // -1=nochannel
                commandChannel = -1; // -1=nochannel

                contentBufferTime = -1; // buffer time of content data in milliseconds, will be set to 15000 when connectie is made (ig streamID is valid)

                liveStream = true; // default is false, controle message 0x04 is send when it's recorded
                audioDatarate = 0;
                videoDatarate = 0;
                combinedTracksLength = 0;

                audioMetaData = new AudioMetaData();

                TimeStamp = TimeSpan.Zero;
                LastMediaPacket = null;
                msAudioBuffer = new MemoryStream(INITIAL_AUDIO_STREAMBUFFER);
                msVideoBuffer = new MemoryStream();
                lastAudioPacket = null;
                savedPackets = null;
                atBeginOfAudio = true; // for compress audio we need a minimum of data before you can begin to play (mp3 for example)

                mediaBytesReceived = 0;
                blockMediaPackets = 0; // when playlist is running and we reset the list ("NetStream.Play.Switch") we wait until we get a "NetStream.Play.Start" before we deblock
                syncAfterPauseNeeded = false;
                pauseIsActive = false;
                seekIsActive = false;
                deltaTimeStampInMS = 0;
                metaDataDurationInMS = 0; // Duration of stream in milliseconds returned by Metadata                
            } //lock
        }

        public virtual bool Connect()
        {
            return NetStreamInitialize();
        }
        
        public virtual void Close()
        {
            // Do something
            if (netConnection != null)
            {
                if (stream_id > 0)
                {
                    netConnection.DeleteStream(stream_id);
                }
                netConnection.UnRegisterNetStream(this);
            }

            // Now reset stream_id (we are nog connected anymore to a NetConnection)
            stream_id = -1;
            // probaly already erased by UnRegisterNetStream
            netConnection = null;
            InitVars();
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
                catch { }
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
        ~NetStream()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion

        public int Stream_ID
        {
            get
            {
                return stream_id;
            }
            internal set
            {
                stream_id = value;

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream] Stream_ID set to <{0}>.", stream_id));
            }
        }

        public int MediaChannel
        {
            get
            {
                return mediaChannel;
            }
            internal set
            {
                mediaChannel = value;
            }

        }

        public int CommandChannel
        {
            get
            {
                if (commandChannel < 0)
                {
                    commandChannel = nextCommandChannel++;
                    if (nextCommandChannel > RTMPConst.RTMP_CHANNELS)
                    {
                        nextCommandChannel = 8; //start at beginning
                    }
                }
                return commandChannel;
            }
        }

        /// <summary>
        /// Return is this NetStream is connected to an RTMP server and 
        /// has it's own channel (stream_id).
        /// </summary>
        public bool IsConnected
        {
            get
            {
                // This NetStream object is only connected when it has a valid stream_id
                // and is still connected to a connected NetConnection
                if (netConnection != null && stream_id > 0)
                {
                    return netConnection.IsConnected;
                }

                return false;
            }
        }

        public bool WaitForValidStream_ID(int waitTimeInMS)
        {
            int i = 0;
            while (stream_id <= 0 && i <= waitTimeInMS)
            {
                Thread.Sleep(100);
                i += 100;
            }

            return (stream_id > 0);
        }

        /// <summary>
        /// Play a media file from a RTMP (Flash) Media Server.
        /// </summary>
        /// <param name="mediaFile">
        /// The name of a recorded file, an identifier for live data published , or false. 
        /// If false, the stream stops playing and any additional parameters are ignored. 
        /// </param>
        /// <param name="start/seekTime">
        /// The start time, in seconds. Allowed values are -2, -1, 0, or a positive number. 
        /// The default value is -2, which looks for a live stream, then a recorded stream, 
        /// and if it finds neither, opens a live stream. You cannot use -2 with MP3 files. 
        /// If -1, plays only a live stream. If 0 or a positive number, plays a recorded stream, 
        /// beginning start milliseconds in. </param>
        /// <param name="lenToPlay">
        /// The duration of the playback, in seconds. Allowed values are -1, 0, or a 
        /// positive number. The default value is -1, which plays a live or recorded stream 
        /// until it ends. If 0, plays a single frame that is start seconds from the 
        /// beginning of a recorded stream. If a positive number, plays a live or recorded 
        /// stream for len seconds. 
        /// </param>
        /// <param name="resetPlayList">
        /// Whether to clear a playlist. The default value is 1 or true, which clears any 
        /// previous play calls and plays name immediately. If 0 or false, adds the stream 
        /// to a playlist. If 2, maintains the playlist and returns all stream messages at 
        /// once, rather than at intervals. If 3, clears the playlist and returns all 
        /// stream messages at once. 
        /// </param>
        public virtual void Play(string mediaFile, int start = 0, int lenToPlay = -1, bool resetPlayList = true)
        {
            Play(mediaFile, 0, lenToPlay, resetPlayList, null);
        }

        public virtual void Play(string mediaFile, int start, int lenToPlay, bool resetPlayList, AMFObjectProperty properties)
        {
            if (!CheckConnection())
            {
                return;
            }

            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.Play;
            message.Params = new object[] { this, mediaFile, start, lenToPlay, resetPlayList, properties };

            netConnection.PostMessage(message);
            
        }


        public void Play2()
        {
            throw new Exception("Not supported");
        }

        public void CloseStream()
        {
            // Only needed when there is a valid stream
            if (stream_id < 0)
            {
                return;
            }

            if (!CheckConnection())
            {
                return;
            }

            // needed to make sure the channel wil start streaming again when a new play command
            // is send (verry important!)
            if (pauseIsActive)
            {
                Pause(false);
            }

            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.CloseStream;
            message.Params = new object[] { this };

            netConnection.PostMessage(message);
        }

        public void DeleteStream()
        {
            throw new Exception("Not supported");
        }

        public void ReceiveAudio()
        {
            throw new Exception("Not supported");
        }

        public void ReceiveVideo()
        {
            throw new Exception("Not supported");
        }

        public void Publish()
        {
            throw new Exception("Not supported");
        }

        virtual public void Seek(long positionInMS)
        {
            if (!CheckConnection() && seekIsActive)
            {
                return;
            }

            seekIsActive = true;
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.Seek;
            message.Params = new object[] { this, positionInMS };

            netConnection.PostMessage(message);
        }

        public void Pause(bool doPause)
        {
            if (!CheckConnection())
            {
                return;
            }

            pauseIsActive = true;
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.Pause;
            message.Params = new object[] { this, doPause };

            netConnection.PostMessage(message, true);
        }

        /// <summary>
        /// Set number of milliseconds of content data which are buffered on the client
        /// 
        /// Can only be set in "NS_OnAssignStream_ID" event or after this event has been 
        /// fired.
        /// </summary>
        public int ContentBufferTime
        {
            get
            {
                return contentBufferTime;
            }
            set
            {
                if (stream_id > 0 && value > 0 && value != contentBufferTime)
                {
                    MQ_RTMPMessage message = new MQ_RTMPMessage();
                    message.MethodCall = MethodCall.MQInternal_SetContentBufferTime;
                    message.Params = new object[] { this, contentBufferTime };

                    netConnection.PostMessage(message);
                }
            }
        }

        /// <summary>
        /// Check is there is an connection and the stream_id is valid else 
        /// returns false (because of different threads main and netconnectionm one
        /// we can not throw exc'eption because you can't test on it before 
        /// a call is made. So we let it go silently
        /// </summary>
        private bool CheckConnection()
        {
            if (netConnection == null)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, "NetStream needs a valid NetConnection.");
                return false;
            }

            if (stream_id <= 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, "NetStream needs a valid stream_id to send commands over.");
                return false;
            }

            if (!netConnection.IsConnected)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, "NetConnection isn't connected to a RTMP server.");
                return false;
            }

            return true;
        }

        #region Internal Handle functions

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal void internal_NetStreamDisconnectNotify()
        {
            // the NetConnection has been disconnected from it's RTMP server. Dealwith it
            // if disconnect was request then deleteStream commands were send

            // Now reset stream_id (we are nog connected anymore to a NetConnection)
            stream_id = -1;

            InitVars();
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal bool SeekIsWithinBounderies(long seekTime)
        {
            return true;
        }

        /// <summary>
        /// This class and/or it's dirved class get some time from the netconnection thread
        /// to doe "something" NetStreamBass for example can use it to move it's 
        /// internal; buffer to the 2 second buffer of bass
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal void HandleOnTimeSlice()
        {
        }

        /// <summary>
        /// The NetConnection give a tick 
        /// </summary>
        internal void HandleOnTick()
        {
            if (OnTick != null)
            {
                DoOnTickEvent(OnTick);
            }
        }

        /// <summary>
        /// Fire OnTick event on the right thread
        /// The Tick event is blocking for the NetConnection!
        /// </summary>
        private bool inOnTickEvent = false;
        private void DoOnTickEvent(NS_OnTick onTick)
        {
            if (inOnTickEvent || onTick == null)
            {
                return;
            }

            inOnTickEvent = true;
            try
            {
                SynchronizationContext sc = netConnection.SynchronizationContext;
                if (sc != null)
                {
                    // Always use "Send" so we can protect against overrunning
                    sc.Send(new SendOrPostCallback(delegate(object state)
                    {
                        try
                        {
                            // This is on other thread
                            (state as NS_OnTick)(this);
                        }
                        catch (Exception e)
                        {
                            LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.NetStream.DoOnTickEvent] Catched unhandled user exception: {0}", e.ToString()));
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
        /// Called right after NetConnection receives the return result for "createStream" call.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal bool HandleOnAssignStream_ID(RTMPPacket packet, int stream_id, out int contentBufferTime)
        {
            this.stream_id = stream_id; // assign stream id
            this.contentBufferTime = NetConnection.DefaultContentBufferTime;

            NS_OnAssignStream_ID onAssignStream_ID = OnAssignStream_ID;
            if (onAssignStream_ID != null)
            {
                // -----------------------------------------------------------------------------------------
                SendOrPostCallback callback = new SendOrPostCallback(delegate(object state)
                {
                    // This is specified other thread
                    onAssignStream_ID(this, this.stream_id, ref this.contentBufferTime);
                });
                // -----------------------------------------------------------------------------------------
            
                SynchronizationContext sc = netConnection.SynchronizationContext;
                if (sc != null)
                {
                    // Use send because we need the changed "contentBufferTime" sto be able to continue
                    sc.Send(callback, null);
                }
                else
                {
                    callback(null);
                }
            }

            if (this.contentBufferTime <= 0)
            {
                this.contentBufferTime = NetConnection.DefaultContentBufferTime;
            }

            contentBufferTime = this.contentBufferTime;

            // NetConnection can handle it now
            return true;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal bool HandleOnStatus(RTMPPacket packet)
        {
            AMFObject obj = new AMFObject();
            int nRes = obj.Decode(packet.Body, 0, (int)packet.BodySize, false);
            if (nRes < 0)
            {
                return false;
            }

            // string method = obj.GetProperty(0).GetString(); // always onStatus
            int stream_id = packet.InfoField2;
            string code = obj.GetProperty(3).ObjectValue.GetProperty("code").StringValue;
            string level = obj.GetProperty(3).ObjectValue.GetProperty("level").StringValue;
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnStatus] code :{0}, level: {1}", code, level));

            return Internal_HandleOnStatusDecoded(packet, "onStatus", code, level, obj);
        }

        /// <summary>
        /// Also called directly from NetConnection OnMetaData (is has also and onStatus event, which fit's in here I think!)
        /// </summary>
        /// <param name="eventStr"></param>
        /// <param name="codeStr"></param>
        /// <param name="levelStr"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual protected bool Internal_HandleOnStatusDecoded(RTMPPacket packet, string eventStr, string codeStr, string levelStr, AMFObject obj)
        {
            //LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.NetStream.Internal_HandleOnStatusDecoded] event={0} code={1} level={2} Timestamp={3}", eventStr, codeStr, levelStr, packet.TimeStamp));
            //Console.WriteLine(string.Format("event={0} code={1} level={2} Timestamp={3} seekIsActive={4}", eventStr, codeStr, levelStr, packet.TimeStamp, seekIsActive.ToString()));

            if (OnStatus != null)
            {
                NetStreamStatusEvent netStreamStatusEvent = new NetStreamStatusEvent();
                netStreamStatusEvent.Clear();
                netStreamStatusEvent.Event = eventStr;
                netStreamStatusEvent.Code = codeStr;
                netStreamStatusEvent.Level = levelStr;
                netStreamStatusEvent.EventInfo = (AMFObject)obj.Clone(); // for thread safety

                MQ_RTMPMessage message = new MQ_RTMPMessage();
                message.MethodCall = MethodCall.OnEventCallUserCode;
                message.Params = new object[] { OnStatus, this, netStreamStatusEvent };
                netConnection.PostOnEventUserCallCodeMessage(message);
            }

            // Make sure we point to the right record which is buffered!
            if (codeStr == "NetStream.Play.Switch")
            {
                // end of stream (adjust duration to correct for inaccuracy!)
                lock (lockVAR)
                {
                    seekIsActive = false;
                } //lock
            }

            // Now do our thing
            switch (codeStr)
            {
                // We need to flush the existing buffers and wait until 
                // new data arrives
                case "NetStream.Seek.Notify":
                    lock (lockVAR)
                    {
                        seekIsActive = true; // is probably already set
                        blockMediaPackets++;
                        deltaTimeStampInMS = packet.TimeStamp;
                    }

                    Internal_OnSeekNotify(packet.TimeStamp);
                    break;

                case "NetStream.Play.Reset": // send when playlist starts at the beginning
                    lock (lockVAR)
                    {
                        atBeginOfAudio = true;
                        mediaBytesReceived = 0;
                        deltaTimeStampInMS = 0;
                    } //lock
                    break;

                case "NetStream.Play.Switch":
                    // Tell we have to stop playing (and drain the buffers!)
                    if (mediaBytesReceived > 0) // only when we are streaming already
                    {
                        blockMediaPackets++;
                    }
                    break;
                case "NetStream.Data.Start":
                    mediaBytesReceived = 0;
                    break;

                // stream begins to play (that is data is send)
                // deblock if needed
                case "NetStream.Play.Start":
                    lock (lockVAR)
                    {
                        if (blockMediaPackets > 0)
                        {
                            blockMediaPackets--;
                        }
                        seekIsActive = false; // is probably already set
                    } //lock
                    break;
                case "NetStream.Play.Stop":
                    ReplaySavedPackets(); // needed in case packets where saved (we're at the end so a byte sync will not occure anymore)
                    lock (lockVAR)
                    {
                        atBeginOfAudio = true;
                    }
                    break;

                // Pause logic
                case "NetStream.Pause.Notify":
                    ReplaySavedPackets(); // needed in case packets where saved (we're at the end so a byte sync will not occure anymore)
                    lock (lockVAR)
                    {
                        if (mediaBytesReceived > 0) // we're buffering
                        {
                            pauseIsActive = true;
                            Internal_OnPauseStream(true);
                            if (OnPauseStream != null)
                            {
                                MQ_RTMPMessage message = new MQ_RTMPMessage();
                                message.MethodCall = MethodCall.OnEventCallUserCode;
                                message.Params = new object[] { OnPauseStream, this, true };
                                netConnection.PostOnEventUserCallCodeMessage(message);
                            }
                        }
                    } //lock
                    break;
                case "NetStream.Unpause.Notify":
                    lock (lockVAR)
                    {
                        syncAfterPauseNeeded = true;
                        pauseIsActive = false;
                        Internal_OnPauseStream(false);
                        if (OnPauseStream != null)
                        {
                            MQ_RTMPMessage message = new MQ_RTMPMessage();
                            message.MethodCall = MethodCall.OnEventCallUserCode;
                            message.Params = new object[] { OnPauseStream, this, false };
                            netConnection.PostOnEventUserCallCodeMessage(message);
                        }
                    } //lock
                    break;

                case "NetStream.Play.Failed":
                case "NetStream.Play.StreamNotFound":
                case "NetStream.Failed":
                    break;
                case "NetStream.Play.Complete": // all data is send
                    lock (lockVAR)
                    {
                        seekIsActive = false; // safety
                    }
                    break;

            } //switch

            // Code can be eg:
            // "NetStream.Failed"
            // "NetStream.Play.Failed" 
            // "NetStream.Play.StreamNotFound" 
            // "NetConnection.Connect.InvalidApp"
            // "NetStream.Play.Start"
            // "NetStream.Publish.Start"
            // "NetStream.Play.Complete" //audio
            // "NetStream.Play.Stop"     // audio
            // "NetStream.Pause.Notify"
            // "NetStream.Seek.Notify"

            return true;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal bool HandleOnMetaData(RTMPPacket packet)
        {
            AMFObject obj = new AMFObject();
            int nRes = obj.Decode(packet.Body, 0, (int)packet.BodySize, false);
            if (nRes < 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetStream.HandleOnMetaData] Error decoding meta data packet");
                return false;
            }

            /* For video:
             *   canSeekToEnd = true
             *   videocodecid = 4
             *   framerate = 15
             *   videodatarate = 400
             *   height = 215
             *   width = 320
             *   duration = 7.347
             *   
             * For Audio (MP3): metastring =="onID3"
             * 
             */


            string metastring = obj.GetProperty(0).StringValue;
            switch (metastring)
            {
                case "onMetaData":
                    List<AMFObjectProperty> props = new List<AMFObjectProperty>();

                    props.Clear();
                    obj.FindMatchingProperty("audiodatarate", props, 1);
                    if (props.Count > 0)
                    {
                        int rate = (int)props[0].NumberValue;
                        audioDatarate += rate;
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnMetaData] audiodatarate: {0}", audioDatarate));
                    }
                    props.Clear();
                    obj.FindMatchingProperty("videodatarate", props, 1);
                    if (props.Count > 0)
                    {
                        int rate = (int)props[0].NumberValue;
                        videoDatarate += rate;
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnMetaData] videodatarate: {0}", videoDatarate));
                    }
                    if (audioDatarate == 0 && videoDatarate == 0)
                    {
                        props.Clear();
                        obj.FindMatchingProperty("filesize", props, int.MaxValue);
                        if (props.Count > 0)
                        {
                            combinedTracksLength = (int)props[0].NumberValue;
                            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnMetaData] Set CombinedTracksLength from filesize: {0}", combinedTracksLength));
                        }
                    }
                    if (combinedTracksLength == 0)
                    {
                        props.Clear();
                        obj.FindMatchingProperty("datasize", props, int.MaxValue);
                        if (props.Count > 0)
                        {
                            combinedTracksLength = (int)props[0].NumberValue;
                            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnMetaData] Set CombinedTracksLength from datasize: {0}", combinedTracksLength));
                        }
                    }
                    props.Clear();
                    obj.FindMatchingProperty("duration", props, 1);
                    if (props.Count > 0)
                    {
                        double duration = props[0].NumberValue;
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetStream.HandleOnMetaData] Set duration: {0}", duration));
                        lock (lockVAR)
                        {
                            metaDataDurationInMS = Convert.ToInt64(duration * 1000);
                        }

                        // Looks the same as the "onPlayStatus" (See NetConnection.HandleMetadata (we route it there through HandleOnStatusDecoded
                        // doing it here also)
                        Internal_HandleOnStatusDecoded(packet, "onStatus", "NetStream.Play.OnMetaData", "onPlayStatus", obj);
                    }
                    break;
                // Looks more as an invoke to me Let NetStream.OnStatus handle it
                case "onStatus": // -=> "NetStream.Data.Start"
                    Internal_HandleOnStatusDecoded(packet, "onStatus", obj.GetProperty(1).ObjectValue.GetProperty("code").StringValue, "", obj);
                    break;
                case "onPlayStatus":
                    // "code" = "NetStream.Play.Switch"
                    // "level"= "status" ,made it "onPlayStatus" 
                    // Has also "duration" and "bytes" as additional metadata (looks like a normal onMetaData to me with less options as the normal)
                    Internal_HandleOnStatusDecoded(packet, "onStatus", obj.GetProperty(1).ObjectValue.GetProperty("code").StringValue, "onPlayStatus", obj);
                    break;
                case "onID3":
                    return HandleOnID3(obj);
                default:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.NetStream.HandleOnMetaData] metastring= {0}", metastring));
                    break;
            } //switch

            return true;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal bool HandleOnID3(AMFObject obj)
        {
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection.NetStream.HandleOnID3]");
            obj.Dump();
            audioMetaData.Clear();
            audioMetaData.Valid = true;
            List<AMFObjectProperty> props = new List<AMFObjectProperty>();

            props.Clear();
            obj.FindMatchingProperty("v1SongTitle", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.SongTitle = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1LeadArtist", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.LeadArtist = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1AlbumTitle", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.AlbumTitle = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1YearReleased", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.YearReleased = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1SongComment", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.SongComment = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1SongGenre", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.SongGenre = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("v1TrackNumberOnAlbum", props, 1);
            if (props.Count > 0)
            {
                audioMetaData.TrackNumberOnAlbum = props[0].StringValue;
            }


            // handle event
            if (OnID3 != null)
            {
                MQ_RTMPMessage message = new MQ_RTMPMessage();
                message.MethodCall = MethodCall.OnEventCallUserCode;
                message.Params = new object[] { OnID3, this, (AudioMetaData)audioMetaData.Clone(), obj };
                netConnection.PostOnEventUserCallCodeMessage(message);
            }

            return true;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        virtual internal bool HandleOnMediaPacket(RTMPPacket packet)
        {
            if (blockMediaPackets > 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] blockingMediaPacket Timestamp : {0}, Size : {0}", TimeSpan.FromMilliseconds(packet.TimeStamp), packet.BodySize));
                return true;
            }

            mediaChannel = packet.Channel; //need for pause to have lastest timestamp stored in NetConnection."channelTimestamp" 

            LastMediaPacket = packet;
            TimeStamp = TimeSpan.FromMilliseconds(packet.TimeStamp);

            if (packet.PacketType == PacketType.Audio)
            {
                if (packet.BodySize <= 1)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Ignoring too small audio packet: size: {0}", packet.BodySize));
                    return true;
                }

                // Store audio data in audio buffer
                int offset = 1;
                if (syncAfterPauseNeeded)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Info, "[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Unpause event code started");

                    // Match last audio packet with this packet (must both be atleast 10 bytes big otherwhise match 
                    // will be bad
                    if (packet.BodySize > 10 && lastAudioPacket.Length > 10)
                    {
                        byte[] pattern = new byte[10];
                        Buffer.BlockCopy(packet.Body, 1, pattern, 0, 10);
                        LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Duplicate data, packetsize={0}", packet.BodySize - 1));

                        int[] hits = lastAudioPacket.Locate(pattern);
                        if (hits.Length > 0)
                        {
                            LibRTMPLogger.Log(LibRTMPLogLevel.Info, "[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Duplicate data detected");
                            // free data
                            savedPackets = null;
                            syncAfterPauseNeeded = false; // we're in sync

                            // we only look at the first hit, and try to match it up with as much 
                            // data as we have in "packet.body[1]" (this is start of pattern)
                            int j = hits[0];
                            // set offset so, it matches the entire packet!
                            offset = (int)packet.BodySize - 1;
                            for (int i = 1; i < packet.BodySize; i++)
                            {
                                if (j >= lastAudioPacket.Length || packet.Body[i] != lastAudioPacket[j])
                                {
                                    // New offset for packet data, where new data starts
                                    offset = i;
                                    // we're ready
                                    break;
                                }
                                j++;
                            } //for

                            // remove unused data from lastAudioPacket (or all)
                            if (offset >= (lastAudioPacket.Length - 1))
                            {
                                lastAudioPacket = null;
                            }
                            else
                            {
                                // there is some data left
                                int left = (lastAudioPacket.Length - 1) - offset;
                                byte[] leftB = new byte[left];
                                Buffer.BlockCopy(lastAudioPacket, j, leftB, 0, left);
                                // make sure we run this routine also for the next packet
                                lock (lockVAR)
                                {
                                    syncAfterPauseNeeded = true;
                                } //lock
                            }

                            LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Duplicate data detected up till offset:  {0}", offset));
                        }
                        else
                        {
                            LibRTMPLogger.Log(LibRTMPLogLevel.Info, "[CDR.LibRTMP.NetStream.HandleOnMediaPacket] NO Duplicate data detected");
                            if (savedPackets == null)
                            {
                                savedPackets = new List<RTMPPacket>();
                            }
                            savedPackets.Add(packet);

                            // After 10 packets received , just quit trying!
                            if (savedPackets.Count >= 10)
                            {
                                ReplaySavedPackets();
                            }
                            
                            return true;
                        }
                    }
                } // if got unpause event

                if (offset >= (int)packet.BodySize - 1)
                {
                    int newSize = (int)packet.BodySize - 1;
                    LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Duplicate data, skipped entire packet (size={0})", newSize));
                    // no data left to work with. so skip this packet
                    return true;
                }

                msAudioBuffer.Write(packet.Body, offset, (int)packet.BodySize - offset);
                mediaBytesReceived += packet.BodySize;

                // lastAudioPacket is needed to rematch data after a pause (streaming server 
                // seems to send the same packet and manipulation of position doesn't seem the fix it)
                // small optimalization (most of the time packets are of the same size!
                if (lastAudioPacket == null || lastAudioPacket.Length != (packet.BodySize - 1))
                {
                    lastAudioPacket = new byte[packet.BodySize - 1];
                }
                Buffer.BlockCopy(packet.Body, 1, lastAudioPacket, 0, (int)(packet.BodySize - 1));

                if ((atBeginOfAudio && msAudioBuffer.Position >= 8192) || (!atBeginOfAudio && msAudioBuffer.Position > 0))
                {
                    atBeginOfAudio = false;
                    byte[] tmpBuffer = new byte[msAudioBuffer.Position];
                    Buffer.BlockCopy(msAudioBuffer.GetBuffer(), 0, tmpBuffer, 0, Convert.ToInt32(msAudioBuffer.Position));
                    // Reset position to start from beginning
                    msAudioBuffer.Position = 0;

                    Internal_OnAudioPacket(TimeStamp, tmpBuffer);
                    if (OnAudioPacket != null)
                    {
                        MQ_RTMPMessage message = new MQ_RTMPMessage();
                        message.MethodCall = MethodCall.OnEventCallUserCode;
                        message.Params = new object[] { OnAudioPacket, this, TimeStamp, tmpBuffer.Clone() };
                        netConnection.PostOnEventUserCallCodeMessage(message);
                    }
                }

                return true;
            }
            else if (OnVideoPacket != null && packet.PacketType == PacketType.Video)
            {
                // skip video info/command packets
                if (packet.BodySize == 2 && ((packet.Body[0] & 0xf0) == 0x50))
                {
                    return true;
                }
                else if (packet.BodySize <= 5)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetStream.HandleOnMediaPacket] Ignoring too small video packet: size: {0}", packet.BodySize));
                    return true;
                }
                else
                {
                    // TODO
                    // Probaly when using pause/unpause the first packet contains data which we already got
                    // should check for this and repair as done in audio part

                    // Store video data in buffer
                    msVideoBuffer.Write(packet.Body, 1, (int)packet.BodySize - 1);

                    if (OnVideoPacket != null)
                    {
                        MQ_RTMPMessage message = new MQ_RTMPMessage();
                        message.MethodCall = MethodCall.OnEventCallUserCode;
                        message.Params = new object[] { OnVideoPacket, this, TimeStamp, msVideoBuffer.ToArray().Clone() };
                        netConnection.PostOnEventUserCallCodeMessage(message);
                    }

                    // Clear Buffer again
                    msVideoBuffer.Seek(0, SeekOrigin.Begin);
                    msVideoBuffer.SetLength(0);

                    return true;
                }
            }

            return false;
        }

        private void ReplaySavedPackets()
        {
            // After 10 packets received , just quit trying!
            if (savedPackets != null && savedPackets.Count > 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, "[CDR.LibRTMP.NetStream.ReplaySavedPackets] Replay code running to save what we got!!!");

                // Sync can not be found (or just reset it), replay previous packets
                syncAfterPauseNeeded = false;
                foreach (RTMPPacket packet in savedPackets)
                {
                    HandleOnMediaPacket(packet);
                } //foreach

            }
            // free data
            savedPackets = null;
        }

        #endregion

        #region Internal events for derived classes

        virtual protected void Internal_OnAudioPacket(TimeSpan timeStamp, byte[] data)
        {
        }
        virtual protected void Internal_OnSeekNotify(uint timeStampInMS)
        {
        }
        virtual protected void Internal_OnPauseStream(bool doPause)
        {
        }

        #endregion

        #region (public) Properties

        public bool LiveStream
        {
            get
            {
                return liveStream;
            }
            internal set
            {
                liveStream = value;
            }
        }

        /// <summary>
        /// Maximum duration of this mediaFile
        /// Duration of stream in seconds returned by Metadata        
        /// </summary>
        public double Duration
        {
            get
            {
                return (metaDataDurationInMS / 1000.0);
            }
        }

        /// <summary>
        /// Maximum duration of this mediaFile
        /// Duration of stream in milliseconds returned by Metadata        
        /// </summary>
        public long DurationInMS
        {
            get
            {
                return metaDataDurationInMS;
            }
        }

        public long AudioDatarate
        {
            get
            {
                return audioDatarate;
            }
        }

        public long VideoDatarate
        {
            get
            {
                return videoDatarate;
            }
        }

        public long CombinedTracksLength
        {
            get
            {
                return combinedTracksLength;
            }
        }

        public bool PauseIsActive
        {
            get
            {
                return pauseIsActive;
            }
        }

        /// <summary>
        /// When a seek command is give it takes a bit of time before the command is send and result
        /// gets back. In this timeperiod without this function it's not possible to know a seek
        /// is running for the stream
        /// </summary>
        public bool SeekIsActive
        {
            get
            {
                return seekIsActive;
            }
        }

        public uint DeltaTimeStampInMS
        {
            get
            {
                lock (lockVAR)
                {
                    return deltaTimeStampInMS;
                }
            }
        }

        /// <summary>
        /// Number of Mediabytes received
        /// </summary>
        public long MediaBytesReceived
        {
            get
            {
                lock (lockVAR)
                {
                    return mediaBytesReceived;
                }
            }
        }

        /// <summary>
        /// This var is not used in this class, for user only
        /// </summary>
        public bool AutoPlay
        {
            get
            {
                return autoPlay;
            }
            set
            {
                autoPlay = value;
            }
        }

        /// <summary>
        /// This var is not used in this class, for user only
        /// </summary>
        public object Tag
        {
            get
            {
                return tag;
            }
            set
            {
                tag = value;
            }
        }

        #endregion

    }
}
