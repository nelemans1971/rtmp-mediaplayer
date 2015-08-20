# rtmp-mediaplayer v 1.05 #


# LibRTMP #

LibRTMP is an open source C# api to connect to an rtmp server. It's mainly
geared toward streaming audio, although an event is exposed for video (but not
tested)

# LibMediaplayer #

LibMediaplayer is build upon LibRTMP and exposes an api to build a mediaplayer
which uses rtmp to stream the audio to your soundcard. The BASS (audio) library
from us4seen was chosen to output the audio, because it's available on many
different platforms. Both libraries have been tested under Windows 7, iOS and
Android. For iOS an Android Xamarin development enviroment was used.


# License #

The code is copyrighted by Stichting Centrale Discotheek Rotterdam, my employer,
and licensed under the GPLv2 license (http://www.gnu.org/licenses/gpl-2.0.html).
I got permission from my employer to share a large part of the code for all to
use. The only part that was removed was rtmpe support.


# Why? #

I wrote this code because the company I work for needed a rtmp library to
connect to our streaming server, where we expose all our audio files. After
searching on the internet I couldn't find a suitable .net library and was forced
to write mine own. We needed something to could be used under windows, iOS en
Android. Most of the code is in c#. Visual Studio 2012 is used for windows and
the Xamarin enviroment for iOS and Android.

# External Libraries #

Audio: The bass library is used see http://www.un4seen.com/bass.html‎

Encyption: the Bouncy Castle Crypto APIs is used see http://www.bouncycastle.org/csharp/
We don't claim any copyright on these libraries


# Getting started #

Setting up a rtmp server test enviroment. I will describe a wowza(=streaming
server) setup. This it what I used for developing the LibRTMP library, and what
we use in production (we run wowza on amazone EC2)

1. Download wowza (at the time of this writing the latest version is 3.6.2)
> Goto http://www.wowza.com/pricing/installer en select the windows version
> direct link http://www.wowza.com/downloads/WowzaMediaServer-3-6-2/WowzaMediaServer-3.6.2.exe
2. Sign up for a free developer license. You can't use it for production, but for
> setting up a development enviroment it's great.
> Goto http://www.wowza.com/media-server/developers/license
3. Download Java runtime enviroment from http://java.com/en/download/index.jsp
4. Download some audio files
> Goto http://freemusicarchive.org/curator/creative_commons
> eg
> http://freemusicarchive.org/music/download/fb47aed67f43888e09032d3e26e928a4273daa4d (Comfort\_Fit_-_03_-_Sorry.mp3)
> http://freemusicarchive.org/music/download/b448c9eabb8d1c1f3cb36e5b10bead77f20b061e (Kriss_-_03_-_jazz\_club.mp3)
> http://freemusicarchive.org/music/download/0ac9b197bceac21e092c5e67e28df6528cd43614 (Monopole_-_02_-_Stereo-vision\_radio.mp3)
> http://freemusicarchive.org/music/download/40b6e8bb15b3224670593fd7b17ecdd0e8cac05e (Paper\_Navy_-_08_-_Swan\_Song.mp3)
5. Install the software.
> 5.1 Begin with the Java enviroment
> 5.2 Install wowza. I did it in "C:\Wowza" for easy and fast access to the settings
> > files & content

> 5.3 Copy the four mp3 files to "C:\wowza\content"
> > They are used in the examples.
6. Edit the file "C:\wowza\bin\startup.bat". Remove or put rem at the start of
> > the line which contains "WowzaInfo.exe" to remove popup.


> Start wowza C:\wowza\bin\startup.bat

> The rtmp server is accessible at localhost:1935 (use the command
> "telnet localhost 1935" to test this)

You're now ready with the server part.


# Examples #

The examples and code are for Visual Studio 2012.
The exact same code also compiles under xamarin for iOS and Android, projects
for this are not included though. I use Project Linker 2012
(http://visualstudiogallery.msdn.microsoft.com/273dbf44-55a1-4ac6-a1f3-0b9741587b9a)
to automatically accomplish this.

Start Visual Studio 2012 and open the Media\_Solution.sln file.

It contains 2 libraries:
  * LibRTMP.NET.Windows
  * LibMediaplayer.Windows

and 2 examples:
  * TestMediaPlayer.Console
  * TestMediaPlayer.WinForm


If you have setup the test enviroment on the same pc as you compile/run this
code, it should run without any problems (otherwise change the url now pointing
to localhost:1935 to your own location).


# BASS #

The examples use the 32-bit dll version of bass. This means you must force the compiled
version to be x86.
If you create a new project don't forget to set the platfrom target to x86,
otherwhise you get an error when running your project.
development enviroment was used.

# libzplay #
The Red5Test example uses the lizplay library to play music, using the "LibRTMP.NET.Windows"
See http://libzplay.sourceforge.net/ for more info on libzplay
This is a very basic example, I used to test Red5 server and as an example
how to use the LibRTMP.NET library.




---



v1.00<br>
Initial version<br>

v1.05<br>
Fix for Red5 media server, vod should work now.<br>
Added demo app Red5Test which plays music using libzplay library (<a href='http://libzplay.sourceforge.net/'>http://libzplay.sourceforge.net/</a>)<br>
Packet sync when paused detected, fixed.<br>
Stabilization fixes.<br>
Lots of other small bug fixes.<br>