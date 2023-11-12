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
    readonly short MaxPayloadLength = 8192, PayloadDataLength = 8;
    //Allow Logging
    public bool fullLogging = false;
    string friendlyName;
    bool gotPing = false;
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
    public delegate void ServerEventHandler(ServerData data);
    public event ServerEventHandler onServerUpdate;
    public struct ServerData{
        public bool connected, unexpected;
        public string connectionName;
    }
    async Task HandleProspectiveClientAsync(TcpClient c) {
        //create the buffers
        DateTime start = DateTime.Now;
        byte[] infoBuffer = new byte[8], dataBuffer;
        try {
            NetworkStream stream = c.GetStream();
            // Handle client requests
            while (c.Connected && start.AddSeconds(5) > DateTime.Now){
                await stream.ReadAsync(infoBuffer, 0, 8); //read the info buffer
                string info = Encoding.ASCII.GetString(infoBuffer, 0, 8); //convert the info buffer to a string
                int size = int.Parse(info.Substring(0, 4)); //get the size of the data buffer
                dataBuffer = new byte[size]; //create the data buffer
                await stream.ReadAsync(dataBuffer, 0, size); //read the data buffer using the first 4 digits of info as the length
                int payloadId = int.Parse(info.Substring(4, 4));
                string friendlyName = (string)ByteArrayToObject(dataBuffer);
                if(payloadId == 1){
                    
                    //Make us a listener
                    //if we get here, there is no server connection with the same name, so we need to make one
                    ServerConnection serverConnection = new ServerConnection(friendlyName)
                    { listener = new ListenerClientStream(c, stream, this, friendlyName) };
                    serverConnections.Add(serverConnection);
                    Log($"Added listener to new server connection: {friendlyName}");
                    return;
                }
                else if(payloadId == 2){
                    //make us a sender
                    //check if there is a server connection with the same name
                    foreach(ServerConnection sc in serverConnections){
                        if(sc.friendlyName == friendlyName){
                            //add us as a sender
                            sc.sender = new SenderClientStream(c, stream);
                            Log($"Added sender to {friendlyName}");
                            onServerUpdate?.Invoke(new ServerData{ connected = true, connectionName = friendlyName });
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
                sc.listener.stop = true;
                //await Task.Delay(20);
                sc.listener.client.Close();
                Log($"Closed listener for {friendlyName}");
                onServerUpdate?.Invoke(new ServerData{ connected = false, connectionName = friendlyName });
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
    public async Task<bool> ConnectToServer(string IP, int port, string friendlyName){
        try{
            TcpClient client = new TcpClient();
            await client.ConnectAsync(IP, port);
            NetworkStream stream = client.GetStream();
            //send the friendly name
            await Send(friendlyName, 1, stream);
            await stream.FlushAsync();
            Log("Stream Flushed");
            //add the client to the list
            senderClient = new SenderClientStream(client, stream);
            Log("Connected to server", false, ConsoleColor.DarkGreen);
            this.friendlyName = friendlyName;
            await Task.Delay(100);
            TcpClient client2 = new TcpClient();
            await client2.ConnectAsync(IP, port);
            NetworkStream stream2 = client2.GetStream();
            //send the friendly name
            await Send(friendlyName, 2, stream2);
            await stream2.FlushAsync();
            //add the client to the list
            listenerClient = new ListenerClientStream(client2, stream2, this);
            onClientStatusChange?.Invoke(true);
            return true;
        }
        catch(Exception e){
            Log($"Error connecting to server: {e.Message}", true);
            return false;
        }
    }
    public async Task<int> GetPing(){
        gotPing = false;
        DateTime start = DateTime.Now;
        await SendData(friendlyName, -2);
        while(!gotPing){ await Task.Delay(1); }
        return (int)(DateTime.Now - start).TotalMilliseconds;
    }
    public async Task<bool> SendData(object data, int payloadId){
        try{
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
        Log("Requesting server listeners to close");
        await SendData(friendlyName, -1);
        Log("Requesting our listeners to close");
        listenerClient.stop = true;
        listenerClient.client.Close();
        Log("Requesting our senders to close");
        senderClient.client.Close();
        Log("Client Stopped");
        onClientStatusChange?.Invoke(false);
    }
    public delegate void ClientEventHandler(bool connected);
    public event ClientEventHandler onClientStatusChange;

    public delegate void ProcessDataEventHandler(RecivedData data);
    public event ProcessDataEventHandler onDataRecived;
    public struct RecivedData{
        public object dataObj;
        public int payloadId;
        public string connetionName;
    }
    async void ProcessData(byte[] data, int payloadId, string friendlyName)
    {
        // Your data processing logic here
        object dataObj = ByteArrayToObject(data);
        //Log($"ProcessData ID:({payloadId}) {(string)dataObj}");
        if(payloadId < 0){
            if(payloadId == -1){
                await EndListenersAsync((string)dataObj); //destroy all listeners with the same name
            }
            else if(payloadId == -2){
                 //ping the server
                 await SendToClient(friendlyName, -3, friendlyName);
            }
            else if(payloadId == -3){
                 //ping the server
                gotPing = true;
            }
            return;
        }
        // Notify listeners
        RecivedData r = new RecivedData
        {
            dataObj = dataObj,
            payloadId = payloadId,
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
        public bool stop = false;
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
            byte[] dataBuffer, infoBuffer = new byte[8];
            try {
                // Handle client requests
                while (client.Connected){
                    await stream.ReadAsync(infoBuffer, 0, 8); //read the info buffer
                    string info = Encoding.ASCII.GetString(infoBuffer, 0, 8); //convert the info buffer to a string
                    int size = int.Parse(info.Substring(0, 4)); //get the size of the data buffer
                    dataBuffer = new byte[size]; //create the data buffer
                    await stream.ReadAsync(dataBuffer, 0, size); //read the data buffer using the first 4 digits of info as the length
                    if(size == 0){ continue; }
                    int payloadId = int.Parse(info.Substring(4, 4));
                    netSuper.Log($"Recived Data From Client: {friendlyName} ID: {payloadId}");
                    netSuper.ProcessData(dataBuffer, int.Parse(info.Substring(4, 4)), friendlyName); //get just the data from the buffer
                }
            }
            catch (Exception ex) {
                if(!stop){
                    netSuper.Log($"TCP Handler Error: {ex.Message}", true, ConsoleColor.Red);
                }
                else{
                    netSuper.Log("TCP Handeler Terminated");
                }
            }
            client.Dispose();
            netSuper.Log("Client Process Ended", false, ConsoleColor.DarkYellow);
        }
    }
    async Task<bool> Send(object data, int payloadId, NetworkStream stream)
    {
        // Convert the object to bytes
        byte[] dataBytes = ObjectToByteArray(data);
        if(dataBytes.Length > MaxPayloadLength - PayloadDataLength){ Log($"Data too large: {dataBytes.Length} bytes", true); return false; }
        int payloadLength = dataBytes.Length + PayloadDataLength; // lengthBytes + payloadId + payloadData + terminator
        string payloadIDString = payloadId.ToString("0000");
        if(payloadIDString.Length == 5){ payloadIDString = $"-00{payloadIDString[4]}"; }
        string info = $"{dataBytes.Length:0000}{payloadIDString}";
        byte[] infoBytes = Encoding.ASCII.GetBytes(info);
        // Create the complete payload
        byte[] completePayload = new byte[payloadLength];
        //write the info to the first 8 bytes
        Array.Copy(infoBytes, 0, completePayload, 0, 8);
        //write the data to the rest of the payload
        Array.Copy(dataBytes, 0, completePayload, 8, dataBytes.Length);
        // Send the payload
        bool b = await SendPayloadAsync(completePayload, stream);
        if(!b){ Log("Payload Send Error", true);  return false; }
        return true;
    }
    async Task<bool> SendPayloadAsync(byte[] payload, NetworkStream stream)
    {
        try{
            await stream.WriteAsync(payload, 0, payload.Length); 
            while(stream.DataAvailable){ await Task.Delay(1); } //wait until the stream is empty
        }
        catch(Exception e){ Log($"Error sending payload: {e.Message}", true); return false; }
        return true;
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