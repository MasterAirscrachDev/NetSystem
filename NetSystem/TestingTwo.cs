using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace NetSystem
{
    class TestingTwo
    {
        NetSuper netSys = new NetSuper();
        int lastID = 0, scaleIndex = 0;
        public async Task DoTests()
        {
            //ask s/c for server or client
            //NetSys netSys = new NetSys();
            Console.WriteLine("Server or Client? (s/c)");
            netSys.fullLogging = true;
            ListenForData(netSys);
            bool useServer = false;
            string input = "";
            if (useServer)
            {
                input = Console.ReadLine();
            }  
            //check if this app is already running
            bool running = System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Count() > 1;
            
            if (input == "s" || (!running && !useServer))
            {
                //SendListenTicks();
                Console.WriteLine("Starting server");
                netSys.StartServer(12347);
                ListenForServerEvents(netSys);
            }
            else if (input == "c" || (running && !useServer))
            {
                //start client
                await netSys.ConnectToServer(IP.GetIP(!useServer), 12347, "My Friendly Connection");
                //await netSys.AddListenersAsync(9);
                //await netSys.AddSendersAsync(9);

                //await Task.Delay(3000);
                //await client.SendData("beans", 1);
                //await client.SendData("beans223434", 1);
                //await Task.Delay(1000);
                //await client.SendData("beans332434234243243", 1);
                //await client.SendData("beans32434324234342234342425325443523153153562435246453462423534514246362", 1);
                //await Task.Delay(1000);
                //await client.SendData("What the fuck did you just fucking say about me, you little bitch? I'll have you know I graduated top of my class in the Navy Seals, and I've been involved in numerous secret raids on Al-Quaeda, and I have over 300 confirmed kills. I am trained in gorilla warfare and I'm the top sniper in the entire US armed forces. You are nothing to me but just another target. I will wipe you the fuck out with precision the likes of which has never been seen before on this Earth, mark my fucking words.", 1);
                //await client.SendData("What the fuck did you just fucking say about me, you little bitch? I'll have you know I graduated top of my class in the Navy Seals, and I've been involved in numerous secret raids on Al-Quaeda, and I have over 300 confirmed kills. I am trained in gorilla warfare and I'm the top sniper in the entire US armed forces. You are nothing to me but just another target. I will wipe you the fuck out with precision the likes of which has never been seen before on this Earth, mark my fucking words. You think you can get away with saying that shit to me over the Internet? Think again, fucker. As we speak I am contacting my secret network of spies across the USA and your IP is being traced right now so you better prepare for the storm, maggot. The storm that wipes out the pathetic little thing you call your life. You're fucking dead, kid. I can be anywhere, anytime, and I can kill you in over seven hundred ways, and that's just with my bare hands. Not only am I extensively trained in unarmed combat, but I have access to the entire arsenal of the United States Marine Corps and I will use it to its full extent to wipe your miserable ass off the face of the continent, you little shit. If only you could have known what unholy retribution your little clever comment was about to bring down upon you, maybe you would have held your fucking tongue. But you couldn't, you didn't, and now you're paying the price, you goddamn idiot. I will shit fury all over you and you will drown in it. You're fucking dead, kiddo.", 1);
                bool active = true;
                while (active)
                {
                    Console.WriteLine($"STOP, RETURN, UDP, SCALETEST, SPEEDUDP, DATA, data");
                    input = Console.ReadLine();
                    if (input == "STOP") { active = false; }
                    else if (input.StartsWith("RETURN")) {
                        await netSys.SendData(input, 2);
                    }
                    else if (input.StartsWith("UDP"))
                    {
                        //bool b = await netSys.client.SendInstant(input, 4);
                        //if (!b) { Console.WriteLine("FAILED TO SEND"); }
                    }
                    else if (input.StartsWith("SPEEDUDP"))
                    {
                        //await TimedTestUDP(netSys.client);
                    }
                    else if (input.StartsWith("PING"))
                    {
                        int i = await netSys.GetPing();
                        Console.WriteLine($"Ping: {i}");
                    }
                    else if (input.StartsWith("SCALETEST"))
                    {
                        await MessagingTest();
                    }
                    else if (input.StartsWith("DATA"))
                    {
                        //TestNetworkData data = GetRandomNetData();
                        //PrintNetData(data);
                        //await netSys.client.SendData(data, 10);
                    }
                    else { await netSys.SendData(input, 1); }
                }
                await netSys.StopClient();
                //await netSys.client.Disconnect();
            }
            else
            { Console.WriteLine("Invalid input"); }
        }
        async Task MessagingTest(){
            Console.WriteLine("Starting test 1s");
            int min = 2990, max = 3000;
            int am = await MultiTest(min, max, 1);
            Console.WriteLine($"Sent {am} messages in 1 second");
            await Task.Delay(5000);
            Console.WriteLine("Starting test 5s");
            am = await MultiTest(min, max, 5);
            Console.WriteLine($"Sent {am} messages in 5 seconds, mp/s: {am/5f}");
            await Task.Delay(5000);
            Console.WriteLine("Starting test 10s");
            am = await MultiTest(min, max, 10);
            Console.WriteLine($"Sent {am} messages in 10 seconds, mp/s: {am/10f}");
        }
        async Task<int>MultiTest( int min, int max, int seconds)
        {
            //get the start time
            DateTime start = DateTime.Now;
            int sends = 0;
            //while its been less than 10 seconds from start
            Random r = new Random();
            string s = getRandomString(r.Next(min, max));
            while (DateTime.Now < start.AddSeconds(seconds))
            {
                await netSys.SendData(s, 7);
                sends++;
            }
            await netSys.SendData($"Sent {sends} messages over {seconds} seconds", 8);
            return sends;
        }
        string getRandomString(int length){
            string random = "";
            Random r = new Random();
            for(int i = 0; i < length; i++){
                char randomLetter = (char)r.Next('a', 'z' + 1);
                random += randomLetter;
            }
            return random;
        }

        void ListenForData(NetSuper e)
        {
            e.onDataRecived += async (data) =>
            {
                if(data.payloadId == 1){
                    Console.WriteLine($"Recived: {(string)data.dataObj}");
                }
                else if(data.payloadId == 2){
                    Console.WriteLine($"Recived and returning: {(string)data.dataObj}");
                    await netSys.SendToClient(data.dataObj, 3, data.connetionName);
                }
                else if(data.payloadId == 3){
                    Console.WriteLine($"Recived from server: {(string)data.dataObj}");
                }
                else if(data.payloadId == 4){
                    Console.WriteLine($"Recived UDP: {(string)data.dataObj}");
                }
                else if(data.payloadId == 5){
                    Console.WriteLine($"Listened: {(string)data.dataObj}");
                }
                else if(data.payloadId == 6){
                    string s = (string)data.dataObj;
                    //get the first 10 chars
                    s = s.Substring(0, 10);
                    //split on the space
                    string[] split = s.Split(' ');
                    //get the number
                    int num = int.Parse(split[0]);
                    //compare to last id
                    if(num == lastID + 1){
                        Console.WriteLine($"Good: {num} == {lastID + 1}");
                    }
                    else if(num < lastID){
                        Console.WriteLine($"Late: {num} < {lastID}");
                    }
                    else if(num > lastID){
                        Console.WriteLine($"Early: {num} > {lastID}");
                    }
                    lastID = num;
                }
                else if (data.payloadId == 7)
                {
                    string s = (string)data.dataObj;
                    scaleIndex++;
                }
                else if (data.payloadId == 8)
                {
                    string s = (string)data.dataObj;
                    Console.WriteLine($"{s} scaleIndex: {scaleIndex}");
                    scaleIndex = 0;
                }
                else if (data.payloadId == 9)
                {
                    string s = (string)data.dataObj;
                    string[] split = s.Split(' ');
                    Console.WriteLine($"Recived ({data.payloadId}): {split[0]}");
                }
                else if (data.payloadId == 10)
                {
                    TestNetworkData d = (TestNetworkData)data.dataObj;
                    //PrintNetData(d);
                }
                else
                {
                    Console.WriteLine($"Recived ({data.payloadId}): {(string)data.dataObj}");
                }
            };
        }

        void ListenForServerEvents(NetSuper e){
            e.onServerUpdate += (client) =>
            {
                if(client.connected){
                    Console.WriteLine($"Client connected: {client.connectionName}");
                }
                else{
                    Console.WriteLine($"Client disconnected: {client.connectionName}");
                }
            };
        }
    }
}
