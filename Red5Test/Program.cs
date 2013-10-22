using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Red5Test
{
    /// <summary>
    /// Example using libZPlay and Red5 Playing audio.
    /// libzplay: http://libzplay.sourceforge.net/
    /// red5: http://www.red5.org/
    /// 
    /// Because libzplay.dll is 32-bits, project is compiled as x86 to force it
    /// to use 32-bit
    /// 
    /// This is a very simple example, just to show how the LibRTMP library works.
    /// Not for real use. For a better example see LibMediaplayer 
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Red5Test");

            TestRun testRun = new TestRun();
            testRun.Run();
        }
    }
}
