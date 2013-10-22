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
using System.IO;
using System.Text;

namespace CDR.LibRTMP
{
    public enum LibRTMPLogLevel
    {
        None = 0, // Off
        Error, // Highest
        Warning,
        Info,
        Debug,
        Trace      // Lowest
    }

    /// <summary>
    /// Minimal log system
    /// 
    /// Error  - Other runtime errors or unexpected conditions. Expect these to be 
    ///          immediately visible on a status console. 
    /// warn   - Use of deprecated APIs, poor use of API, 'almost' errors, other 
    ///          runtime situations that are undesirable or unexpected, but not 
    ///          necessarily "wrong". Expect these to be immediately visible on a 
    ///          status console. 
    /// info   - Interesting runtime events (startup/shutdown). Expect these to be 
    ///          immediately visible on a console, so be conservative and keep to 
    ///          a minimum. 
    /// debug  - detailed information on the flow through the system. Expect these 
    ///          to be written to logs only. 
    /// trace  - more detailed information. Expect these to be written to logs only.
    /// </summary>
    public static class LibRTMPLogger
    {
        private static LibRTMPLogLevel activeLogLevel = LibRTMPLogLevel.None;

        private static bool logToFile = false;
        private static bool logToOutput = false;
        private static object lockLogFile = new object();
        private static string LogFilename = "Log.log";


        public static LibRTMPLogLevel ActiveLogLevel
        {
            get
            {
                return activeLogLevel;
            }
            set
            {
                activeLogLevel = value;
            }
        }

        public static bool LogToFile
        {
            get
            {
                return logToFile;
            }
            set
            {
                logToFile = value;
            }
        }

        public static bool LogToOutput
        {
            get
            {
                return logToOutput;
            }
            set
            {
                logToOutput = value;
            }
        }

        public static void Log(LibRTMPLogLevel logLevel, string message)
        {
#if !__ANDROID__
            if (activeLogLevel != LibRTMPLogLevel.None && Convert.ToInt32(activeLogLevel) >= Convert.ToInt32(logLevel))
            {
                string line = string.Format("[{0}]: {1}", logLevel, message);
                if (logToOutput)
                {
                    Console.WriteLine(line);
                }
#if !__IOS__
                if (LogToFile)
                {
                    lock (lockLogFile)
                    {
                        if (!File.Exists(LogFilename))
                        {
                            // Create a file to write to. 
                            using (StreamWriter sw = File.CreateText(LogFilename))
                            {
                                sw.WriteLine(line);
                            } //using
                        }
                        else
                        {
                            using (StreamWriter sw = File.AppendText(LogFilename))
                            {
                                sw.WriteLine(line);
                            } //using
                        }
                    } //lock
                }
#endif
            }
#endif
        }

        public static void LogError(Exception e)
        {
#if !__IOS__ && !__ANDROID__
            if (activeLogLevel != LibRTMPLogLevel.None && Convert.ToInt32(activeLogLevel) >= Convert.ToInt32(LibRTMPLogLevel.Error))
            {
                Log(LibRTMPLogLevel.Error, e.Message);
            }
#endif
        }

        public static void LogHex(LibRTMPLogLevel logLevel, byte[] array, int offset, int count)
        {
#if !__IOS__ && !__ANDROID__
            if (activeLogLevel != LibRTMPLogLevel.None && Convert.ToInt32(activeLogLevel) >= Convert.ToInt32(logLevel))
            {
                string result = string.Empty;
                for (int i = offset; i < offset + count; i++)
                {
                    result += array[i].ToString("x2") + " ";
                    if (result.Length >= 47)
                    {
                        Log(logLevel, result.Trim());
                        result = string.Empty;
                    }
                } //for
                if (result.Length > 0)
                {
                    Log(logLevel, result.Trim());
                }
            }
#endif
        }
    }
}
