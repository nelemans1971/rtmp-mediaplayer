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

namespace CDR.LibRTMP
{
    public class RTMPPacket: ICloneable
    {
        private HeaderType headerType;
        private PacketType packetType;
        private bool hasAbsTimestamp; // timestamp absolute or relative?
        private int channel;
        private uint timeStamp; // timestamp 
        private int infoField2; // last  4 bytes in a long header        
        private uint bytesRead;
        private uint bodySize;
        private byte[] body;

        public bool IsReady() 
        { 
            return bytesRead == bodySize; 
        }

        public RTMPPacket()
        {
            Reset();
        }

        ~RTMPPacket()
        {
            FreePacket();
        }
        
        public object Clone()
        {
            RTMPPacket clone = new RTMPPacket();

            clone.headerType = headerType;
            clone.packetType = packetType;
            clone.hasAbsTimestamp = hasAbsTimestamp;
            clone.channel = channel;
            clone.timeStamp = timeStamp;
            clone.infoField2 = infoField2;
            clone.bytesRead = bytesRead;
            clone.bodySize = bodySize;
            clone.body = (byte[])body.Clone();

            return clone;
        }
        

        public void Reset()
        {
            headerType = 0;
            packetType = 0;
            channel = 0;
            timeStamp = 0;
            infoField2 = 0;
            bodySize = 0;
            bytesRead = 0;
            hasAbsTimestamp = false;            
            body = null;
        }

        public bool AllocPacket(int nSize)
        {
            body = new byte[nSize];
            bytesRead = 0;
            return true;
        }

        public void FreePacket()
        {
            Free();
            Reset();
        }

        public void Free()
        {
            body = null;
        }

        public void Dump()
        {
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("[CDR.LibRTMP.RTMPPACKET] packet type: 0x%02x. channel: 0x%02x. info 1: %d info 2: %d. Body size: %lu. body: 0x%02x", packetType, channel, timeStamp, infoField2, bodySize, body != null ? body[0].ToString() : "0"));
        }

        public RTMPPacket ShallowCopy()
        {
            return (RTMPPacket)this.MemberwiseClone();
        }

        #region properties

        public HeaderType HeaderType
        {
            get
            {
                return headerType;
            }
            set
            {
                headerType = value;
            }
        }


        public PacketType PacketType
        {
            get
            {
                return packetType;
            }
            set
            {
                packetType = value;
            }
        }

        public bool HasAbsTimestamp
        {
            get
            {
                return hasAbsTimestamp;
            }
            set
            {
                hasAbsTimestamp = value;
            }
        }

        public int Channel
        {
            get
            {
                return channel;
            }
            set
            {
                channel = value;
            }
        }

        public uint TimeStamp
        {
            get
            {
                return timeStamp;
            }
            set
            {
                timeStamp = value;
            }
        }

        public int InfoField2
        {
            get
            {
                return infoField2;
            }
            set
            {
                infoField2 = value;
            }
        }

        public uint BytesRead
        {
            get
            {
                return bytesRead;
            }
            set
            {
                bytesRead = value;
            }
        }

        public uint BodySize
        {
            get
            {
                return bodySize;
            }
            set
            {
                bodySize = value;
            }
        }

        public byte[] Body
        {
            get
            {
                return body;
            }
            set
            {
                body = value;
            }
        }        
        #endregion

    }
}

