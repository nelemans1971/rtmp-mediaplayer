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
using System.Threading;
using System.Threading.Tasks;
using CDR.LibRTMP;
using libZPlay;

namespace Red5Test
{
    class TestRun
    {
        private ZPlay zPlay = null;
        private CircularBlockBuffer zPlayBuffer = null;
        private NetConnection netConnection = null;

        public void Run()
        {
            zPlayBuffer = new CircularBlockBuffer();
            zPlay = new ZPlay();
            netConnection = new NetConnection();
            netConnection.OnDisconnect += new NC_OnDisconnect(OnDisconnect);
            netConnection.OnTick += new NC_OnTick(NC_OnTick);
            try
            {
                int result = -1;
                // This is to connect to default vod app
                netConnection.Connect(new ServerLink("rtmp://localhost:1935/vod"), new NC_ResultCallBackConnect((sender, success) =>
                {
                    // Runs in RTMP thread (NOT MainThread!!!)
                    Console.WriteLine("NetConnection.Connect => Success=" + success.ToString());

                    if (success)
                    {
                        result = 1;
                    }
                    else
                    {
                        result = 0;
                    }
                }));

                // Wait until we are connected (needed because we run async)
                while (result == -1)
                {
                    Thread.Sleep(100);
                } //while


                // Succes for connecting to rtmp server
                if (result == 1)
                {
                    NetStream netStream = new NetStream(netConnection);
                    netStream.OnStatus += new NS_OnStatus(NS_OnStatus);
                    netStream.OnAudioPacket += new NC_OnMediaPacket(NC_OnMediaPacket);

                    netStream.WaitForValidStream_ID(4000); // wait max 4 seconds for the netstream to become valid (for real test connect to event)

                    // This is to get and MP3 stream
                    netStream.Play("Comfort_Fit_-_03_-_Sorry.mp3", 0, -1, true);
                }


                Console.WriteLine("Press enter to stop.");
                Console.ReadLine();
            }
            finally
            {
                // Cleanup
                if (netConnection != null)
                {
                    netConnection.Close();
                    netConnection = null;
                }
                if (zPlay != null)
                {
                    TStreamStatus status = new TStreamStatus();
                    zPlay.GetStatus(ref status);
                    if (status.fPlay)
                    {
                        zPlay.StopPlayback();
                    }

                    zPlay.Close();
                    zPlay = null;
                }
                if (zPlayBuffer != null)
                {
                    zPlayBuffer = null;
                }
            }
        }

        private void OnDisconnect(object sender)
        {
            Console.WriteLine("\r\nDisconnected from RTMP server.\r\n");
        }
        
        private void NS_OnStatus(object sender, NetStreamStatusEvent netStreamStatusEvent)
        {
            if (netStreamStatusEvent.Code == "NetStream.Play.Complete")
            {
                Console.WriteLine("RTMP NetStream has ended sending data. (Audio playing will stop some time later)");
            }
        }

        private void NC_OnMediaPacket(object sender, TimeSpan timeStamp, byte[] data)
        {
            // Store audio packets in Circulair (auto grow) buffer
            zPlayBuffer.Write(data);
        }

        /// <summary>
        /// Gets called on average every 500ms. This is a console application, so no SynchronizationContext
        /// available. This means this function is called on theNetConnection thread
        /// watch out for that.!
        /// </summary>
        private void NC_OnTick(object sender)
        {
            // feed zplay with data received from netstream
            // Minimum buffer voordat we starten met afspelen                                    
            TStreamStatus status = new TStreamStatus();
            zPlay.GetStatus(ref status);

            // Not playing and buffer has enough data to examine mp3 to start playing
            if (!status.fPlay && zPlayBuffer.UsedBytes >= 8192)
            {
                byte[] tmpBuffer = new byte[8192];
                zPlayBuffer.Read(tmpBuffer, 8192);

                if (!(zPlay.OpenStream(true, true, ref tmpBuffer, 8192, TStreamFormat.sfMp3)))
                {
                    // Got an error with libzplay
                    Console.WriteLine(zPlay.GetError());
                    return;
                }
                // Start playing audio
                zPlay.StartPlayback();
            }
            else if (status.fPlay && zPlayBuffer.UsedBytes > 0)
            {
                int bufRead = Convert.ToInt32(zPlayBuffer.UsedBytes);
                byte[] tmpBuffer = new byte[bufRead];
                zPlayBuffer.Read(tmpBuffer, bufRead);

                // Push data into libzplay so it can continue playing audio
                if (!zPlay.PushDataToStream(ref tmpBuffer, Convert.ToUInt32(bufRead)))
                {
                    // Got an error with libzplay
                    Console.WriteLine(zPlay.GetError());
                    return;
                }
            }
        }
    }
}