//Version 0.0.1
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using BinaryFormatter = System.Runtime.Serialization.Formatters.Binary.BinaryFormatter;

public class NetSuper {
    //Configure the PayloadLength values
    readonly short MaxPayloadLength = 1480, PayloadDataLength = 8;
    //Allow Logging
    public bool fullLogging = false;
    int port;
    string IP;
    string friendlyName;
    SenderClientStream senderClient;
    ListenerClientStream listenerClient;
    List<ServerConnection> serverConnections;

    //Server
    TcpListener server;
    bool serverRunning = false;
    public bool StartServer(int port){
        try{
            serverRunning = true;
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            serverConnections = new List<ServerConnection>();
            AcceptTCPClientsAsync();
            Log($"Server Started on port {port}", false, ConsoleColor.DarkGreen);
            return true;
        }
        catch(Exception e){
            serverRunning = false;
            Log($"Error starting server: {e.Message}", true);
            return false;
        }
    }
    public void StopServer(){
        serverRunning = false;
    }
    async Task AcceptTCPClientsAsync() {
        //while the server is listening, accept clients
        while (serverRunning) {
            // Accept a client connection
            HandleProspectiveClientAsync(await server.AcceptTcpClientAsync());
            if (!serverRunning){ break; } //if the server is no longer listening, break the loop
        }
        server.Stop();
        Log("Server Process Ended");
    }
    async Task HandleProspectiveClientAsync(TcpClient c) {
        //create the buffers
        DateTime start = DateTime.Now;
        byte[] buffer, lenBuffer, bBuffer;
        try {
            NetworkStream stream = c.GetStream();
            // Handle client requests
            while (c.Connected && start.AddSeconds(5) > DateTime.Now){
                lenBuffer = new byte[4]; //create the length buffer
                await stream.ReadAsync(lenBuffer, 0, 4); //read the length buffer
                string lenString = Encoding.ASCII.GetString(lenBuffer, 0, 4); //convert the length buffer to a string
                int len = 0; int.TryParse(lenString, out len);
                //netSys.Log($"Recived Length: {lenString}", true);
                if(lenString == ""){ len = len / 0;}
                else if(len == 0){ continue; }
                int totalBytes = len; //get the total bytes from the length buffer
                totalBytes += PayloadDataLength; //add the payload data length
                buffer = new byte[totalBytes]; bBuffer = new byte[totalBytes - 4]; //create the buffer and the data buffer
                await stream.ReadAsync(bBuffer, 0, bBuffer.Length); //read the data buffer
                Array.Copy(lenBuffer, 0, buffer, 0, lenBuffer.Length); //make buffer = lenBuffer + bBuffer
                Array.Copy(bBuffer, 0, buffer, 4, bBuffer.Length); //make buffer = lenBuffer + bBuffer
                if (totalBytes == 0) { Log("totalbytes0"); break; } //might be redundant
                else{
                    // get the first 8 bytes as a string
                    string info = Encoding.ASCII.GetString(buffer, 0, 8);
                    //id is bytes 4-6
                    int payloadId = int.Parse(info.Substring(4, 3));
                    //terminator is byte 7
                    int terminator = int.Parse(info.Substring(7, 1));
                    //Log($"PayloadId: {payloadId}, Terminator: {terminator}, from info: {info}");
                    //remove the first 8 bytes to get the payload
                    byte[] payload = new byte[buffer.Length - 8];
                    Array.Copy(buffer, 8, payload, 0, buffer.Length - 8);
                    string friendlyName = (string)ByteArrayToObject(payload);
                    if(payloadId == 1){
                        await DataOKAsync(stream);
                        //Make us a listener
                        //if we get here, there is no server connection with the same name, so we need to make one
                        ServerConnection serverConnection = new ServerConnection(friendlyName);
                        serverConnection.listener = new ListenerClientStream(c, stream, this, friendlyName);
                        serverConnections.Add(serverConnection);
                        Log($"Added listener to new server connection: {friendlyName}");
                        return;
                    }
                    else if(payloadId == 2){
                        await DataOKAsync(stream);
                        //make us a sender
                        //check if there is a server connection with the same name
                        foreach(ServerConnection sc in serverConnections){
                            if(sc.friendlyName == friendlyName){
                                //add us as a sender
                                sc.sender = new SenderClientStream(c, stream);
                                Log($"Added sender to {friendlyName}");
                                return;
                            }
                        }
                        //if we get here, there is no server connection with the same name, so we need to make one
                        Log($"Tried To Add Sender with no client", true);
                        c.Close();
                        return;
                    }
                    else{
                        break;
                    }
                }
            }
        }
        catch (Exception ex) { 
            Log($"Prospective Client Error: {ex.Message}", true);
        }
        Log("Prospective Client Process Ended");
        c.Close();
        Log("Client Rejected");
    }
    async Task EndListenersAsync(string friendlyName){
        foreach(ServerConnection sc in serverConnections){
            if(sc.friendlyName == friendlyName){
                sc.listener.plsClose = true;
                sc.listener.stop = true;
                await Task.Delay(20);
                sc.listener.client.Close();
                return;
            }
        }
    }
    public async Task<bool> SendToClient(object data, int payloadId, string friendlyName){
        try{
            foreach(ServerConnection sc in serverConnections){
                if(sc.friendlyName == friendlyName){
                    try{
                        //check how many senders are available, if there are none, return false, and if there more than 1 run syncronously
                        if(sc.sender == null){return false;}
                        else{
                            return await Send(data, payloadId, sc.sender.stream);
                        }
                    }
                    catch(Exception e){
                        Log($"Error sending data: {e.Message}", true);
                        return false;
                    }
                }
            }
            return false;
        }
        catch(Exception e){
            Log($"Error sending data: {e.Message}", true);
            return false;
        }
    }
    //Client
    public async Task<bool> ConnectToServer(string ip, int port, string friendlyName){
        try{
            TcpClient client = new TcpClient();
            await client.ConnectAsync(ip, port);
            NetworkStream stream = client.GetStream();
            //send the friendly name
            await Send(friendlyName, 1, stream);
            await stream.FlushAsync();
            Log("Stream Flushed");
            //add the client to the list
            senderClient = new SenderClientStream(client, stream);
            Log("Connected to server", false, ConsoleColor.DarkGreen);
            this.IP = ip;
            this.port = port;
            this.friendlyName = friendlyName;
            TcpClient client2 = new TcpClient();
            await client2.ConnectAsync(IP, port);
            NetworkStream stream2 = client2.GetStream();
            //send the friendly name
            await Send(friendlyName, 2, stream2);
            await stream2.FlushAsync();
            //add the client to the list
            listenerClient = new ListenerClientStream(client2, stream2, this);
            return true;
        }
        catch(Exception e){
            Log($"Error connecting to server: {e.Message}", true);
            return false;
        }
    }
    public async Task<bool> SendData(object data, int payloadId){
        try{
            //check how many senders are available, if there are none, return false, and if there more than 1 run syncronously
            if(senderClient == null){return false;}
            else{
                return await Send(data, payloadId, senderClient.stream);
            }
        }
        catch(Exception e){
            Log($"Error sending data: {e.Message}", true);
            return false;
        }
    }
    public async Task StopClient(){
        Log("Requesting senders to close");
        await SendData(friendlyName, -3);
        Log("Requesting listeners to close");
        listenerClient.plsClose = true;
        listenerClient.stop = true;
        listenerClient.client.Close();
        Log("Client Stopped");
    }

    public delegate void ProcessDataEventHandler(RecivedData data);
    public event ProcessDataEventHandler onDataRecived;
    public struct RecivedData{
        public object dataObj;
        public int payloadId;
        public bool isServer;
        public string connetionName;
    }
    async void ProcessData(byte[] data, int payloadId, bool isServer, string friendlyName)
    {
        // Your data processing logic here
        object dataObj = ByteArrayToObject(data);
        //Log($"ProcessData ID:({payloadId}) {(string)dataObj}");
        if(isServer && payloadId < 0){
            if(payloadId == -1){
                await EndListenersAsync((string)dataObj); //destroy all listeners with the same name
            }
            else if(payloadId == -2){
                //server.SetClientAsListen((string)dataObj, ID); //ping the server
            }
            return;
        }
        // Notify listeners
        RecivedData r = new RecivedData
        {
            dataObj = dataObj,
            payloadId = payloadId,
            isServer = isServer,
            connetionName = friendlyName
        };
        onDataRecived?.Invoke(r);
    }
    class ServerConnection{
        public ListenerClientStream listener;
        public SenderClientStream sender;
        public string friendlyName;
        public ServerConnection(string friendlyName){
            this.friendlyName = friendlyName;
        }
    }
    class SenderClientStream{
        public TcpClient client;
        public NetworkStream stream;
        public bool inUse = false;
        public int sent = 0;
        public SenderClientStream(TcpClient client, NetworkStream stream){
            this.client = client;
            this.stream = stream;
        }
    }
    class ListenerClientStream{
        public TcpClient client;
        public NetworkStream stream;
        //Task listenerTask;
        public bool stop = false, plsClose = false;
        NetSuper netSuper;
        string friendlyName;
        public ListenerClientStream(TcpClient client, NetworkStream stream, NetSuper netSuper, string friendlyName = null){
            this.client = client;
            this.stream = stream;
            this.netSuper = netSuper;
            this.friendlyName = friendlyName;
            HandleTCPClientAsync();
        }
        async Task HandleTCPClientAsync() {
            //create the buffers
            netSuper.Log("Client Process Started");
            if(stream == null){ stream = client.GetStream(); }
            byte[] fBuffer = null, buffer, lenBuffer, bBuffer;
            try {
                // Handle client requests
                while (client.Connected){
                    if(stop){ netSuper.Log("TCP Handeler Terminated"); return; }
                    lenBuffer = new byte[4]; //create the length buffer
                    //await stream.FlushAsync();
                    await stream.ReadAsync(lenBuffer, 0, 4); //read the length buffer
                    string lenString = Encoding.ASCII.GetString(lenBuffer, 0, 4); //convert the length buffer to a string
                    int len = 0; int.TryParse(lenString, out len);
                    //netSuper.Log($"Recived Length: {lenString}", true);
                    if(lenString == ""){ len = len / 0;}
                    else if(len == 0){ continue; }
                    int totalBytes = len; //get the total bytes from the length buffer
                    totalBytes += netSuper.PayloadDataLength; //add the payload data length
                    buffer = new byte[totalBytes]; bBuffer = new byte[totalBytes - 4]; //create the buffer and the data buffer
                    await stream.ReadAsync(bBuffer, 0, bBuffer.Length); //read the data buffer
                    Array.Copy(lenBuffer, 0, buffer, 0, lenBuffer.Length); //make buffer = lenBuffer + bBuffer
                    Array.Copy(bBuffer, 0, buffer, 4, bBuffer.Length); //make buffer = lenBuffer + bBuffer
                    if (totalBytes == 0) { netSuper.Log("totalbytes0"); break; } //might be redundant
                    netSuper.Log($"Recived Data From Client: {friendlyName}");
                    fBuffer = netSuper.Recive(buffer, true, friendlyName, fBuffer); //get just the data from the buffer
                    //if we are expecting a response, confirm the data was recieved
                    await netSuper.DataOKAsync(stream);
                }
            }
            catch (Exception ex) {
                if(!plsClose){ netSuper.Log($"TCP Handler Error: {ex.Message}", true, ConsoleColor.Red); }
            }
            client.Dispose();
            netSuper.Log("Client Process Ended", false, ConsoleColor.DarkYellow);
        }
    }
    async Task<bool> Send(object data, int payloadId, NetworkStream stream)
    {
        // Convert the object to bytes
        byte[] dataBytes = ObjectToByteArray(data);
        //Log($"Sending {dataBytes.Length} bytes of data");
        // Split the data into multiple payloads if necessary
        List<byte[]> payloads = SplitDataIntoPayloads(dataBytes);
        // Send each payload
        for (int i = 0; i < payloads.Count; i++) {
            // Determine if this is the last payload
            bool isLastPayload = i == payloads.Count - 1;
            // Determine the terminator value
            char terminator = isLastPayload ? '1' : '2';
            // Determine the length of the payload
            int payloadLength = payloads[i].Length + PayloadDataLength; // lengthBytes + payloadId + payloadData + terminator
            string payloadIDString = payloadId.ToString("000");
            if(payloadIDString.Length == 4){ payloadIDString = $"-0{payloadIDString[3]}"; }
            string info = $"{payloads[i].Length:0000}{payloadIDString}{terminator}";
            byte[] infoBytes = Encoding.ASCII.GetBytes(info);
            // Create the complete payload
            byte[] completePayload = new byte[payloadLength];

            Array.Copy(infoBytes, 0, completePayload, 0, infoBytes.Length);
            Log($"completePayload: {completePayload.Length}");
            //Log($"payloadLength: {payloadLength}, info: {info}");
            Array.Copy(payloads[i], 0, completePayload, 8, payloads[i].Length);
            // Send the payload
            bool b = await SendPayloadAsync(completePayload, stream);
            if(!b){ Log("Payload Send Error", true);  return false; }
            Log($"Payload {i + 1}/{payloads.Count} sent, waiting for OK.");
            await WaitForDataOKAsync(stream);
            Log("Data OK Recived");
        }
        return true;
    }
    byte[] Recive(byte[] toProcess, bool isServer, string friendlyName, byte[] previous = null){
        try{
            //if previous is null, then this is the first payload
            // get the first 8 bytes as a string
            string info = Encoding.ASCII.GetString(toProcess, 0, 8);
            //id is bytes 4-6
            int payloadId = int.Parse(info.Substring(4, 3));
            //terminator is byte 7
            int terminator = int.Parse(info.Substring(7, 1));
            //Log($"PayloadId: {payloadId}, Terminator: {terminator}, from info: {info}");
            //remove the first 8 bytes to get the payload
            byte[] payload = new byte[toProcess.Length - 8];
            Array.Copy(toProcess, 8, payload, 0, toProcess.Length - 8);

            if(previous != null){
                byte[] combined = new byte[previous.Length + payload.Length];
                Array.Copy(previous, 0, combined, 0, previous.Length);
                Array.Copy(payload, 0, combined, previous.Length, payload.Length);
                payload = combined;
            }
            
            if (terminator == 1){
                //this is the last payload
                //process the data
                ProcessData(payload, payloadId, isServer, friendlyName);
                return null;
            }
            else if (terminator == 2){ return payload; } //this is not the last payload
            else{
                //this is not a valid payload
                Log("Invalid payload", true); return null;   
            }
        }
        catch(Exception e){
            Log($"Error reciving data: {e.Message}", true);
            return null;
        }
    }
    async Task WaitForDataOKAsync(NetworkStream stream)
    {
        byte[] responseBuffer = new byte[2];
        string response = "";
        while (!response.Contains("ok")) {
            try
            {
                int bytesRead = await stream.ReadAsync(responseBuffer, 0, 2);
                response += Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);
            }
            catch(Exception ex){ Log($"Failed to get OK: {ex.Message}", true); return; }
        }
    }
    async Task DataOKAsync(NetworkStream stream)
    {
        byte[] buffer = Encoding.ASCII.GetBytes("ok");
        await stream.WriteAsync(buffer, 0, buffer.Length);
    }
    async Task<bool> SendPayloadAsync(byte[] payload, NetworkStream stream)
    {
        try{ await stream.WriteAsync(payload, 0, payload.Length); }
        catch(Exception e){ Log($"Error sending payload: {e.Message}", true); return false; }
        return true;
    }
    List<byte[]> SplitDataIntoPayloads(byte[] data)
    {
        List<byte[]> payloads = new List<byte[]>();
        int remainingLength = data.Length, startIndex = 0;
        while (remainingLength > 0) {
            int currentPayloadLength = Math.Min(remainingLength, MaxPayloadLength - PayloadDataLength);
            byte[] payload = new byte[currentPayloadLength];
            Array.Copy(data, startIndex, payload, 0, currentPayloadLength);
            payloads.Add(payload);
            remainingLength -= currentPayloadLength;
            startIndex += currentPayloadLength;
        }
        return payloads;
    }
    byte[] ObjectToByteArray(object b){
        try{
            if(b == null){ return null; }
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            { bf.Serialize(ms, b); return ms.ToArray(); }
        }
        catch(Exception e){
            Log($"Error converting object to byte array: {e.Message}", true);
            return null;
        }
    }
    //Byte array to object
    object ByteArrayToObject(byte[] arrBytes){
        try{
            using (MemoryStream memStream = new MemoryStream()) {
                BinaryFormatter binForm = new BinaryFormatter();
                memStream.Write(arrBytes, 0, arrBytes.Length);
                memStream.Seek(0, SeekOrigin.Begin);
                return binForm.Deserialize(memStream);
            }
        }
        catch(Exception e){
            Log($"Error converting byte array to object: {e.Message}", true);
            return null;
        }
        
    }
    void Log(string message, bool error = false, ConsoleColor BG = ConsoleColor.Black, ConsoleColor FG = ConsoleColor.White) {
        if(fullLogging || error){
            #if UNITY_EDITOR
                UnityEngine.Debug.Log($"{message}");
            #else
                Console.ForegroundColor = FG; Console.BackgroundColor = BG;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                Console.ResetColor();
            #endif
        }
    }
}