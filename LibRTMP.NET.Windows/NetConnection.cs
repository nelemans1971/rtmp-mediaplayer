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
#define INCLUDE_TMPE
//#define SSL
#define RANDOM // Use random data in handshake

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
#if SSL
using System.Net.Security;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CDR.LibRTMP
{
    /// <summary>
    /// Every NetConnection class has it's own background thread.
    /// All commands are route from/to the NetConnection Message pump
    /// User code will be called on the UI thread when possible (if SynchronizationContext
    /// is filled)
    /// </summary>
    public class NetConnection : IDisposable
    {
        private object lockVAR = new object(); // used to synchronize internal var access between different threads

        private Thread thread = null;
        private bool threadStarted = false;
        private bool closeCalled = false;
        // Info:
        //   http://www.codeproject.com/Articles/31971/Understanding-SynchronizationContext-Part-I
        //   http://www.codeproject.com/Articles/32113/Understanding-SynchronizationContext-Part-II
        //   http://www.codeproject.com/Articles/32119/Understanding-SynchronizationContext-Part-III
        //   http://blogs.msdn.com/b/csharpfaq/archive/2010/06/18/parallel-programming-task-schedulers-and-synchronization-context.aspx
        private SynchronizationContext synchronizationContext = null;
        private SynchronizationContextMethod synchronizationContextMethod = SynchronizationContextMethod.Post;
        // This is for the message queue pump which does the acutual work in a sperate thread
        // to not block the mainthread when we're waiting for netwerk data
        private static EventWaitHandle messageQueueWaitEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        private List<MQ_RTMPMessage> messageQueue;


        private NetConnectionState netConnectionState = NetConnectionState.Disconnected;
        private DateTime dtNetConnectionKeepAlive = DateTime.MaxValue;
        private int timeOutKeepAliveInSec = 10; // how long between no data over connection do we check for connection
        private ServerLink serverLink = null;
        private Socket tcpSocket = null;
        private bool DisconnectEventSend = false; // used to limit sending only one disconnect event after we had a succesfull connect
#if SSL
        private SslStream sslStream = null;
#endif

        // intial timeout until stream is connected (don't set too low, as the server's answer after the handshake might take some time)
        private int onConnectWaitTimeoutMS = 7000;
        private int receiveTimeoutMS = RTMPConst.TIMEOUT_RECEIVE;
        private int bytesReadTotal = 0;
        private int lastSentBytesRead = 0;
        private int numInvokes = 0;

        public const int DefaultContentBufferTime = 15 * 1000; // default buffer time at assign of stream_id

        public int InChunkSize = RTMPConst.RTMP_DEFAULT_CHUNKSIZE; // Current ChunkSize for incoming packets (default: 128 byte)
        private int outChunkSize = RTMPConst.RTMP_DEFAULT_CHUNKSIZE; // is dit per NetConnection?

        private Version rtmpServerVersion = new Version(0, 0, 0, 0);
        private Version realRTMPServerVersion = new Version(0, 0, 0, 0);
        private uint rtmpServerUptime = 0;
        private NetConnectionConnectInfo netConnectionConnectInfo;

        private string secureTokenPassword = ""; // gebruiken we voor wowza

        private int bwCheckCounter;
        private int serverBW;
        private int clientBW;
        private byte clientBW2;

        private Dictionary<int, object> transactionIDReferenceTable = new Dictionary<int, object>(); // Key value pair for looking up the result of a transaction
        private NetStream[] netStreams = new NetStream[RTMPConst.RTMP_CHANNELS]; // the open streams stored by stream_id
        private List<NetStream> netStreamsList = new List<NetStream>(); // Used to fast loop through all available netstreams connected to this NetConnection
        private RTMPPacket[] vecChannelsIn = new RTMPPacket[RTMPConst.RTMP_CHANNELS];
        private RTMPPacket[] vecChannelsOut = new RTMPPacket[RTMPConst.RTMP_CHANNELS];
        private uint[] channelTimestamp = new uint[RTMPConst.RTMP_CHANNELS]; // abs timestamp of last packet
        private Dictionary<int, string> methodCallDictionary = new Dictionary<int, string>(); //remote method calls queue

        private Dictionary<string, NC_MethodCallBack> dMethodLookup = new Dictionary<string, NC_MethodCallBack>();

        /// <summary>
        /// Not used in this libary
        /// How to build the swf Hash:
        /// if swf is compressed, decompress with http://flasm.sourceforge.net/
        /// $ openssl sha -sha256 -hmac "Genuine Adobe Flash Player001" file.swf
        /// </summary>
        private string swfURL = string.Empty;
        private string swfPageURL = string.Empty;
        private int swfSize = 0;
        private string swfAuth = string.Empty;
        private byte[] swfHash = null;
        private string swfFlashVer = string.Empty;
        private byte[] swfVerificationResponse = new byte[42];

#if INCLUDE_TMPE
#endif

        // NetConnection Events
        // --------------------
        /// <summary>
        /// Will be trigger after an (un)successful connection
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NC_OnConnect OnConnect = null;
        /// <summary>
        /// Will be trigger after a disconnect is detected
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NC_OnDisconnect OnDisconnect = null;
        /// <summary>
        /// Gives a call every 500ms (not accurate!!) on LibRTMP thread
        /// Using SynchronizationContext called on this thread if set
        /// </summary>
        public event NC_OnTick OnTick = null;


        #region Mainthread Entry point API
        // -----------------------------------------------------------------------------------------------------------------
        // Mainthread entry point API for main (user) thread
        // -----------------------------------------------------------------------------------------------------------------

        /*
                public event NC_OnError OnError;
        */

        /// <summary>
        /// Constructor
        /// </summary>
        public NetConnection()
        {
            // in a winform, wpf, MonoTouch or Mono Android enviroment this will be set
            this.synchronizationContext = SynchronizationContext.Current;
            InitNetConnection();
        }

        public NetConnection(SynchronizationContext synchronizationContext)
        {
            this.synchronizationContext = synchronizationContext;
            InitNetConnection();
        }

        private void InitNetConnection()
        {
            messageQueue = new List<MQ_RTMPMessage>();
            netConnectionConnectInfo = new NetConnectionConnectInfo();
            netConnectionConnectInfo.Clear();

            CreateThread();
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
        ~NetConnection()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        /// <summary>
        /// After this, this class is not useable again. You need to create a new one!
        /// </summary>
        public void Close()
        {
            try
            {
                // prevent continues recurve callback
                if (closeCalled)
                {
                    return;
                }
                closeCalled = true;

                for (int i = netStreams.GetLowerBound(0); i < netStreams.GetUpperBound(0); i++)
                {
                    if (netStreams[i] != null)
                    {
                        netStreams[i].Close();
                        netStreams[i] = null;
                    }
                } //for
                netStreamsList.Clear();
                // Fire event

                if (OnDisconnect != null)
                {
                    // We have to explcitly start this event, because after this
                    // the thread will be killed!
                    MQ_RTMPMessage message = new MQ_RTMPMessage();
                    message.MethodCall = MethodCall.OnEventCallUserCode;
                    message.Params = new object[] { OnDisconnect, this };

                    SynchronizationContext sc;
                    lock (lockVAR)
                    {
                        sc = synchronizationContext;
                    } //lock
                    if (sc != null)
                    {
                        sc.Send(HandleOnEventCallUserCode, message);
                    }
                    else
                    {
                        HandleOnEventCallUserCode(message);
                    }
                }
            }
            catch (Exception e)
            {
                LibRTMPLogger.LogError(e);
            }

            KillThread();
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
            thread.Name = "CDR.LibRTMP.NetConnection";
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

        /// <summary>
        /// Make connection with a RTMP server like Wowza, red5 or FMS
        /// </summary>
        /// <returns></returns>
        public void Connect(ServerLink serverLink, NC_ResultCallBackConnect resultCallBackConnect, params AMFObjectProperty[] amfProperties)
        {
            // First check if there isn't a build connection in the message queue
            lock (lockVAR)
            {
                if (messageQueue != null)
                {
                    foreach (MQ_RTMPMessage msg in messageQueue)
                    {
                        if (msg.MethodCall == MethodCall.ConnectRTMPServer)
                        {
                            // cancel, we're already trying to do it!
                            return;
                        }
                    } //foreach
                }
            } //lock

            netConnectionConnectInfo.Clear();

            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.ConnectRTMPServer;
            message.Params = new object[] { serverLink, resultCallBackConnect, amfProperties };

            AddMessageToPump(message);
        }

        public void Connect(ServerLink serverLink, NC_ResultCallBackConnect resultCallBackConnect)
        {
            Connect(serverLink, resultCallBackConnect, null);
        }

        /// <summary>
        /// Connect to RTMPServer, nocallback (you use the OnConnect event)
        /// </summary>
        public void Connect(ServerLink serverLink)
        {
            Connect(serverLink, null, null);
        }

        /// <summary>
        /// Make connection with a RTMP server like Wowza, red5 or FMS
        /// </summary>
        /// <returns></returns>
        public void Connect(string connectUrl, NC_ResultCallBackConnect resultCallBackConnect)
        {
            ServerLink serverLink = new ServerLink(connectUrl);
            Connect(serverLink, resultCallBackConnect);
        }

        /// <summary>
        /// Connect to RTMPServer, nocallback (you use the OnConnect event)
        /// </summary>
        public void Connect(string connectUrl)
        {
            ServerLink serverLink = new ServerLink(connectUrl);
            Connect(serverLink, null);
        }

        public void CloseConnection()
        {
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.CloseConnectionRTMPServer;
            message.Params = null;

            AddMessageToPump(message);
        }

        /// <summary>
        /// Create a logical channel for message communication for use
        /// in publishing of audio or video and metadata carrying
        /// </summary>
        /// <returns></returns>
        public void CreateStream(NetStream ns)
        {
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.CreateStream;
            message.Params = new object[] { ns }; ;

            AddMessageToPump(message);
        }

        /// <summary>
        /// Deletes a logical channel. Stream_id is give so we don't
        /// have to mess with thread issues
        /// </summary>
        /// <returns></returns>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void DeleteStream(int stream_id)
        {
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.DeleteStream;
            message.Params = new object[] { stream_id }; ;

            AddMessageToPump(message);
        }

        /// <summary>
        /// Runs remote procedure calls (RPC) at the receiving end.
        /// </summary>
        /// <returns></returns>
        public bool Call()
        {
            return false;
        }

        /// <summary>
        /// Register a callback method. Method will be called on UI thread when SynchronizationContext
        /// is filled.
        /// </summary>
        public void RegisterCallFunction(string methodName, NC_MethodCallBack method)
        {
            lock (lockVAR)
            {
                dMethodLookup.Add(methodName, method);
            } //lock
        }

        /// <summary>
        /// Is this NetConnection connected to a RTMP server
        /// (Does it have a valid connection!), this is different from
        /// "MQInternal_IsConnected" which only checks is we are connected
        /// (when in the handshake state)
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return MQInternal_IsConnected && netConnectionState == NetConnectionState.Connected;
            }
        }

        /// <summary>
        /// Are we trying to connect to a RTMP server?
        /// </summary>
        public bool Connecting
        {
            get
            {
                lock (lockVAR)
                {
                    if (netConnectionState != NetConnectionState.Disconnected)
                    {
                        return true;
                    }

                    if (tcpSocket != null && !tcpSocket.Connected)
                    {
                        return true;
                    }

                    if (messageQueue != null)
                    {
                        foreach (MQ_RTMPMessage msg in messageQueue)
                        {
                            if (msg.MethodCall == MethodCall.ConnectRTMPServer)
                            {
                                return true;
                            }
                        } //foreach
                    }
                } //lock

                return false;
            }
        }

        /// <summary>
        /// When not real Flash Media Server it is probaly facked.
        /// (tested with wowza 3.5.2 it reports version 3.0.1.1)
        /// </summary>
        public Version RTMPServerVersion
        {
            get
            {
                return rtmpServerVersion;
            }
        }

        /// <summary>
        /// Number of tick or something like that?
        /// </summary>
        public uint RTMPServerUptime
        {
            get
            {
                return rtmpServerUptime;
            }
        }

        /// <summary>
        /// When set events will be executed on this threads context.
        /// eg use
        ///   SynchronizationContext.Current;
        /// This will be set when an form is created.
        /// Now events will be fired on the gui thread
        /// </summary>
        public SynchronizationContext SynchronizationContext
        {
            get
            {
                lock (lockVAR)
                {
                    return synchronizationContext;
                }
            }
            set
            {
                lock (lockVAR)
                {
                    synchronizationContext = value;
                }
            }
        }
        // -----------------------------------------------------------------------------------------------------------------


        // -----------------------------------------------------------------------------------------------------------------
        // private calls for NetStream (on main (user) thread)
        // -----------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Add new command for NetConnection thread. Inserted at the end
        /// of the queue.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void PostMessage(MQ_RTMPMessage message, bool waitForEmptyQueue = false, int timeOutinMS = -1)
        {
            if (waitForEmptyQueue)
            {
                // Wait before posting that the messagequeue is empty
                DateTime timeStamp = DateTime.Now;
                while (true)
                {
                    lock (lockVAR)
                    {
                        if (messageQueue == null || messageQueue.Count == 0)
                        {
                            break;
                        }
                    }//lock
                    Thread.Sleep(10);
                    if (timeOutinMS >= 0 && (DateTime.Now - timeStamp).TotalMilliseconds >= timeOutinMS)
                    {
                        // we exiten because of timeout
                        break;
                    }
                }//
            }

            if (message != null)
            {
                AddMessageToPump(message);
            }
        }

        /// <summary>
        /// Insert a delegate callback. Will be insert at the beginning of
        /// the queue, after alle the other delegate callbaks (if any).
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void PostOnEventUserCallCodeMessage(MQ_RTMPMessage message)
        {
            AddOnEventUserCallCodeToPump(message);
        }

        /// <summary>
        /// Send a ping command
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void SendPing(short nType, uint nObject, uint nTime)
        {
            MQ_RTMPMessage message = new MQ_RTMPMessage();
            message.MethodCall = MethodCall.SendPing;
            message.Params = new object[] { nType, nObject, nTime };

            AddMessageToPump(message);
        }

        /// <summary>
        /// Safe get the message count on the queue.
        /// </summary>
        private int MessageQueueCount
        {
            get
            {
                lock (lockVAR)
                {
                    return messageQueue.Count;
                }//lock
            }
        }
        // -----------------------------------------------------------------------------------------------------------------

        #endregion


        #region Message pump queue code
        /// <summary>
        /// This is the main LibRTMP func where the thread spents most of it's time
        /// </summary>
        private void StartMessageQueueProcessor()
        {
            threadStarted = true;

            // long running thing that does something with this message.
            lock (lockVAR)
            {
                if (messageQueue == null)
                {
                    return;
                }
            }

            DateTime dtTimer = DateTime.Now; // used to trigger every 500 milliseconds an event
            while (threadStarted)
            {
                try
                {
                    MQ_RTMPMessage message = null;

                    lock (lockVAR)
                    {
                        if (messageQueue.Count > 0)
                        {
                            // get oldest first
                            message = messageQueue[0];
                            messageQueue.RemoveAt(0);
                        }
                    }//lock

                    if (message != null)
                    {
                        switch (message.MethodCall)
                        {
                            case MethodCall.ConnectRTMPServer:
                                MQ_Connect((ServerLink)message.Params[0], (NC_ResultCallBackConnect)message.Params[1], (AMFObjectProperty[])message.Params[2]);
                                break;
                            case MethodCall.RemoteDisconnect: // RTMP server disappeared notify all object of this event
                                MQ_Close();
                                break;
                            case MethodCall.CloseConnectionRTMPServer: // request disconnect from RTMP server
                                MQ_Close();
                                break;
                            case MethodCall.Call:
                                MQ_SendCall((NetStream)message.Params[0], (string)message.Params[1], (object[])message.Params[2]);
                                break;

                            case MethodCall.SendPing:
                                MQ_SendPing((short)message.Params[0], (uint)message.Params[1], (uint)message.Params[2]);
                                break;
                            case MethodCall.CreateStream:
                                MQ_SendCreateStream((NetStream)message.Params[0]);
                                break;
                            case MethodCall.DeleteStream:
                                MQ_SendDeleteStream((int)message.Params[0]);
                                break;
                            case MethodCall.Play:
                                MQInternal_SendPlay((NetStream)message.Params[0], (string)message.Params[1], (int)message.Params[2], (int)message.Params[3], (bool)message.Params[4], (AMFObjectProperty)message.Params[5]);
                                break;
                            case MethodCall.CloseStream:
                                MQ_SendCloseStream((NetStream)message.Params[0]);
                                break;

                            case MethodCall.Pause:
                                MQInternal_SendPause((NetStream)message.Params[0], (bool)message.Params[1]);
                                break;
                            case MethodCall.Seek:
                                MQInternal_SendSeek((NetStream)message.Params[0], (long)message.Params[1]);
                                break;

                            case MethodCall.MQInternal_SetContentBufferTime:
                                MQInternal_SetContentBufferTime((NetStream)message.Params[0], (int)message.Params[1]);
                                break;

                            case MethodCall.OnEventCallUserCode:
                                SynchronizationContext sc;
                                lock (lockVAR)
                                {
                                    sc = synchronizationContext;
                                } //lock
                                if (sc != null)
                                {
                                    switch (synchronizationContextMethod)
                                    {
                                        case SynchronizationContextMethod.Post:
                                            sc.Post(HandleOnEventCallUserCode, message);
                                            break;
                                        case SynchronizationContextMethod.Send:
                                            sc.Send(HandleOnEventCallUserCode, message);
                                            break;
                                    } //switch
                                }
                                else
                                {
                                    HandleOnEventCallUserCode(message);
                                }
                                break;
                        } //switch
                    }

                    // We must process MethodCall.OnEventCallUserCode as soon as possible to preserve calls on timeline
                    lock (lockVAR)
                    {
                        if (messageQueue.Count > 0)
                        {
                            if (messageQueue[0].MethodCall == MethodCall.OnEventCallUserCode)
                            {
                                // get next message
                                continue;
                            }
                        }
                    }//lock

                    // Nu packet proberen te ontvangen en verwerken.
                    if (netConnectionState == NetConnectionState.Connected)
                    {
                        if (MQInternal_IsConnected)
                        {
                            if (MQInternal_DataInSocket)
                            {
                                RTMPPacket packet = null;
                                ReadPacket(out packet);
                                HandleClientPacket(packet);
                            }
                        }
                        else
                        {
                            // tell there is no connection anymore
                            RemoteServerDisconnected();
                        }
                    }

                    // We must process MethodCall.OnEventCallUserCode as soon as possible to preserve calls on timeline
                    // We need this cod ehere again because of "HandleClientPacket" call
                    lock (lockVAR)
                    {
                        if (messageQueue.Count > 0)
                        {
                            if (messageQueue[0].MethodCall == MethodCall.OnEventCallUserCode)
                            {
                                // get next message
                                continue;
                            }
                        }
                    }//lock

                    // Every 100ms we generate an event derived class have indepent time to do something
                    // with the received data (they must be quick and not wait)
                    // Now also let all connected NetStreams give a tick
                    foreach (NetStream netStream in netStreamsList)
                    {
                        netStream.HandleOnTimeSlice();
                    } //foreach

                    // Check if we need to give a timer tick
                    if ((DateTime.Now - dtTimer).TotalMilliseconds >= 500)
                    {
                        dtTimer = DateTime.Now; // setup for next timer tick
                        if (OnTick != null)
                        {
                            // is implemented in place here, because of calling sequence and speed
                            DoOnTickEvent(OnTick);
                        }
                        // Now also let all connected NetStreams give a tick
                        foreach (NetStream netStream in netStreamsList)
                        {
                            netStream.HandleOnTick();
                        } //foreach
                    }

                    // Check if we need to check for keep-alive
                    TimeSpan tsKeepAlive = (DateTime.Now - dtNetConnectionKeepAlive);
                    if (tsKeepAlive.TotalDays <= 1 && tsKeepAlive.TotalSeconds >= timeOutKeepAliveInSec)
                    {
                        // Send ping to check for connection alive!
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.StartMessageQueueProcessor] Sending Ping for Keepalive test"));

                        MQ_SendPing(6, (uint)System.Environment.TickCount, (uint)System.Environment.TickCount);
                        dtNetConnectionKeepAlive = DateTime.Now;
                    }

                    if (threadStarted && MessageQueueCount == 0 && !MQInternal_DataInSocket)
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
            messageQueue = null;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "exit");
            thread = null;
        }

        /// <summary>
        /// Add new message to queue. Thread safe
        /// </summary>
        private void AddMessageToPump(MQ_RTMPMessage message)
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
        /// Needed especially for OnEventUserCallCode. We want the user code to get
        /// the event as soon as possible. Because of lock we need to call it from
        /// the messagepump (then the usercode can call the NetLibRTMP code
        /// wihtout locking problems
        /// </summary>
        private void AddOnEventUserCallCodeToPump(MQ_RTMPMessage message)
        {
            if (message.MethodCall != MethodCall.OnEventCallUserCode)
            {
                return;
            }

            lock (lockVAR)
            {
                if (messageQueue == null)
                {
                    return;
                }

                // Add as "last" OnEventUserCallCode but before rother events
                for (int i = 0; i < messageQueue.Count; i++)
                {
                    if (messageQueue[i].MethodCall != MethodCall.OnEventCallUserCode)
                    {
                        // added it before [i] and return. We're ready
                        messageQueue.Insert(i, message);
                        return;
                    }
                } //for

                // It's probably the first message which is inserted
                messageQueue.Add(message);
            } //lock
        }

        #endregion

        #region Message Pump function calls

        /// <summary>
        /// Fire OnTick event on the right thread
        /// The Tick event is blocking for the NetConnection!
        /// </summary>
        private bool inOnTickEvent = false;
        private void DoOnTickEvent(NC_OnTick onTick)
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
                    try
                    {
                        // Always use "Send" so we can protect against overrunning
                        sc.Send(new SendOrPostCallback(delegate(object state)
                        {
                            // This is on other thread
                            (state as NC_OnTick)(this);
                        }), onTick);
                    }
                    catch (Exception e)
                    {
                        LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.NetConnection.DoOnTickEvent] Catched unhandled user exception: {0}", e.ToString()));
                    }
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
        /// When SynchronizationContext is filled, we're on that thread otherwise
        /// we're on the NetConnection thread.
        /// </summary>
        /// <param name="message"></param>
        private void HandleOnEventCallUserCode(object state)
        {
            try
            {
                if (!(state is MQ_RTMPMessage))
                {
                    return;
                }
                MQ_RTMPMessage message = state as MQ_RTMPMessage;

                if (message.Params[0] is NC_OnConnect)
                {
                    (message.Params[0] as NC_OnConnect)(message.Params[1]);
                }
                else if (message.Params[0] is NC_OnDisconnect)
                {
                    (message.Params[0] as NC_OnDisconnect)(message.Params[1]);
                }
                else if (message.Params[0] is NC_OnTick)
                {
                    (message.Params[0] as NC_OnTick)(message.Params[1]);
                }
                else if (message.Params[0] is NS_OnStatus)
                {
                    (message.Params[0] as NS_OnStatus)(message.Params[1], (NetStreamStatusEvent)message.Params[2]);
                }
                else if (message.Params[0] is NS_OnID3)
                {
                    (message.Params[0] as NS_OnID3)(message.Params[1], (AudioMetaData)message.Params[2], (AMFObject)message.Params[3]);
                }
                else if (message.Params[0] is NC_OnMediaPacket)
                {
                    (message.Params[0] as NC_OnMediaPacket)(message.Params[1], (TimeSpan)message.Params[2], (byte[])message.Params[3]);
                }
                else if (message.Params[0] is NS_OnTick)
                {
                    (message.Params[0] as NS_OnTick)(message.Params[1]);
                }
                else if (message.Params[0] is NS_OnStreamStart)
                {
                    (message.Params[0] as NS_OnStreamStart)((string)message.Params[1]);
                }
                else if (message.Params[0] is NS_OnStreamStop)
                {
                    (message.Params[0] as NS_OnStreamStop)((string)message.Params[1]);
                }
                else if (message.Params[0] is NS_OnSwitchStream)
                {
                    (message.Params[0] as NS_OnSwitchStream)(message.Params[1]);
                }
                else if (message.Params[0] is NS_OnPauseStream)
                {
                    (message.Params[0] as NS_OnPauseStream)(message.Params[1], (bool)message.Params[2]);
                }
            }
            catch (Exception e)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Error, string.Format("[CDR.LibRTMP.NetConnection.HandleOnEventCallUserCode] Catched unhandled user exception: {0}", e.ToString()));
            }
        }

        private void MQ_Connect(ServerLink sl, NC_ResultCallBackConnect onResult, params AMFObjectProperty[] amfProperties)
        {
            MQ_Close();

            netConnectionState = NetConnectionState.Connecting;
            serverLink = sl;
            bool success = MQInternal_MakeConnection(amfProperties);
            if (success)
            {
                netConnectionState = NetConnectionState.Connected;
            }

            if (success)
            {
                success = false;

                // Now we have to wait for the "onConnect" event to be send by the wowza server, before we can say the
                // connection was successful. We do this for a max of 7 seconds, if no "onConnect" then
                // disconnect and say failed
                DateTime timeStamp = DateTime.Now.AddMilliseconds(onConnectWaitTimeoutMS);
                while (!success && (timeStamp - DateTime.Now).TotalMilliseconds > 0)
                {
                    if (netConnectionState == NetConnectionState.Connected)
                    {
                        if (MQInternal_IsConnected)
                        {
                            if (MQInternal_DataInSocket)
                            {
                                RTMPPacket packet = null;
                                ReadPacket(out packet);
                                // When we get an Invoke while connecting this is the "connect" event!
                                // where we have been waiting for.
                                if (packet.PacketType == PacketType.Invoke)
                                {
                                    success = true;
                                }

                                HandleClientPacket(packet);
                            }
                        }
                        else
                        {
                            // There is no connection anymore
                            break;
                        }
                    }
                    // don't use cpu 100%
                    if (!success)
                    {
                        Thread.Sleep(10);
                    }
                } //while

                // Close connection when failed!
                if (!success || (DateTime.Now - timeStamp).TotalMilliseconds > 0)
                {
#if SSL
                    if (sslStream != null)
                    {
                        try
                        {
                            sslStream.Close();
                            sslStream.Dispose();
                        }
                        catch { }
                    }
                    sslStream = null;
#endif
                    if (tcpSocket != null)
                    {
                        try
                        {
                            tcpSocket.Shutdown(SocketShutdown.Both);
                            tcpSocket.Close();
                        }
                        catch { }
                    }
                    tcpSocket = null;

                    // temporary before we set the state to disconnected!
                    netConnectionState = NetConnectionState.Connecting;
                    dtNetConnectionKeepAlive = DateTime.Now;
                }
            }

            if (onResult != null)
            {
                // this is delegate given when connecting (used by Mediaplayer class)
                // makes event OnConnect not needed
                DoNC_ResultCallBackConnectEvent(onResult, success);
            }

            // Now we do a "global" onConnect
            if (success && OnConnect != null)
            {
                MQ_RTMPMessage message = new MQ_RTMPMessage();
                message.MethodCall = MethodCall.OnEventCallUserCode;
                message.Params = new object[] { OnConnect, this };
                AddOnEventUserCallCodeToPump(message);
            }

            if (!success)
            {
                // Set state
                netConnectionState = NetConnectionState.Disconnected;
            }
        }

        /// <summary>
        /// Fires an NC_ResultCallBackConnect on the correct thread
        /// if SynchronizationContext is filled.
        /// </summary>
        private void DoNC_ResultCallBackConnectEvent(NC_ResultCallBackConnect onResult, bool success)
        {
            // -----------------------------------------------------------------------------------------
            SendOrPostCallback callback = new SendOrPostCallback(delegate(object state)
            {
                // This is specified other thread
                (state as State_NC_ResultCallBackConnect).OnResult((state as State_NC_ResultCallBackConnect).thisObject,
                    (state as State_NC_ResultCallBackConnect).Success);
            });
            // -----------------------------------------------------------------------------------------

            State_NC_ResultCallBackConnect callbackState = new State_NC_ResultCallBackConnect();
            callbackState.thisObject = this;
            callbackState.OnResult = onResult;
            callbackState.Success = success;

            SynchronizationContext sc;
            lock (lockVAR)
            {
                sc = synchronizationContext;
            } //lock
            if (sc != null)
            {
                switch (synchronizationContextMethod)
                {
                    case SynchronizationContextMethod.Post:
                        sc.Post(callback, callbackState);
                        break;
                    case SynchronizationContextMethod.Send:
                        sc.Send(callback, callbackState);
                        break;
                } //switch
            }
            else
            {
                callback(callbackState);
            }
        }

        private void MQ_Close()
        {
            lock (lockVAR)
            {
                // delete all streams if there were any
                bool wasConnected = (tcpSocket != null && netConnectionState == NetConnectionState.Connected);
                if (MQInternal_IsConnected)
                {
                    foreach (NetStream netStream in netStreamsList)
                    {
                        if (netStream.Stream_ID >= 0)
                        {
                            MQ_SendDeleteStream(netStream.Stream_ID);
                            // this will generate result packets but we're cclosing the
                            // connect. Really we should wait to get all result before disconnecting
                        }
                    } //foreach
                }

#if SSL
                if (sslStream != null)
                {
                    try
                    {
                        sslStream.Close();
                        sslStream.Dispose();
                    }
                    catch { }
                }
                sslStream = null;
#endif
                if (tcpSocket != null)
                {
                    try
                    {
                        tcpSocket.Shutdown(SocketShutdown.Both);
                        tcpSocket.Close();
                    }
                    catch { }
                }
                tcpSocket = null;
                netConnectionState = NetConnectionState.Disconnected;
                dtNetConnectionKeepAlive = DateTime.MaxValue;

                receiveTimeoutMS = RTMPConst.TIMEOUT_RECEIVE;

                InChunkSize = RTMPConst.RTMP_DEFAULT_CHUNKSIZE;
                outChunkSize = RTMPConst.RTMP_DEFAULT_CHUNKSIZE;

                bwCheckCounter = 0;
                clientBW = 2500000;
                clientBW2 = 2;
                serverBW = 2500000;
                bytesReadTotal = 0;
                lastSentBytesRead = 0;
                numInvokes = 0;

                for (int i = 0; i < RTMPConst.RTMP_CHANNELS; i++)
                {
                    vecChannelsIn[i] = null;
                    vecChannelsOut[i] = null;
                    channelTimestamp[i] = 0;
                } //for

                methodCallDictionary.Clear();
                dMethodLookup.Clear();

                if (wasConnected)
                {
                    foreach (NetStream netStream in netStreamsList)
                    {
                        netStream.internal_NetStreamDisconnectNotify();
                        //
                    } //foreach

                    if (OnDisconnect != null)
                    {
                        MQ_RTMPMessage message = new MQ_RTMPMessage();
                        message.MethodCall = MethodCall.OnEventCallUserCode;
                        message.Params = new object[] { OnDisconnect, this };
                        AddOnEventUserCallCodeToPump(message);
                    }
                }

                // reset is for the next connect
                DisconnectEventSend = false;
            } //lock
        }

        /// <summary>
        /// Create a tcp connection with the rtmp server.
        /// A connection will be created on the calling thread, so it will block until succeded or
        /// throw an exception when not.
        /// </summary>
        /// <returns></returns>
        private bool MQInternal_MakeConnection(params AMFObjectProperty[] amfProperties)
        {
            try
            {
                // connect (lock for tcpSocket protection)
                IAsyncResult result = null;
                lock (lockVAR)
                {
                    tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    // Connect using a timeout (TIMEOUT_MAKE_CONNECTION seconds)
                    result = tcpSocket.BeginConnect(serverLink.Hostname, serverLink.Port, null, null);
                } //lock

                bool timeout = !result.AsyncWaitHandle.WaitOne(RTMPConst.TIMEOUT_MAKE_CONNECTION);

                lock (lockVAR)
                {
                    if (timeout || !tcpSocket.Connected)
                    {
                        // NOTE, MUST CLOSE THE SOCKET
                        tcpSocket.Close();
                        tcpSocket = null;

                        throw new SocketException(10060); // Connection timed out.
                    }

                    tcpSocket.ReceiveTimeout = receiveTimeoutMS;
                    tcpSocket.NoDelay = true;

                    if (!MQInternal_NetConnection_HandShake(true))
                    {
                        return false;
                    }
                    if (!MQInternal_NetConnection_SendConnect(amfProperties))
                    {
                        return false;
                    }

                    // after connection was successfull, set the timeouts for receiving data higher
                    receiveTimeoutMS = RTMPConst.TIMEOUT_RECEIVE * 2;
                    tcpSocket.ReceiveTimeout = receiveTimeoutMS;
                } //lock

                return true;
            }
            catch (Exception e)
            {
                LibRTMPLogger.LogError(e);
            }

            return false;
        }

        /// <summary>
        /// Can also be called from another thread!!
        /// </summary>
        private bool MQInternal_IsConnected
        {
            get
            {
                lock (lockVAR)
                {
                    try
                    {
                        return tcpSocket != null && tcpSocket.Connected && !(tcpSocket.Poll(1, SelectMode.SelectRead) && tcpSocket.Available == 0);
                    }
                    catch (SocketException)
                    {
                        return false;
                    }
                } //lock
            }
        }

        private bool MQInternal_DataInSocket
        {
            get
            {
                lock (lockVAR)
                {
                    return (tcpSocket != null && tcpSocket.Available > 0);
                } //lock
            }
        }

        private void MQInternal_SetContentBufferTime(NetStream netStream, int contentBufferTime)
        {
            if (netStream.Stream_ID > 0 && contentBufferTime > 0)
            {
                SendPing(3, (uint)netStream.Stream_ID, (uint)contentBufferTime);
            }
        }

        #endregion

        #region NetConnection Handshake

        private bool MQInternal_NetConnection_HandShake(bool FP9HandShake)
        {
            // Work with copy of var so we don't have to lock to much, for to long
            ServerLink link;
            lock (lockVAR)
            {
                link = (ServerLink)serverLink.Clone();
            }


            // use the same seed everytime to have the same random numbers everytime (as rtmpdump)
            Random rand = new Random(0);

#if INCLUDE_TMPE
#endif

            int offalg = 0;
            int dhposClient = 0;
            int digestPosClient = 0;
            bool encrypted = (link.Protocol == Protocol.RTMPE || link.Protocol == Protocol.RTMPTE);

            byte[] clientsig = new byte[RTMPConst.RTMP_SIG_SIZE + 1];
            byte[] serversig = new byte[RTMPConst.RTMP_SIG_SIZE];
            byte[] clientResp;

            if (encrypted || swfSize > 0)
            {
                FP9HandShake = true;
            }
            else
            {
                FP9HandShake = false;
            }

            if (encrypted)
            {
                clientsig[0] = 0x06; // 0x08 is RTMPE as well
                offalg = 1;
            }
            else
            {
                clientsig[0] = 0x03;
            }

            int uptime = System.Environment.TickCount;
#if !RANDOM
            byte[] uptime_bytes = BitConverter.GetBytes(0);
#else
            byte[] uptime_bytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(uptime));
#endif
            Array.Copy(uptime_bytes, 0, clientsig, 1, uptime_bytes.Length);

            if (FP9HandShake)
            {
                /* set version to at least 9.0.115.0 */
                if (encrypted)
                {
                    clientsig[5] = 128;
                    clientsig[7] = 3;
                }
                else
                {
                    clientsig[5] = 10;
                    clientsig[7] = 45;
                }
                clientsig[6] = 0;
                clientsig[8] = 2;

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Client type: {0}", clientsig[0]));
            }
            else
            {
                clientsig[5] = 0; clientsig[6] = 0; clientsig[7] = 0; clientsig[8] = 0;
            }

#if !RANDOM
            // Fake random data
            for (int i = 9; i < (RTMPConst.RTMP_SIG_SIZE - 8); i += 4)
            {
                Array.Copy(BitConverter.GetBytes(0), 0, clientsig, i, 4);
            } //for
#else
            // generate random data
            for (int i = 9; i < RTMPConst.RTMP_SIG_SIZE-8; i += 4)
            {
                Array.Copy(BitConverter.GetBytes(rand.Next(ushort.MaxValue)), 0, clientsig, i, 4);
            } //for
#endif

#if INCLUDE_TMPE
#endif

            // set handshake digest
            if (FP9HandShake)
            {
                if (encrypted)
                {
#if INCLUDE_TMPE
#endif
                }

                digestPosClient = (int)RTMPHelper.GetDigestOffset(offalg, clientsig, 1, RTMPConst.RTMP_SIG_SIZE); // reuse this value in verification
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Client digest offset: {0}", digestPosClient));

                RTMPHelper.CalculateDigest(digestPosClient, clientsig, 1, RTMPConst.GenuineFPKey, 30, clientsig, 1 + digestPosClient);

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Initial client digest: ");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, clientsig, 1 + digestPosClient, RTMPConst.SHA256_DIGEST_LENGTH);
            }

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Clientsig:");
            LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, clientsig, 1, RTMPConst.RTMP_SIG_SIZE);
            WriteN(clientsig, 0, RTMPConst.RTMP_SIG_SIZE + 1);

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Initial client clientsig send");

            byte[] singleByteToReadBuffer = new byte[1];
            if (ReadN(singleByteToReadBuffer, 0, 1) != 1)
            {
                return false;
            }
            byte type = singleByteToReadBuffer[0]; // 0x03 or 0x06
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Type Answer   : {0}", type.ToString()));

            if (type != clientsig[0])
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Type mismatch: client sent {0}, server answered {1}", clientsig[0], type));
            }

            if (ReadN(serversig, 0, RTMPConst.RTMP_SIG_SIZE) != RTMPConst.RTMP_SIG_SIZE)
            {
                return false;
            }

            // decode server response
            rtmpServerUptime = (uint)RTMPHelper.ReadInt32(serversig, 0);
            rtmpServerVersion = new Version(serversig[4], serversig[5], serversig[6], serversig[7]);

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Server Uptime : {0}", rtmpServerUptime));
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] FMS Version   : {0}.{1}.{2}.{3}", rtmpServerVersion.Major, rtmpServerVersion.Minor, rtmpServerVersion.Build, rtmpServerVersion.Revision));
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Server signature:");
            LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, serversig, 0, RTMPConst.RTMP_SIG_SIZE);


            if (FP9HandShake && type == 3 && serversig[4] == 0)
            {
                FP9HandShake = false;
            }

            if (FP9HandShake)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] FP9HandShake");

                // we have to use this signature now to find the correct algorithms for getting the digest and DH positions
                int digestPosServer = (int)RTMPHelper.GetDigestOffset(offalg, serversig, 0, RTMPConst.RTMP_SIG_SIZE);

                if (!RTMPHelper.VerifyDigest(digestPosServer, serversig, RTMPConst.GenuineFMSKey, 36))
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Trying different position for server digest!");
                    offalg ^= 1;
                    digestPosServer = (int)RTMPHelper.GetDigestOffset(offalg, serversig, 0, RTMPConst.RTMP_SIG_SIZE);

                    if (!RTMPHelper.VerifyDigest(digestPosServer, serversig, RTMPConst.GenuineFMSKey, 36))
                    {
                        LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection] Couldn't verify the server digest");//,  continuing anyway, will probably fail!\n");
                        return false;
                    }
                }

                // generate SWFVerification token (SHA256 HMAC hash of decompressed SWF, key are the last 32 bytes of the server handshake)
                if (swfHash != null)
                {
                    byte[] swfVerify = new byte[2] { 0x01, 0x01 };
                    Array.Copy(swfVerify, swfVerificationResponse, 2);
                    List<byte> data = new List<byte>();
                    RTMPHelper.EncodeInt32(data, swfSize);
                    RTMPHelper.EncodeInt32(data, swfSize);
                    Array.Copy(data.ToArray(), 0, swfVerificationResponse, 2, data.Count);
                    byte[] key = new byte[RTMPConst.SHA256_DIGEST_LENGTH];
                    Array.Copy(serversig, RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH, key, 0, RTMPConst.SHA256_DIGEST_LENGTH);
                    RTMPHelper.HMACsha256(swfHash, 0, RTMPConst.SHA256_DIGEST_LENGTH, key, RTMPConst.SHA256_DIGEST_LENGTH, swfVerificationResponse, 10);
                }

                // do Diffie-Hellmann Key exchange for encrypted RTMP
                if (encrypted)
                {
#if INCLUDE_TMPE
#endif
                }

                clientResp = new byte[RTMPConst.RTMP_SIG_SIZE];

#if !USERANDOM
                // Fake random data
                for (int i = 0; i < RTMPConst.RTMP_SIG_SIZE; i += 4)
                {
                    Array.Copy(BitConverter.GetBytes(0), 0, clientResp, i, 4);
                }
#else
                // generate random data
                for (int i = 0; i < RTMPConst.RTMP_SIG_SIZE; i += 4)
                {
                    Array.Copy(BitConverter.GetBytes(rand.Next(ushort.MaxValue)), 0, clientResp, i, 4);
                }
#endif

                // calculate response now
                byte[] signatureResp = new byte[RTMPConst.SHA256_DIGEST_LENGTH];
                byte[] digestResp = new byte[RTMPConst.SHA256_DIGEST_LENGTH];

                RTMPHelper.HMACsha256(serversig, digestPosServer, RTMPConst.SHA256_DIGEST_LENGTH, RTMPConst.GenuineFPKey, RTMPConst.GenuineFPKey.Length, digestResp, 0);
                RTMPHelper.HMACsha256(clientResp, 0, RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH, digestResp, RTMPConst.SHA256_DIGEST_LENGTH, signatureResp, 0);

                // some info output
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Calculated digest key from secure key and server digest: ");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, digestResp, 0, RTMPConst.SHA256_DIGEST_LENGTH);

                // FP10 stuff
                if (type == 8)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] RTMPE type 8 XTEA");
                    // encrypt signatureResp
                    for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i += 8)
                    {
                        RTMPHelper.rtmpe8_sig(signatureResp, i, digestResp[i] % 15);
                    } //for
                }
                else if (type == 9)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] RTMPE type 9 Blowfish");
                    // encrypt signatureResp
                    for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i += 8)
                    {
                        RTMPHelper.rtmpe9_sig(signatureResp, i, digestResp[i] % 15);
                    } //for
                }

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Client signature calculated:");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, signatureResp, 0, RTMPConst.SHA256_DIGEST_LENGTH);

                Array.Copy(signatureResp, 0, clientResp, RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH, RTMPConst.SHA256_DIGEST_LENGTH);
            }
            else
            {
                clientResp = serversig;
            }

            WriteN(clientResp, 0, RTMPConst.RTMP_SIG_SIZE);

            // 2nd part of handshake
            byte[] resp = new byte[RTMPConst.RTMP_SIG_SIZE];
            if (ReadN(resp, 0, RTMPConst.RTMP_SIG_SIZE) != RTMPConst.RTMP_SIG_SIZE)
            {
                return false;
            }

            if (FP9HandShake)
            {
                if (resp[4] == 0 && resp[5] == 0 && resp[6] == 0 && resp[7] == 0)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Wait, did the server just refuse signed authentication?");
                }

                // verify server response
                byte[] signature = new byte[RTMPConst.SHA256_DIGEST_LENGTH];
                byte[] digest = new byte[RTMPConst.SHA256_DIGEST_LENGTH];

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Client signature digest position: {0}", digestPosClient));
                RTMPHelper.HMACsha256(clientsig, 1 + digestPosClient, RTMPConst.SHA256_DIGEST_LENGTH, RTMPConst.GenuineFMSKey, RTMPConst.GenuineFMSKey.Length, digest, 0);
                RTMPHelper.HMACsha256(resp, 0, RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH, digest, RTMPConst.SHA256_DIGEST_LENGTH, signature, 0);

                // show some information
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Digest key: ");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, digest, 0, RTMPConst.SHA256_DIGEST_LENGTH);

                // FP10 stuff
                if (type == 8)
                {
                    // encrypt signatureResp
                    for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i += 8)
                    {
                        RTMPHelper.rtmpe8_sig(signature, i, digest[i] % 15);
                    } //for
                }
                else if (type == 9)
                {
                    // encrypt signatureResp
                    for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i += 8)
                    {
                        RTMPHelper.rtmpe9_sig(signature, i, digest[i] % 15);
                    } //for
                }

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Signature calculated:");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, signature, 0, RTMPConst.SHA256_DIGEST_LENGTH);

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Server sent signature:");
                LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, resp, RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH, RTMPConst.SHA256_DIGEST_LENGTH);

                for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i++)
                {
                    if (signature[i] != resp[RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH + i])
                    {
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Server not genuine Adobe!");
                        return false;
                    }
                } //for
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Genuine Adobe Flash Media Server");

                if (encrypted)
                {
#if INCLUDE_TMPE
#endif
                }
            }
            else
            {
                for (int i = 0; i < RTMPConst.RTMP_SIG_SIZE; i++)
                    if (resp[i] != clientsig[i + 1])
                    {
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] client signature does not match!");
                        break; //return false; - continue anyway
                    }
            }

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Handshaking finished....");

            return true;
        }
        #endregion

        #region High level packet routine
        /// <summary>
        /// Decode a RTMP packet from the stream
        /// </summary>
        private bool ReadPacket(out RTMPPacket packet)
        {
            packet = null;

            // eerst checken of er wel dat is
            if (!MQInternal_IsConnected)
            {
                RemoteServerDisconnected();

                // niks te doen
                return false;
            }

            // Chunk Basic Header (1, 2 or 3 bytes)
            // the two most significant bits hold the chunk type
            // value in the 6 least significant bits gives the chunk stream id (0,1,2 are reserved): 0 -> 3 byte header | 1 -> 2 byte header | 2 -> low level protocol message | 3-63 -> stream id
            byte[] singleByteToReadBuffer = new byte[1];
            if (ReadN(singleByteToReadBuffer, 0, 1) != 1)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] failed to read RTMP packet header");
                return false;
            }

            byte type = singleByteToReadBuffer[0];

            byte headerType = (byte)((type & 0xc0) >> 6);
            int channel = (byte)(type & 0x3f);

            if (channel == 0)
            {
                if (ReadN(singleByteToReadBuffer, 0, 1) != 1)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] failed to read RTMP packet header 2nd byte");
                    return false;
                }
                channel = singleByteToReadBuffer[0];
                channel += 64;
                //header++;
            }
            else if (channel == 1)
            {
                int tmp;
                byte[] hbuf = new byte[2];

                if (ReadN(hbuf, 0, 2) != 2)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] failed to read RTMP packet header 3rd and 4th byte");
                    return false;
                }
                tmp = ((hbuf[2]) << 8) + hbuf[1];
                channel = tmp + 64;
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] channel: {0}", channel));
                //header += 2;
            }

            uint nSize = RTMPConst.PacketSize[headerType];

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] reading RTMP packet chunk on channel {0}, headersz {1}", channel, nSize));

            if (nSize < RTMPConst.RTMP_LARGE_HEADER_SIZE)
            {
                // using values from the last message of this channel
                packet = vecChannelsIn[channel];
            }
            else
            {
                packet = new RTMPPacket() { HeaderType = (HeaderType)headerType, Channel = channel, HasAbsTimestamp = true }; // new packet
            }

            nSize--;

            byte[] header = new byte[RTMPConst.RTMP_LARGE_HEADER_SIZE];
            if (nSize > 0 && ReadN(header, 0, (int)nSize) != nSize)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection] failed to read RTMP packet header. type: {0}", type));
                return false;
            }

            if (nSize >= 3)
            {
                packet.TimeStamp = (uint)RTMPHelper.ReadInt24(header, 0);

                if (nSize >= 6)
                {
                    packet.BodySize = (uint)RTMPHelper.ReadInt24(header, 3);
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] new packet body to read {0}", packet.BodySize));
                    packet.BytesRead = 0;
                    packet.Free(); // new packet body

                    if (nSize > 6)
                    {
                        if (Enum.IsDefined(typeof(PacketType), header[6]))
                        {
                            packet.PacketType = (PacketType)header[6];
                        }
                        else
                        {
                            LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection] Unknown packet type received: {0}", header[6]));
                        }

                        if (nSize == 11)
                        {
                            packet.InfoField2 = RTMPHelper.ReadInt32LE(header, 7);
                        }
                    }
                }

                if (packet.TimeStamp == 0xffffff)
                {
                    byte[] extendedTimestampDate = new byte[4];
                    if (ReadN(extendedTimestampDate, 0, 4) != 4)
                    {
                        LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection] failed to read extended timestamp");
                        return false;
                    }
                    packet.TimeStamp = (uint)RTMPHelper.ReadInt32(extendedTimestampDate, 0);
                }
            }

            if (packet.BodySize >= 0 && packet.Body == null && !packet.AllocPacket((int)packet.BodySize))
            {
                //CLog::Log(LOGDEBUG,"%s, failed to allocate packet", __FUNCTION__);
                return false;
            }

            uint nToRead = packet.BodySize - packet.BytesRead;
            uint nChunk = (uint)InChunkSize;
            if (nToRead < nChunk)
                nChunk = nToRead;

            int read = ReadN(packet.Body, (int)packet.BytesRead, (int)nChunk);
            if (read != nChunk)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection] failed to read RTMP packet body. total:{0}/{1} chunk:{2}/{3}", packet.BytesRead, packet.BodySize, read, nChunk));
                packet.Body = null; // we dont want it deleted since its pointed to from the stored packets (m_vecChannelsIn)
                return false;
            }

            packet.BytesRead += nChunk;

            // keep the packet as ref for other packets on this channel
            vecChannelsIn[packet.Channel] = packet.ShallowCopy();

            if (packet.IsReady())
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] packet with {0} bytes read", packet.BytesRead));

                // make packet's timestamp absolute
                if (!packet.HasAbsTimestamp)
                {
                    packet.TimeStamp += channelTimestamp[packet.Channel]; // timestamps seem to be always relative!!
                }
                channelTimestamp[packet.Channel] = packet.TimeStamp;

                // reset the data from the stored packet. we keep the header since we may use it later if a new packet for this channel
                // arrives and requests to re-use some info (small packet header)
                vecChannelsIn[packet.Channel].Body = null;
                vecChannelsIn[packet.Channel].BytesRead = 0;
                vecChannelsIn[packet.Channel].HasAbsTimestamp = false; // can only be false if we reuse header
            }

            return true;
        }

        /// <summary>
        /// Handle when we detect remote server is disconnected
        /// </summary>
        private void RemoteServerDisconnected()
        {
            // Handle disconnect by sending event (once) and resetting everything!
            if (!DisconnectEventSend)
            {
                DisconnectEventSend = true;
                // we do a disconnect by using the message pump makes it cleaner
                MQ_RTMPMessage message = new MQ_RTMPMessage();
                message.MethodCall = MethodCall.RemoteDisconnect;

                AddMessageToPump(message);
            }
        }
        #endregion

        #region Handle Server Packets

        private enum HandlePacketResult
        {
            NoneMediaPacket = 1,
            MediaPacket,
            PlayComplete
        }

        /// <summary>
        /// Handle a received packet.
        /// </summary>
        /// <returns>0 - no media packet, 1 - media packet, 2 - play complete</returns>
        private HandlePacketResult HandleClientPacket(RTMPPacket packet)
        {
            HandlePacketResult result = HandlePacketResult.NoneMediaPacket;

            switch (packet.PacketType)
            {
                case PacketType.ChunkSize:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: ChunkSize."));
                    HandleChangeChunkSize(packet);
                    break;
                case PacketType.BytesReadReport:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: Bytes read report."));
                    break;
                case PacketType.Control:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: Control."));
                    HandlePing(packet);
                    break;
                case PacketType.ServerBW:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: ServerBW."));
                    HandleServerBW(packet);
                    break;
                case PacketType.ClientBW:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: ClientBW."));
                    HandleClientBW(packet);
                    break;
                case PacketType.Audio:
                case PacketType.Video:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: [Media(Audio/Video)] {0} bytes.", packet.BodySize));
                    HandleMedia(packet);
                    result = HandlePacketResult.MediaPacket;
                    break;
                case PacketType.Metadata:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: [MetaData] {0} bytes.", packet.BodySize));
                    HandleMetadata(packet);
                    result = HandlePacketResult.MediaPacket;
                    break;
                case PacketType.Invoke:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: [Invoke] {0} bytes.", packet.BodySize));
                    HandleInvoke(packet);
                    break;
                case PacketType.FlvTags:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: [FlvTags] {0} bytes.", packet.BodySize));
                    HandleFlvTags(packet);
                    result = HandlePacketResult.MediaPacket;
                    break;
                default:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection.HandleClientPacket] PACKET received: [Unknown packet] type {0}.", packet.PacketType));
                    break;
            }

            return result;
        }

        private void HandleChangeChunkSize(RTMPPacket packet)
        {
            if (packet.BodySize >= 4)
            {
                InChunkSize = RTMPHelper.ReadInt32(packet.Body, 0);
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleChangeChunkSize] Chunk size change to {0}", InChunkSize));
            }
        }

        /// <summary>
        /// The type of Ping packet is 0x4 and contains two mandatory parameters and two optional parameters.
        /// The first parameter is the type of Ping (short integer).
        /// The second parameter is the target of the ping.
        /// As Ping is always sent in Channel 2 (control channel) and the target object in RTMP header is always 0
        /// which means the Connection object,
        /// it's necessary to put an extra parameter to indicate the exact target object the Ping is sent to.
        /// The second parameter takes this responsibility.
        /// The value has the same meaning as the target object field in RTMP header.
        /// (The second value could also be used as other purposes, like RTT Ping/Pong. It is used as the timestamp.)
        /// The third and fourth parameters are optional and could be looked upon as the parameter of the Ping packet.
        /// Below is an unexhausted list of Ping messages.
        /// type 0: Clear the stream. No third and fourth parameters.
        ///         The second parameter could be 0. After the connection
        ///         is established, a Ping 0,0 will be sent from server
        ///         to client. The message will also be sent to client on
        ///         the start of Play and in response of a Seek or
        ///         Pause/Resume request. This Ping tells client
        ///         to re-calibrate the clock with the timestamp of the
        ///         next packet server sends.
        /// type 1: Tell the stream to clear the playing buffer.
        /// type 3: Buffer time of the client. The third parameter is the buffer time in millisecond.
        /// type 4: Reset a stream. Used together with type 0 in the case of VOD. Often sent before type 0.
        /// type 6: Ping the client from server. The second parameter is the current time.
        /// type 7: Pong reply from client. The second parameter is the time the server sent with his ping request.
        /// type 26: SWFVerification request
        /// type 27: SWFVerification response
        /// type 31: Buffer empty
        /// type 32: Buffer full
        /// </summary>
        private void HandlePing(RTMPPacket packet)
        {
            short nType = -1;
            if (packet.Body != null && packet.BodySize >= 2)
            {
                nType = (short)RTMPHelper.ReadInt16(packet.Body, 0);
            }

            if (packet.BodySize >= 6)
            {
                uint nTime = (uint)RTMPHelper.ReadInt32(packet.Body, 2);
                switch (nType)
                {
                    case 0:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream Begin {0}", nTime));
                        break;
                    case 1:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream EOF {0}", nTime));
                        break;
                    case 2:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream Dry {0}", nTime));
                        break;
                    case 4: // when this control message is sent, the stream is recorded
                        if (netStreams[nTime] != null) //nTime=stream_id
                        {
                            netStreams[nTime].LiveStream = false;
                            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] stream_id={0}", nTime));
                        }
                        break;
                    case 6:
                        // server ping. reply with pong.
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Ping {0}", nTime));
                        MQ_SendPing(0x07, nTime, 0);
                        break;
                    case 7:
                        // server pong. Do nothing
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Pong received {0}", nTime));
                        break;
                    case 31:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream BufferEmpty {0}", nTime));
                        //if (!mediaLiveStream)
                        {
                            // ---------------------- TODO!!!!!!!!!!!!!!! ---------------------------
                            /*
                            if (pausing == MediaStreamingState.Unknown)
                            {
                                MQInternal_SendPause(true);
                                pausing = MediaStreamingState.Pausing;
                            }
                            else if (pausing == MediaStreamingState.ReceivedEOFStream)
                            {
                                MQInternal_SendPause(false);
                                pausing = MediaStreamingState.ReadyForData;
                            }
                            */
                            // ---------------------- TODO!!!!!!!!!!!!!!! ---------------------------
                        }
                        break;
                    case 32:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream BufferReady {0}", nTime));
                        break;
                    default:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] Stream xx {0}", nTime));
                        break;
                }
            }

            if (nType == 0x1A)
            {
                if (packet.BodySize > 2 && packet.Body[2] > 0x01)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection.HandlePing] SWFVerification Type {0} request not supported! Patches welcome...", packet.Body[2]));
                }
                else if (swfHash != null) // respond with HMAC SHA256 of decompressed SWF, key is the 30 byte player key, also the last 30 bytes of the server handshake are applied
                {
                    MQ_SendPing(0x1B, 0, 0);
                }
                else
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection.HandlePing] Ignoring SWFVerification request, swfhash and swfsize parameters not set!");
                }
            }
        }

        private void HandleServerBW(RTMPPacket packet)
        {
            serverBW = RTMPHelper.ReadInt32(packet.Body, 0);
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleServerBW] server BW = {0}", serverBW));
        }

        private void HandleClientBW(RTMPPacket packet)
        {
            clientBW = RTMPHelper.ReadInt32(packet.Body, 0);
            if (packet.BodySize > 4)
            {
                clientBW2 = packet.Body[4];
            }
            else
            {
                clientBW2 = 0;
            }
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleClientBW] client BW = {0} {1}", clientBW, clientBW2));
        }

        private void HandleMedia(RTMPPacket packet)
        {
            int stream_id = packet.InfoField2;

            if (netStreams[stream_id] != null)
            {
                netStreams[stream_id].HandleOnMediaPacket(packet);
            }
            else
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection.HandleMedia] No NetStream object found in channel {0} ignoring media packet", stream_id));
            }
        }

        private void HandleMetadata(RTMPPacket packet)
        {
            int stream_id = packet.InfoField2;

            // Find matching NetStream and forward this packet.
            if (netStreams[stream_id] == null)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection.HandleMetadata] No matching NetStream found for stream_id {0}", stream_id));
                return;
            }

            AMFObject obj = new AMFObject();
            int nRes = obj.Decode(packet.Body, 0, (int)packet.BodySize, false);
            if (nRes < 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection.HandleMetadata] Error decoding meta data packet");
                return;
            }

            string metastring = obj.GetProperty(0).StringValue;
            switch (metastring)
            {
                case "|RtmpSampleAccess":
                    // Ignore this, unknown
                    return;

                // Looks more as an invoke to me Let NetStream.OnStatus handle it
                case "onStatus": // -=> "NetStream.Data.Start"
                case "onPlayStatus": // -=> "NetStream.Play.Switch"
                case "onMetaData": // This seams to me te real metadata
                case "onID3":
                    // Let NetStream Handle it
                    netStreams[stream_id].HandleOnMetaData(packet);
                    break;

                default:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Warning, string.Format("[CDR.LibRTMP.NetConnection.HandleMetadata] Unknown metadata {0}", metastring));
                    break;
            } //switch


            return;
        }

        private void HandleFlvTags(RTMPPacket packet)
        {
            // go through FLV packets and handle metadata packets
            int pos = 0;
            uint nTimeStamp = packet.TimeStamp;

            while (pos + 11 < packet.BodySize)
            {
                int dataSize = RTMPHelper.ReadInt24(packet.Body, pos + 1); // size without header (11) and prevTagSize (4)

                if (pos + 11 + dataSize + 4 > packet.BodySize)
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection.HandleFlvTags] Stream corrupt?!");
                    break;
                }
                if (packet.Body[pos] == 0x12)
                {
                    // Create "fake packet" convert to a metadatapacket
                    RTMPPacket tmpPacket = (RTMPPacket)packet.Clone();
                    tmpPacket.PacketType = PacketType.Metadata;
                    byte[] newBody = new byte[dataSize];
                    Buffer.BlockCopy(packet.Body, pos + 11, newBody, 0, dataSize);
                    tmpPacket.Body = newBody;
                    tmpPacket.BodySize = Convert.ToUInt32(dataSize);

                    HandleMetadata(tmpPacket);
                }
                else if (packet.Body[pos] == 8 || packet.Body[pos] == 9)
                {
                    nTimeStamp = (uint)RTMPHelper.ReadInt24(packet.Body, pos + 4);
                    nTimeStamp |= (uint)(packet.Body[pos + 7] << 24);
                }
                pos += (11 + dataSize + 4);
            }
        }

        /// <summary>
        /// Analyzes and responds if required to the given <see cref="RTMPPacket"/>.
        /// </summary>
        /// <param name="packet">The packet to inspect and react to.</param>
        /// <returns>0 (false) for OK/Failed/error, 1 for 'Stop or Complete' (true)</returns>
        private bool HandleInvoke(RTMPPacket packet)
        {
            bool ret = false;

            if (packet.Body[0] != 0x02) // make sure it is a string method name we start with
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection.HandleInvoke] Sanity failed. no string method in invoke packet");
                return false;
            }

            AMFObject obj = new AMFObject();
            int nRes = obj.Decode(packet.Body, 0, (int)packet.BodySize, false);
            if (nRes < 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.NetConnection.HandleInvoke] Error decoding invoke packet");
                return false;
            }

            obj.Dump();
            string method = obj.GetProperty(0).StringValue;
            double txn = obj.GetProperty(1).NumberValue;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] Server invoking <{0}>", method));

            if (method == "_result")
            {
                int transactionResultNum = (int)obj.GetProperty(1).NumberValue;
                string methodInvoked = "";
                if (methodCallDictionary.ContainsKey(transactionResultNum))
                {
                    methodInvoked = methodCallDictionary[transactionResultNum];
                    methodCallDictionary.Remove(transactionResultNum);
                }

                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] received result for method call <{0}>", methodInvoked));

                if (methodInvoked == "connect")
                {
                    // Get some info out of the result connection
                    DecodeNetConnectionInfo_Connect_Result(obj);

                    // Is SecureToken activate (when using wowza server)
                    string tmpSecureTokenPassword = string.Empty;
                    lock (lockVAR)
                    {
                        tmpSecureTokenPassword = secureTokenPassword;
                    }
                    if (!string.IsNullOrEmpty(tmpSecureTokenPassword))
                    {
                        List<AMFObjectProperty> props = new List<AMFObjectProperty>();
                        obj.FindMatchingProperty("secureToken", props, int.MaxValue);
                        if (props.Count > 0)
                        {
#if INCLUDE_TMPE
#endif
                        }
                    }
                    MQInternal_SendServerBW();

                    // Send OnConnect event
                }
                else if (methodInvoked == "createStream")
                {
                    int transactionNum = (int)obj.GetProperty(1).NumberValue;
                    int stream_id = (int)obj.GetProperty(3).NumberValue;

                    if (transactionIDReferenceTable.ContainsKey(transactionNum) && transactionIDReferenceTable[transactionNum] is NetStream)
                    {
                        NetStream netStream = (NetStream)transactionIDReferenceTable[transactionNum];
                        transactionIDReferenceTable.Remove(transactionNum);
                        LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] Received createStream(stream_id={0})", stream_id));

                        netStream.Stream_ID = stream_id;
                        // make sure we know which NetStreams use this NetCOnnection
                        RegisterNetStream(netStream);

                        int contentBufferTime = NetConnection.DefaultContentBufferTime;
                        netStream.HandleOnAssignStream_ID(packet, stream_id, out contentBufferTime);
                        if (contentBufferTime <= 0)
                        {
                            contentBufferTime = NetConnection.DefaultContentBufferTime;
                        }
                        // Tell buffer time we want to use for this channel
                        MQ_SendPing(3, (uint)netStream.Stream_ID, (uint)contentBufferTime);
                    }
                    else
                    {
                        // We haven't found a NetStream for which this is intended, so delete it again
                        MQ_SendDeleteStream(stream_id);
                    }
                }
                else if (methodInvoked == "play")
                {
                    // Server send the play command?
                }
            }
            else if (method == "onBWDone")
            {
                if (bwCheckCounter == 0)
                {
                    MQInternal_SendCheckBW();
                }
            }
            else if (method == "_onbwcheck")
            {
                MQInternal_SendCheckBWResult(txn);
            }
            else if (method == "_onbwdone")
            {
                int transactionResultNum = (int)obj.GetProperty(1).NumberValue;
                if (methodCallDictionary.ContainsValue("_checkbw"))
                {
                    var item = methodCallDictionary.First(x => x.Value == "_checkbw");
                    methodCallDictionary.Remove(item.Key);
                }
            }
            else if (method == "_error")
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection.HandleInvoke] rtmp server sent error");
            }
            else if (method == "close")
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection.HandleInvoke] rtmp server requested close");
                CloseConnection();
            }
            else if (method == "onStatus")
            {
                int stream_id = packet.InfoField2;
                string code = obj[3].ObjectValue.GetProperty("code").StringValue;
                string level = obj[3].ObjectValue.GetProperty("level").StringValue;

                LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] stream_id={0}, method={1}, code={2}, level={3}", stream_id, method, code, level));
                // Zoek NetStream op en geef OnStatus door
                if (netStreams[stream_id] != null)
                {
                    if (code == "NetStream.Pause.Notify")
                    {
                        // fix to help netstream
                        packet.TimeStamp = channelTimestamp[netStreams[stream_id].MediaChannel];
                    }
                    netStreams[stream_id].HandleOnStatus(packet);
                }
                else
                {
                    LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] UNHANDLED | stream_id={0}, method={1}, code={2}, level={3}", stream_id, method, code, level));
                }
            }
            else if (dMethodLookup.ContainsKey(method))
            {
                ret = true;

                SynchronizationContext sc;
                State_NC_MethodCall stateMethodCall = new State_NC_MethodCall();
                lock (lockVAR)
                {
                    sc = synchronizationContext;
                    stateMethodCall.Call = dMethodLookup[method];
                } //lock
                stateMethodCall.thisObject = this;
                stateMethodCall.MethodParam = obj;

                if (sc != null)
                {
                    switch (synchronizationContextMethod)
                    {
                        case SynchronizationContextMethod.Post:
                            sc.Post(HandleOnMethodCall, stateMethodCall);
                            break;
                        case SynchronizationContextMethod.Send:
                            sc.Send(HandleOnMethodCall, stateMethodCall);
                            break;
                    } //switch
                }
                else
                {
                    HandleOnMethodCall(stateMethodCall);
                }
            }
            else
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.HandleInvoke] [EVENT]={0}", method));
            }

            return ret;
        }

        /// <summary>
        /// Using SynchronizationContext this is called on another thread
        /// </summary>
        private void HandleOnMethodCall(object state)
        {
            if (!(state is State_NC_MethodCall))
            {
                return;
            }
            State_NC_MethodCall stateMethodCall = state as State_NC_MethodCall;
            stateMethodCall.Call(stateMethodCall.thisObject, stateMethodCall.MethodParam);
        }


        /// <summary>
        /// Decode the amfobject when NetConnection has a successfull connnection.
        /// Don't know if it's of any use and if the same info is available for
        /// different RTMP servers. (Only tested with wowza 3.5.2)
        /// </summary>
        /// <param name="obj"></param>
        private void DecodeNetConnectionInfo_Connect_Result(AMFObject obj)
        {
            netConnectionConnectInfo.Clear();

            // WOWZA: In position 2 and 3 is the info we want as
            // Red5: has all it's info in [3] it seems
            if (obj.Count < 3)
            {
                return;
            }

            List<AMFObjectProperty> props = new List<AMFObjectProperty>();
            props.Clear();

            obj.FindMatchingProperty("fmsVer", props, 1);
            if (props.Count > 0)
            {
                netConnectionConnectInfo.FMSVer = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("capabilities", props, 1);
            if (props.Count > 0)
            {
                try
                {
                    netConnectionConnectInfo.Capabilities = Convert.ToInt32(props[0].NumberValue);
                }
                catch { }
            }
            props.Clear();
            obj.FindMatchingProperty("mode", props, 1);
            if (props.Count > 0)
            {
                try
                {
                    netConnectionConnectInfo.Mode = Convert.ToInt32(props[0].NumberValue);
                }
                catch { }
            }

            props.Clear();
            obj.FindMatchingProperty("code", props, 1);
            if (props.Count > 0)
            {
                netConnectionConnectInfo.Code = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("level", props, 1);
            if (props.Count > 0)
            {
                netConnectionConnectInfo.Level = props[0].StringValue;
            }
            props.Clear();
            obj.FindMatchingProperty("description", props, 1);
            if (props.Count > 0)
            {
                netConnectionConnectInfo.Description = props[0].StringValue;
            }

            props.Clear();
            obj.FindMatchingProperty("data", props, 1);
            if (props.Count > 0)
            {
                AMFObject obj2 = props[0].ObjectValue;
                props.Clear();
                obj2.FindMatchingProperty("version", props, 1);
                if (props.Count > 0)
                {
                    try
                    {
                        netConnectionConnectInfo.Version = new Version(props[0].StringValue.Replace(',', '.'));
                        realRTMPServerVersion = netConnectionConnectInfo.Version;
                    }
                    catch { }
                }
            }

            // Red5 doesn't seem to have this property
            props.Clear();
            obj.FindMatchingProperty("clientid", props, 1);
            if (props.Count > 0)
            {
                try
                {
                    netConnectionConnectInfo.ClientID = Convert.ToInt64(props[0].NumberValue);
                }
                catch { }
            }
            // Red5 doesn't seem to have this property
            props.Clear();
            obj.FindMatchingProperty("objectEncoding", props, 1);
            if (props.Count > 0)
            {
                try
                {
                    netConnectionConnectInfo.ObjectEncoding = Convert.ToInt32(props[0].NumberValue);
                }
                catch { }
            }
        }

        /// <summary>
        /// Make sure when know all NetStreams which use this NetConnection
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void RegisterNetStream(NetStream netStream)
        {
            if (netStream.Stream_ID >= 0 && netStreams[netStream.Stream_ID] == null)
            {
                netStreams[netStream.Stream_ID] = netStream;
            }
            if (!netStreamsList.Contains(netStream))
            {
                netStreamsList.Add(netStream);
            }
        }

        /// <summary>
        /// Remove link to NetStream (for GC to release that object)
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        internal void UnRegisterNetStream(NetStream netStream)
        {
            if (netStream.Stream_ID >= 0 && netStreams[netStream.Stream_ID].Equals(netStream))
            {
                // Unregister
                netStreams[netStream.Stream_ID] = null;
            }
            else
            {
                // Look for it
                if (netStream != null)
                {
                    for (int i = netStreams.GetLowerBound(0); i < netStreams.GetUpperBound(0); i++)
                    {
                        if (netStream.Equals(netStreams[i]))
                        {
                            // Unregister
                            netStreams[i] = null;
                        }
                    } //for i
                }
            }

            if (netStream != null && netStreamsList.Contains(netStream))
            {
                netStreamsList.Remove(netStream);
            }
        }

        #endregion

        #region Properties

        public ServerLink ServerLink
        {
            get
            {
                ServerLink sl;
                lock (lockVAR)
                {
                    sl = (ServerLink)serverLink.Clone();
                }
                return sl;
            }
        }

        public Socket TCPSocket
        {
            get
            {
                // Dit is NIET thread safe! Alleen safe als we zelf de internal van tcpsocket bloot gegeven!
                lock (lockVAR)
                {
                    return tcpSocket;
                }
            }
        }

        public NetConnectionState State
        {
            get
            {
                return netConnectionState;
            }
        }

        /// <summary>
        /// SecureToken used by wowza server
        /// </summary>
        public string SecureTokenPassword
        {
            get
            {
                lock (lockVAR)
                {
                    return secureTokenPassword;
                }
            }
            set
            {
                lock (lockVAR)
                {
                    secureTokenPassword = value;
                }
            }
        }

        #endregion

        #region Send functions (run in thread)

        /// <summary>
        /// The type of Ping packet is 0x4 and contains two mandatory parameters and two optional parameters.
        /// The first parameter is the type of Ping (short integer).
        /// The second parameter is the target of the ping.
        /// As Ping is always sent in Channel 2 (control channel) and the target object in RTMP header is always 0
        /// which means the Connection object,
        /// it's necessary to put an extra parameter to indicate the exact target object the Ping is sent to.
        /// The second parameter takes this responsibility.
        /// The value has the same meaning as the target object field in RTMP header.
        /// (The second value could also be used as other purposes, like RTT Ping/Pong. It is used as the timestamp.)
        /// The third and fourth parameters are optional and could be looked upon as the parameter of the Ping packet.
        /// Below is an unexhausted list of Ping messages.
        /// type 0: Clear the stream. No third and fourth parameters.
        ///         The second parameter could be 0. After the connection
        ///         is established, a Ping 0,0 will be sent from server
        ///         to client. The message will also be sent to client on
        ///         the start of Play and in response of a Seek or
        ///         Pause/Resume request. This Ping tells client
        ///         to re-calibrate the clock with the timestamp of the
        ///         next packet server sends.
        /// type 1: Tell the stream to clear the playing buffer.
        /// type 3: Buffer time of the client. The third parameter is the buffer time in millisecond.
        /// type 4: Reset a stream. Used together with type 0 in the case of VOD. Often sent before type 0.
        /// type 6: Ping the client from server. The second parameter is the current time.
        /// type 7: Pong reply from client. The second parameter is the time the server sent with his ping request.
        /// type 26: SWFVerification request
        /// type 27: SWFVerification response
        /// type 31: Buffer empty
        /// type 32: Buffer full
        /// </summary>
        /// <param name="nType"></param>
        /// <param name="nObject"></param>
        /// <param name="nTime"></param>
        /// <returns></returns>
        private bool MQ_SendPing(short nType, uint nObject, uint nTime)
        {
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.MQ_SendPing] Sending ping type: {0}", nType));

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x02;   // control channel (ping)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Control;
            //packet.m_nInfoField1 = System.Environment.TickCount;

            int nSize = (nType == 0x03 ? 10 : 6); // type 3 is the buffer time and requires all 3 parameters. all in all 10 bytes.
            if (nType == 0x1B) nSize = 44;
            packet.AllocPacket(nSize);
            packet.BodySize = (uint)nSize;

            List<byte> buf = new List<byte>();
            RTMPHelper.EncodeInt16(buf, nType);

            if (nType == 0x1B)
            {
                buf.AddRange(swfVerificationResponse);
            }
            else
            {
                if (nSize > 2)
                {
                    RTMPHelper.EncodeInt32(buf, (int)nObject);
                }

                if (nSize > 6)
                {
                    RTMPHelper.EncodeInt32(buf, (int)nTime);
                }
            }
            packet.Body = buf.ToArray();
            return MQInternal_SendPacket(packet, false);
        }

        /// <summary>
        /// Send Connect after NetConnection Handshake has finished
        /// </summary>
        private bool MQInternal_NetConnection_SendConnect(params AMFObjectProperty[] amfProperties)
        {
            // Work with copy of var so we don't have to lock to much, for to long
            ServerLink link;
            lock (lockVAR)
            {
                link = (ServerLink)serverLink.Clone();
            }

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)
            packet.HeaderType = HeaderType.Large;
            packet.PacketType = PacketType.Invoke;
            packet.AllocPacket(4096);

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Sending connect");
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "connect");
            int transactionNum = ++numInvokes;
            RTMPHelper.EncodeNumber(enc, transactionNum);
            methodCallDictionary.Add(transactionNum, "connect");
            enc.Add(0x03); //Object Datatype
            RTMPHelper.EncodeString(enc, "app", link.Application); LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] app : {0}", link.Application));
            if (String.IsNullOrEmpty(swfFlashVer))
            {
                RTMPHelper.EncodeString(enc, "flashVer", "WIN 10,0,32,18");
            }
            else
            {
                RTMPHelper.EncodeString(enc, "flashVer", swfFlashVer);
            }
            if (!string.IsNullOrEmpty(swfURL)) RTMPHelper.EncodeString(enc, "swfUrl", swfURL);
            RTMPHelper.EncodeString(enc, "tcUrl", link.URL); LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] tcUrl : {0}", link.URL));
            RTMPHelper.EncodeBoolean(enc, "fpad", false);
            RTMPHelper.EncodeNumber(enc, "capabilities", 15.0);
            RTMPHelper.EncodeNumber(enc, "audioCodecs", 3191.0);
            RTMPHelper.EncodeNumber(enc, "videoCodecs", 252.0);
            RTMPHelper.EncodeNumber(enc, "videoFunction", 1.0);
            if (!string.IsNullOrEmpty(swfPageURL))
            {
                RTMPHelper.EncodeString(enc, "pageUrl", swfPageURL);
            }
            enc.Add(0); enc.Add(0); enc.Add(0x09); // end of object - 0x00 0x00 0x09
            // add auth string
            if (!string.IsNullOrEmpty(swfAuth))
            {
                RTMPHelper.EncodeBoolean(enc, true);
                RTMPHelper.EncodeString(enc, swfAuth);
            }
            //EncodeNumber(enc, "objectEncoding", 0.0);
            if (amfProperties != null)
            {
                foreach (AMFObjectProperty prop in amfProperties)
                {
                    prop.Encode(enc);
                } //foreach

                List<byte> objEnc = new List<byte>();
            }

            Array.Copy(enc.ToArray(), packet.Body, enc.Count);
            packet.BodySize = (uint)enc.Count;

            return MQInternal_SendPacket(packet);
        }


        private bool MQ_SendCall(NetStream netStream, string methodName, object[] values)
        {
            // Let's try to encode the values infpo AMF
            List<byte> enc = new List<byte>();
            foreach (object value in values)
            {
                switch (value.GetType().ToString())
                {
                    case "System.Boolean":
                        RTMPHelper.EncodeBoolean(enc, Convert.ToBoolean(value));
                        break;
                    case "System.Byte":
                    case "System.SByte":
                    case "System.Decimal":
                    case "System.Double":
                    case "System.Single":
                    case "System.Int32":
                    case "System.UInt32":
                    case "System.Int64":
                    case "System.UInt64":
                    case "System.Int16":
                    case "System.UInt16":
                        RTMPHelper.EncodeNumber(enc, Convert.ToDouble(value));
                        break;
                    case "System.Char":
                    case "System.String":
                        RTMPHelper.EncodeString(enc, Convert.ToString(value));
                        break;
                    case "System.Object":
                    default:
                        LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection.MQ_SendCall] call(\"{0}\") parameter type not supported for encoding", methodName));
                        break;
                } //switch
            } //for


            int transactionNum = ++numInvokes; // needed incase there is a result which is send back
            // Put netStream in transaction reference table so we can match it up when the rtmp server
            // give us the result back
            transactionIDReferenceTable[transactionNum] = netStream;

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Sending call(\"{0}\")", methodName));
            RTMPHelper.EncodeString(enc, methodName);
            RTMPHelper.EncodeNumber(enc, transactionNum);
            enc.Add(0x05); // NULL

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            methodCallDictionary.Add(transactionNum, methodName);

            return MQInternal_SendPacket(packet);
        }


        private bool MQ_SendCreateStream(NetStream netStream)
        {
            int transactionNum = ++numInvokes;
            // Put netStream in transaction reference table so we can match it up when the rtmp server
            // give us the result back
            transactionIDReferenceTable[transactionNum] = netStream;

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Sending createStream");
            packet.AllocPacket(256); // should be enough
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "createStream");
            RTMPHelper.EncodeNumber(enc, transactionNum);
            enc.Add(0x05); // NULL

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            methodCallDictionary.Add(transactionNum, "createStream");

            return MQInternal_SendPacket(packet);
        }

        private bool MQ_SendDeleteStream(int stream_id)
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Sending deleteStream");
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "deleteStream");
            RTMPHelper.EncodeNumber(enc, 0); // can be 0, because server send no response back
            enc.Add(0x05); // NULL
            RTMPHelper.EncodeNumber(enc, stream_id);

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            // no response expected
            return MQInternal_SendPacket(packet, false);
        }

        /// <summary>
        /// Stream_id checks "netStreams"
        /// Is orginal call pattern: private bool MQInternal_SendPlay(NetStream netStream, string mediafile, int start, int lenToPlay, bool resetPlayList, AMFObjectProperty properties)
        /// </summary>
        private bool MQInternal_SendPlay(NetStream netStream, string mediaFile, int start, int lenToPlay, bool resetPlayList, AMFObjectProperty properties)
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = netStream.CommandChannel; // 0x08 ???
            packet.HeaderType = HeaderType.Large;
            packet.PacketType = PacketType.Invoke;
            packet.InfoField2 = netStream.Stream_ID;

            List<byte> enc = new List<byte>();

            RTMPHelper.EncodeString(enc, "play");
            RTMPHelper.EncodeNumber(enc, 0); // 0 according to spec adobe
            enc.Add(0x05); // NULL

            RTMPHelper.EncodeString(enc, mediaFile);

            /* Optional parameters start and len.
             *
             * start: -2, -1, 0, positive number
             *  -2: looks for a live stream, then a recorded stream, if not found any open a live stream
             *  -1: plays a live stream
             * >=0: plays a recorded streams from 'start' milliseconds
            */
            // RTMPHelper.EncodeNumber(enc, -1000.0d); (liveStream)
            if (start > 0)
            {
                RTMPHelper.EncodeNumber(enc, start);
            }
            else
            {
                RTMPHelper.EncodeNumber(enc, 0.0d);
            }

            // len: -1, 0, positive number
            //  -1: plays live or recorded stream to the end (default)
            //   0: plays a frame 'start' ms away from the beginning
            //  >0: plays a live or recoded stream for 'len' milliseconds
            RTMPHelper.EncodeNumber(enc, lenToPlay);

            // Reset. Optional wether to flush previous playlist
            if (properties == null)
            {
                RTMPHelper.EncodeBoolean(enc, resetPlayList);
            }
            else
            {
                properties.Encode(enc);
            }

            packet.Body = enc.ToArray();
            packet.BodySize = (uint)enc.Count;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Sending play: '{0}'", mediaFile));

            return MQInternal_SendPacket(packet);
        }

        private bool MQ_SendCloseStream(NetStream netStream)
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = netStream.CommandChannel;// netStream.CommandChannel; or 0x03 both work!?
            packet.HeaderType = HeaderType.Large;
            packet.PacketType = PacketType.Invoke;
            packet.InfoField2 = netStream.Stream_ID;

            List<byte> enc = new List<byte>();

            RTMPHelper.EncodeString(enc, "closeStream");
            RTMPHelper.EncodeNumber(enc, 0); // 0 according to spec adobe
            enc.Add(0x05); // NULL

            packet.Body = enc.ToArray();
            packet.BodySize = (uint)enc.Count;

            return MQInternal_SendPacket(packet);
        }

        private bool MQInternal_SendSecureTokenResponse(string resp)
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;	/* control channel (invoke) */
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Sending SecureTokenResponse: {0}", resp));
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "secureTokenResponse");
            RTMPHelper.EncodeNumber(enc, 0.0);
            enc.Add(0x05); // NULL
            RTMPHelper.EncodeString(enc, resp);

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            return MQInternal_SendPacket(packet, false);
        }

        private bool MQInternal_SendServerBW()
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x02;   // control channel (invoke)
            packet.HeaderType = HeaderType.Large;
            packet.PacketType = PacketType.ServerBW;

            packet.AllocPacket(4);
            packet.BodySize = 4;

            List<byte> bytesToSend = new List<byte>();
            RTMPHelper.EncodeInt32(bytesToSend, serverBW); // was hard coded : 0x001312d0
            packet.Body = bytesToSend.ToArray();
            return MQInternal_SendPacket(packet, false);
        }

        private bool MQInternal_SendCheckBW()
        {
            int transactionNum = ++numInvokes;

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)

            packet.HeaderType = HeaderType.Large;
            packet.PacketType = PacketType.Invoke;
            //packet.m_nInfoField1 = System.Environment.TickCount;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.NetConnection] Sending _checkbw");
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "_checkbw");
            RTMPHelper.EncodeNumber(enc, transactionNum);
            enc.Add(0x05); // NULL

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            methodCallDictionary.Add(transactionNum, "_checkbw");

            // triggers _onbwcheck and eventually results in _onbwdone
            return MQInternal_SendPacket(packet, false);
        }

        private bool MQInternal_SendCheckBWResult(double txn)
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x03;   // control channel (invoke)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;
            packet.TimeStamp = (uint)(0x16 * bwCheckCounter); // temp inc value. till we figure it out.

            packet.AllocPacket(256); // should be enough
            List<byte> enc = new List<byte>();
            RTMPHelper.EncodeString(enc, "_result");
            RTMPHelper.EncodeNumber(enc, txn);
            enc.Add(0x05); // NULL
            RTMPHelper.EncodeNumber(enc, (double)bwCheckCounter++);

            packet.BodySize = (uint)enc.Count;
            packet.Body = enc.ToArray();

            return MQInternal_SendPacket(packet, false);
        }

        private bool MQInternal_SendPause(NetStream netStream, bool doPause)
        {
            int transactionNum = ++numInvokes;

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = netStream.CommandChannel; // 0x08 ???
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;

            List<byte> enc = new List<byte>();

            RTMPHelper.EncodeString(enc, "pause");
            RTMPHelper.EncodeNumber(enc, transactionNum);
            enc.Add(0x05); // NULL
            RTMPHelper.EncodeBoolean(enc, doPause);
            RTMPHelper.EncodeNumber(enc, (double)channelTimestamp[netStream.MediaChannel]);

            packet.Body = enc.ToArray();
            packet.BodySize = (uint)enc.Count;

            LibRTMPLogger.Log(LibRTMPLogLevel.Info, string.Format("[CDR.LibRTMP.NetConnection] Sending pause: ({0}), Time = {1}/{2}", doPause.ToString(), channelTimestamp[netStream.MediaChannel], TimeSpan.FromMilliseconds(Convert.ToInt32(channelTimestamp[netStream.MediaChannel]))));

            methodCallDictionary.Add(transactionNum, "pause");

            return MQInternal_SendPacket(packet);
        }

        private bool MQInternal_SendSeek(NetStream netStream, long seekTimeInMS)
        {
            int transactionNum = ++numInvokes;

            RTMPPacket packet = new RTMPPacket();
            packet.Channel = netStream.CommandChannel; // 0x08 ???
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.Invoke;
            packet.TimeStamp = 0;
            packet.InfoField2 = 2;
            packet.HasAbsTimestamp = false;

            List<byte> enc = new List<byte>();

            RTMPHelper.EncodeString(enc, "seek");
            RTMPHelper.EncodeNumber(enc, transactionNum);
            enc.Add(0x05); // NULL
            RTMPHelper.EncodeNumber(enc, seekTimeInMS); // number of milliseconds to seek into playlist

            packet.Body = enc.ToArray();
            packet.BodySize = (uint)enc.Count;

            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Sending seek: ({0})", seekTimeInMS.ToString()));

            methodCallDictionary.Add(transactionNum, "seek");

            return MQInternal_SendPacket(packet);
        }

        private bool MQInternal_SendBytesReceived()
        {
            RTMPPacket packet = new RTMPPacket();
            packet.Channel = 0x02;   // control channel (invoke)
            packet.HeaderType = HeaderType.Medium;
            packet.PacketType = PacketType.BytesReadReport;

            packet.AllocPacket(4);
            packet.BodySize = 4;

            List<byte> enc = new List<byte>();
            lock (lockVAR)
            {
                RTMPHelper.EncodeInt32(enc, bytesReadTotal);
                packet.BodySize = (uint)enc.Count;
                packet.Body = enc.ToArray();

                lastSentBytesRead = bytesReadTotal;
                LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.NetConnection] Send bytes report. ({0} bytes)", bytesReadTotal));
            }
            return MQInternal_SendPacket(packet, false);
        }

        /// <summary>
        /// Do the last assembling and send the packet on it's way
        /// </summary>
        private bool MQInternal_SendPacket(RTMPPacket packet, bool queue = true)
        {
            uint last = 0;
            uint t = 0;

            RTMPPacket prevPacket = vecChannelsOut[packet.Channel];
            if (packet.HeaderType != HeaderType.Large && prevPacket != null)
            {
                // compress a bit by using the prev packet's attributes
                if (prevPacket.BodySize == packet.BodySize && prevPacket.PacketType == packet.PacketType &&
                    packet.HeaderType == HeaderType.Medium)
                {
                    packet.HeaderType = HeaderType.Small;
                }
                if (prevPacket.TimeStamp == packet.TimeStamp && packet.HeaderType == HeaderType.Small)
                {
                    packet.HeaderType = HeaderType.Minimum;
                }

                last = prevPacket.TimeStamp;
            }

            uint nSize = RTMPConst.PacketSize[(byte)packet.HeaderType];
            t = packet.TimeStamp - last;
            List<byte> header = new List<byte>();//byte[RTMP_LARGE_HEADER_SIZE];
            byte c = (byte)(((byte)packet.HeaderType << 6) | packet.Channel);
            header.Add(c);
            if (nSize > 1)
            {
                RTMPHelper.EncodeInt24(header, (int)t);
            }

            if (nSize > 4)
            {
                RTMPHelper.EncodeInt24(header, (int)packet.BodySize);
                header.Add((byte)packet.PacketType);
            }

            if (nSize > 8)
            {
                RTMPHelper.EncodeInt32LE(header, packet.InfoField2);
            }

            uint hSize = nSize;
            byte[] headerBuffer = header.ToArray();
            nSize = packet.BodySize;
            byte[] buffer = packet.Body;
            uint bufferOffset = 0;
            uint nChunkSize = (uint)outChunkSize;
            while (nSize + hSize > 0)
            {
                if (nSize < nChunkSize)
                {
                    nChunkSize = nSize;
                }

                if (hSize > 0)
                {
                    byte[] combinedBuffer = new byte[headerBuffer.Length + nChunkSize];
                    Array.Copy(headerBuffer, combinedBuffer, headerBuffer.Length);
                    Array.Copy(buffer, (int)bufferOffset, combinedBuffer, headerBuffer.Length, (int)nChunkSize);
                    WriteN(combinedBuffer, 0, combinedBuffer.Length);
                    hSize = 0;
                }
                else
                {
                    WriteN(buffer, (int)bufferOffset, (int)nChunkSize);
                }

                nSize -= nChunkSize;
                bufferOffset += nChunkSize;

                if (nSize > 0)
                {
                    byte sep = (byte)(0xc0 | c);
                    hSize = 1;
                    headerBuffer = new byte[1] { sep };
                }
            } //while

            vecChannelsOut[packet.Channel] = packet;

            return true;
        }

        #endregion

        #region Byte Read/Write (Lowest level before socket)

        private int ReadN(byte[] buffer, int offset, int size)
        {
            if (!MQInternal_IsConnected)
            {
                RemoteServerDisconnected();
                return 0;
            }

            // keep reading until wanted amount has been received or timeout after nothing has been received is elapsed
            byte[] data = new byte[size];
            int readThisRun = 0;
            int i = receiveTimeoutMS / 100;
            while (readThisRun < size)
            {
                int read = 0;
                lock (lockVAR)
                {
                    read = tcpSocket.Receive(data, readThisRun, size - readThisRun, SocketFlags.None);
                } //lock

                // decrypt if needed
                if (read > 0)
                {
                    // We read something so reset timer for keepalive
                    dtNetConnectionKeepAlive = DateTime.Now;
#if INCLUDE_TMPE
#endif
                    {
                        Array.Copy(data, readThisRun, buffer, offset + readThisRun, read);
                    }

                    readThisRun += read;

                    bytesReadTotal += read;

                    if (bytesReadTotal > lastSentBytesRead + (clientBW / 2)) MQInternal_SendBytesReceived(); // report bytes read

                    i = receiveTimeoutMS / 100; // we just got some data, reset the receive timeout
                }
                else
                {
                    if (!MQInternal_IsConnected)
                    {
                        RemoteServerDisconnected();
                        return readThisRun;
                    }

                    i--;
                    System.Threading.Thread.Sleep(100);
                    if (i <= 0)
                    {
                        return readThisRun;
                    }
                }
            } //while

            return readThisRun;
        }

        private void WriteN(byte[] buffer, int offset, int size)
        {
            if (!MQInternal_IsConnected)
            {
                RemoteServerDisconnected();
                return;
            }

            // encrypt if needed
            lock (lockVAR)
            {
#if INCLUDE_TMPE
#endif
                {
                    tcpSocket.Send(buffer, offset, size, SocketFlags.None);
                }
            } //lock
        }

        #endregion


        #region Internal classes

        private class State_NC_ResultCallBackConnect
        {
            public object thisObject;
            public NC_ResultCallBackConnect OnResult = null;
            public bool Success = false;
        }

        private class State_NC_MethodCall
        {
            public NC_MethodCallBack Call;
            public object thisObject;
            public AMFObject MethodParam;
        }

        #endregion

    }

}
