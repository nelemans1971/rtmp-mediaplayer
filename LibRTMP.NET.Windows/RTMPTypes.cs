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
using System.Text;
using System.Threading.Tasks;

namespace CDR.LibRTMP
{
    public enum Protocol : short
    {
        UNDEFINED = -1,
        RTMP = 0,
        RTMPT = 1,
        RTMPS = 2,
        RTMPE = 3,
        RTMPTE = 4,
        RTMFP = 5
    };

    public enum PacketType : byte
    {
        Undefined = 0x00,
        ChunkSize = 0x01,
        Abort = 0x02,
        BytesReadReport = 0x03,
        Control = 0x04,
        ServerBW = 0x05,
        ClientBW = 0x06,
        Audio = 0x08,
        Video = 0x09,
        Metadata_AMF3 = 0x0F,
        SharedObject_AMF3 = 0x10,
        Invoke_AMF3 = 0x11,
        Metadata = 0x12,
        SharedObject = 0x13,
        Invoke = 0x14,
        FlvTags = 0x16
    };

    public enum HeaderType : byte
    {
        Large = 0,
        Medium = 1,
        Small = 2,
        Minimum = 3
    };


    public enum NetConnectionState
    {
        Connecting = 0,
        Connected,
        Disconnected
    }

    public struct NetConnectionConnectInfo
    {
        // (2)
        public string FMSVer;
        public int Capabilities;
        public int Mode;
        // (3)
        public string Code;
        public string Level;
        public string Description;
        public Version Version;
        public long ClientID;
        public int ObjectEncoding;

        public void Clear()
        {
            FMSVer = string.Empty;
            Capabilities = -1;
            Mode = -1;
            // (3)
            Code = string.Empty;
            Level = string.Empty;
            Description = string.Empty;
            // parameters for (3)
            Version = new Version(0, 0, 0, 0);
            ClientID = -1;
            ObjectEncoding = -1;
        }
    }

    public struct AudioMetaData : ICloneable
    {
        public bool Valid;

        public string SongTitle;
        public string LeadArtist;
        public string AlbumTitle;
        public string YearReleased;
        public string SongComment;
        public string SongGenre;
        public string TrackNumberOnAlbum;

        public void Clear()
        {
            Valid = false;

            SongTitle = string.Empty;
            LeadArtist = string.Empty;
            AlbumTitle = string.Empty;
            YearReleased = string.Empty;
            SongComment = string.Empty;
            SongGenre = string.Empty;
            TrackNumberOnAlbum = string.Empty;
        }

        /// <summary>
        /// Deep clone
        /// </summary>
        public object Clone()
        {
            AudioMetaData clone = new AudioMetaData();

            clone.Valid = Valid;
            clone.SongTitle = SongTitle;
            clone.LeadArtist = LeadArtist;
            clone.AlbumTitle = AlbumTitle;
            clone.YearReleased = YearReleased;
            clone.SongComment = SongComment;
            clone.SongGenre = SongGenre;
            clone.TrackNumberOnAlbum = TrackNumberOnAlbum;

            return clone;
        }
    }

    /// <summary>
    /// Used for NetStream.OnStatus
    /// </summary>
    public struct NetStreamStatusEvent : ICloneable
    {
        public string Event;
        public string Code;
        public string Level;
        public AMFObject EventInfo;

        public void Clear()
        {
            Event = string.Empty;
            Code = string.Empty;
            Level = string.Empty;
            EventInfo = null;
        }

        /// <summary>
        /// Deep clone
        /// </summary>
        public object Clone()
        {
            NetStreamStatusEvent clone = new NetStreamStatusEvent();
            clone.Event = Event;
            clone.Code = Code;
            clone.Level = Level;
            clone.EventInfo = (AMFObject)EventInfo.Clone();

            return clone;
        }
    }

    public enum SynchronizationContextMethod
    {
        Post = 0, // asyncchrone (Netconnection won't wait)
        Send      // synchrone (NetConnection will wait)
    }


    // ---------------------------------------------------------------------------------
    // Most Delegates are call from de messagepump loop, so we can avoid problems 
    // with exclusive locks. Only a few delegates wich expect a return value 
    // on which base we continue are called in place (and we watch very carefully 
    // we dont't lock anything)
    // ---------------------------------------------------------------------------------
    //
    // Global delegates for callback result
    public delegate void NC_ResultCallBackBool(bool result); // called from calling function
    public delegate void NC_ResultCallBackConnect(object sender, bool success); // called from calling function
    //
    // NetConnection delegates
    public delegate void NC_OnConnect(object sender); // see NetConnection.HandleOnEventCallUserCode
    public delegate void NC_OnDisconnect(object sender); // see NetConnection.HandleOnEventCallUserCode
    public delegate void NC_OnTick(object sender); // is implemented in place because of calling sequence and speed

    // Usedfor call methods
    public delegate void NC_MethodCallBack(object sender, AMFObject obj);
    //
    //
    // NetStream delegates
    // The netStream got a valid stream_id from the RTMP server
    public delegate void NS_OnAssignStream_ID(object sender, int stream_id, ref int contentBufferTime); // is implemented in place because of ref
    //
    public delegate void NS_OnStatus(object sender, NetStreamStatusEvent netStreamStatusEvent); // see NetConnection.HandleOnEventCallUserCode
    //
    public delegate void NS_OnID3(object sender, AudioMetaData audioMetaData, AMFObject obj); // see NetConnection.HandleOnEventCallUserCode
    //
    public delegate void NC_OnMediaPacket(object sender, TimeSpan timeStamp, byte[] data); // see NetConnection.HandleOnEventCallUserCode
    //
    // Easy NetStream Events
    public delegate void NS_OnTick(object sender); // see NetConnection.HandleOnEventCallUserCode
    public delegate void NS_OnStreamStart(string mediaFile); // see NetConnection.HandleOnEventCallUserCode
    public delegate void NS_OnStreamStop(string mediaFile); // see NetConnection.HandleOnEventCallUserCode
    //
    // Shortly new MediaFile will be send (old has ended)
    public delegate void NS_OnSwitchStream(object sender); // see NetConnection.HandleOnEventCallUserCode
    //
    // pause/unpause playing
    public delegate void NS_OnPauseStream(object sender, bool doPause); // see NetConnection.HandleOnEventCallUserCode
}
