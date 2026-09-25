using System;
using System.Threading;
class FakeSmartctl
{
    static int Main(string[] args)
    {
        string device = args[args.Length - 1];
        if (device == "/dev/timeout") { Thread.Sleep(30000); return 0; }
        if (device == "/dev/fail") { Console.Error.WriteLine("Device unavailable"); return 2; }
        // Fill both redirected streams beyond typical pipe capacity to expose deadlocks.
        Console.Error.Write(new string('x', 100000));
        Console.WriteLine("{\"padding\":\"" + new string('x', 100000) + "\",\"temperature\":{\"current\":47}}");
        return 8;
    }
}
