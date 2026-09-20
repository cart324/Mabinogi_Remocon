using System;
using System.Text;
class FakeCLI {
  static int Main(string[] args){if(args.Length>0&&args[0]=="reject"){Console.Write("{\"status\":\"rejected\",\"body\":{\"error\":\"blocked\"}}");return 0;}string body=args.Length>1&&args[1].StartsWith("base64:")?Encoding.UTF8.GetString(Convert.FromBase64String(args[1].Substring(7))):"{}";if(args.Length>0&&args[0]=="hang"){System.IO.File.WriteAllText(body,System.Diagnostics.Process.GetCurrentProcess().Id.ToString());System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);}
Console.OutputEncoding=Encoding.UTF8;Console.Write(body);return 0;}
}
