//Version 0.0.5

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using BinaryFormatter = System.Runtime.Serialization.Formatters.Binary.BinaryFormatter;

public class NetSys
{
    //Configure the PayloadLength values
    readonly short MaxPayloadLength = 1480, PayloadDataLength = 8;
    //Allow Logging
    bool allowLogging = false;
    //Server and Client Variables
    public Server server;
    public Client client;
    //Functions to start the server and client
    /// <summary>
    /// This Starts A Server
    /// </summary>
    public void NewServer(int port = 12345, bool UseUDP = false){
        server = new Server();
        server.SetNetSys(this);
        server.StartServer(port, UseUDP);
    }
    /// <summary>
    /// This Starts A Client
    /// </summary>
    public async Task NewClient(string ipAddress, string friendlyName, int port = 12345){
        client = new Client();
        client.SetNetSys(this);
        await client.Connect(ipAddress, friendlyName, port);
    }
    /// <summary>
    ///This is the server, it will handle all the clients and needs to be port forwarded
    /// </summary>
    public class Server {
        NetSys netSys;
        TcpListener TCPserver;
        UdpClient UDPServer;
        List<ClientConnection> clients = new List<ClientConnection>();
        List<string> safeToDrop = new List<string>();
        bool isListening = true;
        public void SetNetSys(NetSys netSys){ this.netSys = netSys; }
        //This is the main function, it will start the server and accept clients
        public void StartServer(int port = 12345, bool UseUDP = false) {
            CreateTCPServer(port);
            AcceptTCPClientsAsync();
            if(UseUDP){
                CreateUDPServer(port);
                AcceptUPDDataAsync();
            }
        }
        //This Starts the TCP Server
        void CreateTCPServer(int port) {
            TCPserver = new TcpListener(IPAddress.Any, port);
            TCPserver.Start();
            netSys.Log($"TCP Server started: {TCPserver.LocalEndpoint}");
        }
        //This Starts the UDP Server
        void CreateUDPServer(int port) {
            UDPServer = new UdpClient(port);
            netSys.Log($"UDP Server started: {UDPServer.Client.LocalEndPoint}");
        }
        //This will accept clients while the server is active
        async Task AcceptTCPClientsAsync() {
            //while the server is listening, accept clients
            while (isListening) {
                // Accept a client connection
                TcpClient client = await TCPserver.AcceptTcpClientAsync();
                string ID = netSys.GetUniqueID(client.Client.RemoteEndPoint.ToString());
                ClientConnection c = new ClientConnection { connectionID = ID };
                // Handle the client connection asynchronously
                CancellationTokenSource cant = new CancellationTokenSource();
                Task clientTask = HandleTCPClientAsync(client, ID, cant.Token);
                //add the client to the list of clients
                ConnectionTask ct = new ConnectionTask(clientTask, client.GetStream(), cant);
                c.reciveTasks.Add(ct); c.netSys = netSys; clients.Add(c);
                CheckClientValid(c);
                if (!isListening){ break; } //if the server is no longer listening, break the loop
            }
        }
        //This will listen for UDP data
        async Task AcceptUPDDataAsync() {
            while (isListening) {
                // Accept a client connection
                UdpReceiveResult result = await UDPServer.ReceiveAsync();
                bool ok = HandleUDPMessage(result);
                if(ok){ netSys.Log($"UDP Recived: {result.RemoteEndPoint}"); }
            }
        }
        //This will handle a TCP client
        public delegate void ClientDisconnectEventHandler(string ID, string name);
        public event ClientDisconnectEventHandler onClientDisconnected;
        async Task HandleTCPClientAsync(TcpClient client, string ID, CancellationToken ct) {
            //create the buffers
            byte[] fBuffer = null, buffer, lenBuffer, bBuffer;
            int safeIndex = 0;
            try {
                using (NetworkStream stream = client.GetStream()){
                    // Handle client requests
                    while (client.Connected){

                        if(ct.IsCancellationRequested){ return; }
                        safeIndex = 0;
                        lenBuffer = new byte[4]; //create the length buffer
                        //await stream.FlushAsync();
                        await stream.ReadAsync(lenBuffer, 0, 4); //read the length buffer
                        string lenString = Encoding.ASCII.GetString(lenBuffer, 0, 4); //convert the length buffer to a string
                        int len = 0; int.TryParse(lenString, out len);
                        //netSys.Log($"Recived Length: {lenString}", true);
                        if(lenString == ""){ len = len / 0;}
                        else if(len == 0){ continue; }
                        int totalBytes = len; //get the total bytes from the length buffer
                        totalBytes += netSys.PayloadDataLength; //add the payload data length
                        buffer = new byte[totalBytes]; bBuffer = new byte[totalBytes - 4]; //create the buffer and the data buffer
                        await stream.ReadAsync(bBuffer, 0, bBuffer.Length); //read the data buffer
                        Array.Copy(lenBuffer, 0, buffer, 0, lenBuffer.Length); //make buffer = lenBuffer + bBuffer
                        Array.Copy(bBuffer, 0, buffer, 4, bBuffer.Length); //make buffer = lenBuffer + bBuffer
                        if (totalBytes == 0) { netSys.Log("totalbytes0"); break; } //might be redundant
                        netSys.Log($"Recived Data From Client: {ID}");
                        fBuffer = netSys.Recive(buffer, true, ID, fBuffer); //get just the data from the buffer
                        //if we are expecting a response, confirm the data was recieved
                        await netSys.DataOK(stream);
                    }
                }
            }
            catch (Exception ex) { 
                //are we a valid client?
                if(ValidClient(ID)){
                    if(safeToDrop.Contains(ID)){ netSys.Log("Client Disconnected"); safeToDrop.Remove(ID); }
                    else{ netSys.Log($"Client Lost: at {safeIndex} {ex}", true);  }
                    ClientConnection c = clients.Find(x => x.connectionID == ID);
                    onClientDisconnected.Invoke(ID, c.friendlyName);
                    //remove the client from the list
                    clients.Remove(c);
                }
            }
            client.Close();
        }
        //This will check if a client is valid
        bool ValidClient(string ID) {
            return clients.Find(x => x.connectionID == ID) != null;
        }
        //remove a client if they have not set a friendly name after 7 seconds
        async Task CheckClientValid(ClientConnection c) {
            //wait 7 seconds then check if the client has a friendly name
            await Task.Delay(7000);
            if(c.friendlyName == null && ValidClient(c.connectionID)){
                //the client has not set a friendly name, disconnect them
                clients.Remove(c);
                try{ 
                    foreach(ConnectionTask ct in c.reciveTasks){ ct.stream.Close(); }
                    netSys.Log($"Client {c.connectionID} Rejected");
                }
                catch(Exception ex){ netSys.Log($"Client {c.connectionID} Failed to disconnect: {ex.Message}", true); }
            }
        }
        //This will handle a UDP message
        bool HandleUDPMessage(UdpReceiveResult data) {
            return netSys.UDPRecive(data.Buffer, true, netSys.GetUniqueID(data.RemoteEndPoint.ToString()));
        }
        /// <summary>
        /// This closes the server
        /// </summary>
        public void CloseServer() { isListening = false; }
        /// <summary>
        /// This Sends data to a listenClient by ID
        /// </summary>
        public async Task SendToListenClient(object data, int payloadID, string clientID) {
            ClientConnection c = clients.Find(x => x.connectionID == clientID);
            if(c == null){ netSys.Log($"Client with id {clientID} not found"); return; }
            await c.SendToFirstAvalibleListenClient(data, payloadID);
        }
        /// <summary>
        /// This Sends data to all listenClients
        /// </summary>
        public async Task SendToAllListenClients(object data, int payloadID) {
            foreach(ClientConnection c in clients){
                await c.SendToFirstAvalibleListenClient(data, payloadID);
            }
        }
        //This Sets the friendly name of a client
        public delegate void ClientConnectEventHandler(string ID, string name);
        public event ClientConnectEventHandler onClientConnected;
        public void SetFriendlyName(string name, string connectionID) {
            ClientConnection c = clients.Find(x => x.connectionID == connectionID);
            if(c == null){ netSys.Log($"Client with id {connectionID} not found"); return; }
            c.friendlyName = name;
            netSys.Log($"Client Connected: {connectionID} as: {name}");
            onClientConnected.Invoke(connectionID, name);
        }
        //This Sets a client as a listen client
        public void SetClientAsListen(string ownerName, string IDtoConnect) {
            ClientConnection ownerClient = clients.Find(x => x.friendlyName == ownerName);
            if(ownerClient == null){ netSys.Log($"Client with name {ownerName} not found"); return; }
            ClientConnection listenClient = clients.Find(x => x.connectionID == IDtoConnect);
            if(listenClient == null){ netSys.Log($"Client with id {IDtoConnect} not found"); return; }
            ListenStream ls = new ListenStream(listenClient.reciveTasks[0].stream);
            ownerClient.listenStreams.Add(ls);
            netSys.Log($"Client {ownerClient.friendlyName} is now listening");
            clients.Remove(listenClient);
            //listenClient.reciveTasks[0].cancellationToken.Cancel();
        }
        public void AddAsSubClient(string ownerName, string IDtoConnect) {
            ClientConnection ownerClient = clients.Find(x => x.friendlyName == ownerName);
            if(ownerClient == null){ netSys.Log($"Client with name {ownerName} not found"); return; }
            ClientConnection subClient = clients.Find(x => x.connectionID == IDtoConnect);
            if(subClient == null){ netSys.Log($"Client with id {IDtoConnect} not found"); return; }
            ownerClient.reciveTasks.Add(subClient.reciveTasks[0]);
            clients.Remove(subClient);
            subClient.reciveTasks[0].cancellationToken.Cancel();
        }
        //This marks a client as safe to drop
        public void DropClient(string connectionID) { safeToDrop.Add(connectionID);
            ClientConnection c = clients.Find(x => x.connectionID == connectionID);
            if(c == null){ netSys.Log($"Client with id {connectionID} not found"); return; }
            foreach(ListenStream ls in c.listenStreams){ ls.stream.Dispose(); }
        }
        class ClientConnection{
            public NetSys netSys;
            public string connectionID, friendlyName;
            public List<ConnectionTask> reciveTasks = new List<ConnectionTask>();
            public List<ListenStream> listenStreams = new List<ListenStream>();
            public async Task SendToFirstAvalibleListenClient(object data, int payloadID){
                if(listenStreams.Count == 0){ netSys.Log("No Listen Clients Avalible", true); return; }
                bool sent = false;
                int i = 0;
                while(!sent){
                    if(!listenStreams[i].inUse){
                        sent = true;
                        listenStreams[i].inUse = true;
                        await netSys.Send(data, payloadID, listenStreams[i].stream);
                        listenStreams[i].inUse = false;
                    }
                    else{ i++; }
                    if(i >= listenStreams.Count){ i = 0; }
                }
            }
        }
    }
    public class Client {
        //configure the client
        NetSys netSys;
        List<ClientStream> connections = new List<ClientStream>();
        List<ClientStream> listeners = new List<ClientStream>();
        List<SendObject> sendObjects = new List<SendObject>();
        UdpClient udpClient;
        bool isBusy = false;
        string IPAddress, friendlyName;
        int port;
        public void SetNetSys(NetSys netSys){
            this.netSys = netSys;
        }
        public async Task<bool> Connect(string ipAddress, string friendlyName, int port = 12345)
        {
            // Connect to the receiver
            TcpClient cl = new TcpClient();
            try{
                cl.Connect(ipAddress, port);
            }
            catch(Exception ex){
                netSys.Log($"Failed to connect to server: {ex.Message}", true);
                return false;
            }
            netSys.Log("Connected to the server!");
            // Get the network stream for sending and receiving data
            NetworkStream stream = cl.GetStream();
            connections.Add(new ClientStream(cl, stream));
            await SetFriendlyName(friendlyName, stream);
            this.friendlyName = friendlyName;
            this.port = port;
            IPAddress = ipAddress;
            return true;
        }
        public async Task SetFriendlyName(string name, NetworkStream stream){
            await netSys.Send(name, -1, stream);
        }
        public async Task<bool> CreateListenClients(int count = 1){
            for(int i = 0; i < count; i++){
                TcpClient lc = new TcpClient();
                try{ lc.Connect(IPAddress, port); }
                catch(Exception ex){ netSys.Log($"Failed to connect to server: {ex.Message}", true); return false; }
                netSys.Log($"Listen Client ({listeners.Count + 1}) Connected to the server!");
                // Get the network stream for sending and receiving data
                NetworkStream listenStream = lc.GetStream();
                ClientStream cs = new ClientStream(lc, listenStream);
                listeners.Add(cs);
                await netSys.Send(friendlyName, -2, listenStream);
                HandleListenClientAsync(cs);
            }
            return true;
        }
        public async Task<bool> CreateSubClients(int count = 1){
            for(int i = 0; i < count; i++){
                // Connect to the receiver
                TcpClient cl = new TcpClient();
                try{ cl.Connect(IPAddress, port); }
                catch(Exception ex){ netSys.Log($"Failed to connect to server: {ex.Message}", true); return false; }
                netSys.Log($"({connections.Count + 1}) connections to the server!");
                // Get the network stream for sending and receiving data
                NetworkStream stream = cl.GetStream();
                connections.Add(new ClientStream(cl, stream));
                await netSys.Send(friendlyName, -4, stream);
            }
            return true;
        }
        public int GetConnectionCount(bool listeners = false){ return listeners ? this.listeners.Count : connections.Count; }
        public void ConnectUDP(string ipAddress, int port = 12345){
            udpClient = new UdpClient();
            udpClient.Connect(ipAddress, port);
        }
        public async Task<bool> SendInstant(object data, int payloadId){
            return await netSys.SendUDP(data, payloadId, udpClient);
        }
        async Task HandleListenClientAsync(ClientStream lc)
        {
            //create the buffers
            byte[] fBuffer = null, buffer, lenBuffer, bBuffer;
            try{
                using (lc.stream){
                    // Handle client requests
                    while (lc.client.Connected){
                        lenBuffer = new byte[4]; //create the length buffer
                        await lc.stream.ReadAsync(lenBuffer, 0, 4);
                        int totalBytes = int.Parse(Encoding.ASCII.GetString(lenBuffer, 0, 4));
                        totalBytes += netSys.PayloadDataLength; //add the payload data length
                        buffer = new byte[totalBytes]; bBuffer = new byte[totalBytes - 4];
                        await lc.stream.ReadAsync(bBuffer, 0, bBuffer.Length);

                        //make buffer = lenBuffer + bBuffer
                        Array.Copy(lenBuffer, 0, buffer, 0, lenBuffer.Length);
                        Array.Copy(bBuffer, 0, buffer, 4, bBuffer.Length);

                        if (totalBytes == 0) { break; } //might be redundant
                        netSys.Log("Recived Data From Server");
                        fBuffer = netSys.Recive(buffer, false, null, fBuffer); //get just the data from the buffer
                        //if we are expecting a response, confirm the data was recieved
                        if(fBuffer != null) { await netSys.DataOK(lc.stream); }
                    }
                }
            }
            catch (Exception ex)
            {  netSys.Log("Server Lost: " + ex.Message, true);  }
            // Close the client connection
            lc.client.Close();
        }
        public async Task SendData(object data, int payloadId){
            SendObject so = new SendObject(data, payloadId);
            sendObjects.Add(so);
            SendAllInQueue();
            //while(sendObjects.Contains(so)){ await Task.Delay(4); }
        }
        async Task SendAllInQueue(){
            if(isBusy){ return; }
            isBusy = true;
            int i = 0;
            while(sendObjects.Count > 0){
                if(!connections[i].inUse){
                    connections[i].inUse = true;
                    SendDataOnConnection(sendObjects[0], i);
                    sendObjects.RemoveAt(0);
                }
                else{ i++; }
                if(i >= connections.Count){ i = 0; await Task.Delay(10); }
            }
            isBusy = false;
        }
        async Task SendDataOnConnection(SendObject stuff, int index){
            ClientStream cs = connections[index];
            bool b = await netSys.Send(stuff.data, stuff.payloadId, cs.stream);
            //if(!b){ isBusy = false; netSys.client = null; return false; }
            if(!b){ netSys.Log("Failed to send data", true); connections.RemoveAt(index); return; }
            cs.inUse = false;
        }
        public async Task Disconnect()
        {
            SendData("", -3);
            while(sendObjects.Count > 0){ await Task.Delay(7); }
            await Task.Delay(500);
            // Clean up
            foreach(ClientStream cs in connections){ cs.stream.Close(); cs.client.Close(); }
            netSys.Log("Disconnected from the server!");
        }
        class SendObject{
            public object data;
            public int payloadId;
            public SendObject(object data, int payloadId){
                this.data = data;
                this.payloadId = payloadId;
            }
        }
    }
    public delegate void ProcessDataEventHandler(RecivedData data);
    public event ProcessDataEventHandler onDataRecived;
    public struct RecivedData{
        public object dataObj;
        public int payloadId;
        public bool isServer;
        public string connectionID;
    }
    void ProcessData(byte[] data, int payloadId, bool isServer, string ID)
    {
        // Your data processing logic here
        object dataObj = ByteArrayToObject(data);
        //Log($"ProcessData ID:({payloadId}) {(string)dataObj}");
        if(isServer && payloadId < 0){
            if(payloadId == -1){
                server.SetFriendlyName((string)dataObj, ID); //set the friendly name
            }
            else if(payloadId == -2){
                server.SetClientAsListen((string)dataObj, ID); //mark as listen client
            }
            else if(payloadId == -3){
                server.DropClient(ID); //drop the client
            }
            else if(payloadId == -4){
                server.AddAsSubClient((string)dataObj, ID); //add as sub client
            }
            return;
        }
        // Notify listeners
        RecivedData r = new RecivedData
        {
            dataObj = dataObj,
            payloadId = payloadId,
            isServer = isServer,
            connectionID = ID
        };
        onDataRecived?.Invoke(r);
    }
    async Task<bool> Send(object data, int payloadId, NetworkStream stream, bool canBeSplit = true)
    {
        // Convert the object to bytes
        byte[] dataBytes = ObjectToByteArray(data);
        //Log($"Sending {dataBytes.Length} bytes of data");

        // Split the data into multiple payloads if necessary
        List<byte[]> payloads = SplitDataIntoPayloads(dataBytes);
        if(!canBeSplit && payloads.Count > 1){  return false; }

        // Send each payload
        for (int i = 0; i < payloads.Count; i++)
        {
            byte[] payload = payloads[i];

            // Determine if this is the last payload
            bool isLastPayload = i == payloads.Count - 1;
            // Determine the terminator value
            char terminator = isLastPayload ? '1' : '2';
            // Determine the length of the payload
            int payloadLength = payload.Length + PayloadDataLength; // lengthBytes + payloadId + payloadData + terminator
            string payloadIDString = payloadId.ToString("000");
            if(payloadIDString.Length == 4){ payloadIDString = $"-0{payloadIDString[3]}"; }
            string info = $"{payload.Length:0000}{payloadIDString}{terminator}";
            byte[] infoBytes = Encoding.ASCII.GetBytes(info);
            // Create the complete payload
            byte[] completePayload = new byte[payloadLength];

            Array.Copy(infoBytes, 0, completePayload, 0, infoBytes.Length);
            Log($"completePayload: {completePayload.Length}");
            //Log($"payloadLength: {payloadLength}, info: {info}");
            Array.Copy(payload, 0, completePayload, 8, payload.Length);
            // Send the payload
            bool b = await SendPayloadAsync(completePayload, stream);
            if(!b){ Log("Payload Send Error", true);  return false; }
            Log($"Payload {i + 1}/{payloads.Count} sent.");
            // Wait for response if it's not the last payload
            if (!isLastPayload) {Log("Waiting For Recived"); }
            await WaitForDataOKAsync(stream);
        }
        return true;
    }
    async Task<bool> SendUDP(object data, int payloadId, UdpClient client){
        byte[] dataBytes = ObjectToByteArray(data);
        if(dataBytes.Length > MaxPayloadLength - 3){ return false; }
        string payloadIDString = payloadId.ToString("000");
        byte[] payloadIDBytes = Encoding.ASCII.GetBytes(payloadIDString);
        byte[] completePayload = new byte[dataBytes.Length + payloadIDBytes.Length];
        Array.Copy(payloadIDBytes, 0, completePayload, 0, payloadIDBytes.Length);
        Array.Copy(dataBytes, 0, completePayload, payloadIDBytes.Length, dataBytes.Length);
        await client.SendAsync(completePayload, completePayload.Length);
        return true;
    }
    byte[] Recive(byte[] toProcess, bool isServer, string ID, byte[] previous = null){
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
                ProcessData(payload, payloadId, isServer, ID);
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
    bool UDPRecive(byte[] toProcess, bool isServer, string ID){
        try{
            //if previous is null, then this is the first payload
            // get the first 8 bytes as a string
            string info = Encoding.ASCII.GetString(toProcess, 0, 3);
            //id is bytes 4-6
            int payloadId = int.Parse(info);
            //Log($"PayloadId: {payloadId}, Terminator: {terminator}, from info: {info}");
            //remove the first 8 bytes to get the payload
            byte[] payload = new byte[toProcess.Length - 3];
            Array.Copy(toProcess, 3, payload, 0, toProcess.Length - 3);
            //process the data
            ProcessData(payload, payloadId, isServer, ID);
        }
        catch{
            Log("BadUDPData", true);
            return false;
        }
        return true;
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
        if(b == null){ return null; }
        BinaryFormatter bf = new BinaryFormatter();
        using (MemoryStream ms = new MemoryStream())
        { bf.Serialize(ms, b); return ms.ToArray(); }
    }
    //Byte array to object
    object ByteArrayToObject(byte[] arrBytes){
        using (MemoryStream memStream = new MemoryStream()) {
            BinaryFormatter binForm = new BinaryFormatter();
            memStream.Write(arrBytes, 0, arrBytes.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            return binForm.Deserialize(memStream);
        }
    }
    async Task DataOK(NetworkStream stream)
    {
        byte[] buffer = Encoding.ASCII.GetBytes("ok");
        await stream.WriteAsync(buffer, 0, buffer.Length);
    }
    string GetUniqueID(string ip){ return ip.Replace(".", ""); }
    public void SetLogging(bool allowLogging){ this.allowLogging = allowLogging; }
    void Log(string message, bool error = false) {
        if(allowLogging || error){
            #if UNITY_EDITOR
                UnityEngine.Debug.Log($"{message}");
            #else
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            #endif
        }
    }
    class ConnectionTask{
        public Task task;
        public NetworkStream stream;
        public CancellationTokenSource cancellationToken;
        public ConnectionTask(Task task, NetworkStream stream, CancellationTokenSource cancellationToken){
            this.task = task;
            this.stream = stream;
            this.cancellationToken = cancellationToken;
        }
    }
    class ListenStream{
        public bool inUse = false;
        public NetworkStream stream;
        public ListenStream(NetworkStream stream){
            this.stream = stream;
        }
    }
    class ClientStream{
        public TcpClient client;
        public NetworkStream stream;
        public bool inUse = false;
        public ClientStream(TcpClient client, NetworkStream stream){
            this.client = client;
            this.stream = stream;
        }
    }
}