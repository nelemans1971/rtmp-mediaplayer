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

// This library is only tested with a wowza (http://www.wowza.com/) RTMP server
// It should work with others like Adbobe FMS or red5 but it's not tested.
//
// TODO
// High
//   - Automatic Reconnect
//   - Calling RTMP server to get real length of audio (not needed anymore for wowza 3.5.2.0)
// Medium
//   - RTMPT/RTMPTE protocol support
// Low priority
//   - RTMPS support (SSL)
//   - Return value for calling functions in c# code 
//   - AMF3 support
//
namespace CDR.LibRTMP
{
    public class ServerLink : ICloneable
    {
        private Protocol protocol;
        private string hostname;
        private ushort port;
        public string application;

        private ServerLink()
        {
        }

        public ServerLink(Protocol protocol, string hostname, ushort port, string application)
        {
            this.protocol = protocol;
            this.hostname = hostname;
            this.port = port;
            this.application = application;
        }

        public ServerLink(string connectUrl)
        {
            Uri uri = new Uri(connectUrl);
            switch (uri.Scheme.ToLower())
            {
                case "rtmp":
                    protocol = Protocol.RTMP;
                    break;
                case "rtmpe":
                    protocol = Protocol.RTMPE;
                    break;
                case "rtmpt": // not supported
                case "rtmps": // not supported
                case "rtmpte": // not supported
                case "rtmfp": // not supported
                default:
                    protocol = Protocol.UNDEFINED;
                    break;
            } //switch

            hostname = uri.Host;
            port = Convert.ToUInt16(uri.Port);
            application = uri.AbsolutePath;
            if (application.Length >= 1 && application[0] == '/')
            {
                application = application.Substring(1);
            }
        }

        /// <summary>
        /// ICloneable.Clone()
        /// </summary>
        public object Clone()
        {
            ServerLink sl = new ServerLink();
            sl.protocol = protocol;
            sl.hostname = hostname;
            sl.port = port;
            sl.application = application;

            return sl;
        }

        public Protocol Protocol
        {
            get
            {
                return protocol;
            }
        }

        public string Hostname
        {
            get
            {
                return hostname;
            }
        }

        public ushort Port
        {
            get
            {
                return port;
            }
        }

        public string Application
        {
            get
            {
                return application;
            }
        }

        public string URL
        {
            get
            {
                return string.Format("{0}://{1}:{2}/{3}", protocol.ToString().ToLower(), hostname, port, application);
            }
        }
    }

    public enum MethodCall
    {
        None = 0,
        ConnectRTMPServer, // NetConnection.Connect (parameter serverlink)
        Call,
        RemoteDisconnect,  // (unexpected) disconnect from RTMP server
        CloseConnectionRTMPServer,   // Close connection to RTMP server (will also trigger an disconnect event)

        SendPing,
        CreateStream,      // New NetStream
        DeleteStream,      // Diposed NetStream
        Play,
        CloseStream,
        Pause,
        Seek,

        // private calls from NetStream
        MQInternal_SetContentBufferTime,

        OnEventCallUserCode   // Call usercode (always called using SynchronizationContext if set)
    }

    public class MQ_RTMPMessage
    {
        public MethodCall MethodCall;
        public object[] Params;
    }

    public enum MediaPacketType
    {
        Unknown,
        Audio,
        Video
    }


    /// <summary>
    /// A helper class for pinning a managed structure so that it is suitable for
    /// unmanaged calls. A pinned object will not be collected and will not be moved
    /// by the GC until explicitly freed.
    /// </summary>
    public class PinnedObject<T> : IDisposable where T : struct
    {
        protected T managedObject;
        protected System.Runtime.InteropServices.GCHandle handle;
        protected IntPtr ptr;
        protected bool disposed;

        public T ManangedObject
        {
            get
            {
                return (T)handle.Target;
            }
            set
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(value, ptr, false);
            }
        }

        public IntPtr Pointer
        {
            get { return ptr; }
        }

        public PinnedObject()
        {
            handle = System.Runtime.InteropServices.GCHandle.Alloc(managedObject, System.Runtime.InteropServices.GCHandleType.Pinned);
            ptr = handle.AddrOfPinnedObject();
        }

        ~PinnedObject()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                handle.Free();
                ptr = IntPtr.Zero;
                disposed = true;
            }
        }
    }

}
