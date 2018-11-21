using RJCP.IO.Ports;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SREnergyMeterCore {
    class Program {
        static async Task Main(string[] args) {

            var port = new SerialPortStream(Credential.SerialPortDeviceName, 115200);
            port.NewLine = "\r\n";
            port.Encoding = Encoding.ASCII;
            port.Open();

            {
                port.WriteLine("SKVER");
                Console.WriteLine(port.ReadLine());
                Console.WriteLine(port.ReadLine());

                port.WriteLine("SKINFO");
                Console.WriteLine(port.ReadLine());
                Console.WriteLine(port.ReadLine());
                Console.WriteLine(port.ReadLine());

                port.WriteLine("SKSETPWD C " + Credential.BRouteId);
                Console.WriteLine(port.ReadLine());
                Console.WriteLine(port.ReadLine());

                port.WriteLine("SKSETRBID " + Credential.BRoutePasscode);
                Console.WriteLine(port.ReadLine());
                Console.WriteLine(port.ReadLine());
            }

            var httpClient = new HttpClient();

scanretry:
            var duration = 4;
            var map = new Dictionary<string, string>();
            while(!map.ContainsKey("Channel")) {

                port.WriteLine("SKSCAN 2 FFFFFFFF " + duration++);

                var scanEnd = false;
                while (!scanEnd) {

                    var line = port.ReadLine();
                    Console.WriteLine(line);

                    if (line.StartsWith("EVENT 22")) {

                        scanEnd = true;
                    } else if (line.StartsWith("  ")) {

                        var str = line.Trim().Split(':');

                        map[str[0]] = str[1];
                    }
                }
                if(duration > 8) {

                    Console.WriteLine("Duration Exceeded");
                    goto scanretry;
                }
            }

            port.WriteLine("SKSREG S2 " + map["Channel"]);
            Console.WriteLine(port.ReadLine());
            Console.WriteLine(port.ReadLine());

            port.WriteLine("SKSREG S3 " + map["Pan ID"]);
            Console.WriteLine(port.ReadLine());
            Console.WriteLine(port.ReadLine());

            port.WriteLine("SKLL64 " + map["Addr"]);
            Console.WriteLine(port.ReadLine());
            var ipv6 = port.ReadLine().Trim();
            Console.WriteLine(ipv6);

            port.WriteLine("SKJOIN " + ipv6);
            Console.WriteLine(port.ReadLine());
            Console.WriteLine(port.ReadLine());

            var pana = false;
            while(!pana) {

                var line = port.ReadLine();
                Console.WriteLine(line);

                if(line.StartsWith("EVENT 24")) {

                    Console.WriteLine("失敗");
                    break;
                } else if(line.StartsWith("EVENT 25")) {

                    pana = true;
                }
            }

            port.ReadTimeout = 2000;
            port.WriteTimeout = 2000;

            while (pana) {

retry:
                try {

                    var echonetFrame = "";
                    echonetFrame += "\x10\x81";
                    echonetFrame += "\x00\x01";

                    echonetFrame += "\x05\xFF\x01";
                    echonetFrame += "\x02\x88\x01";
                    echonetFrame += "\x62";
                    echonetFrame += "\x01";
                    echonetFrame += "\xE7";
                    echonetFrame += "\x00";

                    var memory = new MemoryStream();
                    var writer = new BinaryWriter(memory);

                    var tt = $"SKSENDTO 1 {ipv6} 0E1A 1 {echonetFrame.Length.ToString("X4")} ";

                    writer.Write(Encoding.ASCII.GetBytes(tt));
                    writer.Write((byte)0x10);
                    writer.Write((byte)0x81);
                    writer.Write((byte)0x00);
                    writer.Write((byte)0x01);
                    writer.Write((byte)0x05);
                    writer.Write((byte)0xFF);
                    writer.Write((byte)0x01);
                    writer.Write((byte)0x02);
                    writer.Write((byte)0x88);
                    writer.Write((byte)0x01);
                    writer.Write((byte)0x62);
                    writer.Write((byte)0x01);
                    writer.Write((byte)0xE7);
                    writer.Write((byte)0x00);

                    var b = memory.ToArray();
                    port.Write(b, 0, b.Length);

                    Console.WriteLine(port.ReadLine());
                    Console.WriteLine(port.ReadLine());
                    Console.WriteLine(port.ReadLine());

                    var erxudp = port.ReadLine();
                    Console.WriteLine(erxudp);

                    if (erxudp.StartsWith("ERXUDP")) {

                        var str = erxudp.Split(' ');
                        var res = str[8];

                        var seoj = res.Substring(8, 6);
                        var esv = res.Substring(20, 2);

                        if (seoj == "028801" && esv == "72") {

                            var epc = res.Substring(24, 2);

                            if (epc == "E7") {

                                var wat = int.Parse(erxudp.Substring(erxudp.Length - 8), NumberStyles.HexNumber);

                                var request = new HttpRequestMessage(HttpMethod.Post, Credential.ApiEndpoint);
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Credential.BearerToken);

                                var pairs = new List<KeyValuePair<string, string>>();
                                pairs.Add(new KeyValuePair<string, string>("wat", wat.ToString()));
                                request.Content = new FormUrlEncodedContent(pairs);


                                await httpClient.SendAsync(request);

                                Console.WriteLine("瞬時電力計測値：" + wat + " W");
                            }
                        }
                    }
                } catch(TimeoutException) {

                    goto retry;
                }

            }
        }
    }
}
