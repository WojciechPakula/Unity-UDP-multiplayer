using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public enum SendMode
{
    SM_BROADCAST,
    SM_ALL_IN_NETWORK,
    SM_COMPUTER,
    SM_PLAYER,
    SM_TO_SERVER_TO_ALL,
    SM_TO_SERVER
}

public enum NetworkState
{
    NET_DISABLED,
    NET_ENABLED,
    NET_CLIENT,
    NET_SERVER
}

public class PlayerInfo
{
    public int id = 0;
    public string name = "";
    public IPEndPoint ip;
}

public class Computer
{
    public IPEndPoint ip;
    public int state = 0;
    public float offlineTime = 0;
}

public class QueuePack
{
    public IPEndPoint endpoint = null;  //ip nadawcy z portem na który można wysyłać dane
    public QueryPack qp = null;
}

[Serializable]
public class QueryPack
{
    public string type = "";
    public SendMode sendMode = SendMode.SM_ALL_IN_NETWORK;
    public int port = 11001;
    public int targetPlayerId = 0;
    public string json = "";

    public static string getJson(QueryPack q)
    {
        return JsonUtility.ToJson(q);
    }

    public static QueryPack getObject(string json)
    {
        return JsonUtility.FromJson<QueryPack>(json);
    }
}

public class NetworkManager
{
    public static NetworkManager instance = new NetworkManager();   //instancja
    public int broadcastPort = 11000;   //port na którym musi chodzić serwer i na który będą wysyłane wiadomości broadcast.
    public int connectionPort = 11001;  //port na którym chodzi klient, serwer pamięta port przez który może się komunikować z klientem.
    public int port = 11000;
    public bool lockMode = false;   //tryb blokowania wiadomości z poza podłączonyh komputerów (tylko dla serwera)

    private const int joinTimeout = 4000;
    private const int receiverTimeout = 100;
    private const int aliveTimeout = 5000;

    private NetworkState networkState;
    private UdpClient listener = null;
    private Socket s = null;
    private Thread receiver = null;
    private Thread joiner = null;
    private Thread connector = null;

    private Queue<QueuePack> sendQueue = null;
    private Queue<QueuePack> receiveQueue = null;

    public List<PlayerInfo> players = null;
    public List<Computer> computers = null;

    private IPEndPoint serverIp = null;
    private float serverOfflineTime = 0;
    private IPEndPoint myIp = null;

    private bool disableTrigger = false;
    private bool listenerErrorTrigger = false;
    private int listenerCounter = 0;

    private IPEndPoint joinSemaphore = null;

    private static int idCounter = 0;

    // Use this for initialization
    NetworkManager()
    {
        networkState = NetworkState.NET_DISABLED;
        //socket init
        s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.EnableBroadcast = true;
        s.MulticastLoopback = false;
        myIp = new IPEndPoint(getMyIp(), port);
    }

    public void kickComputer(IPEndPoint ip)
    {
        if (networkState == NetworkState.NET_SERVER)
        {
            for (int i = 0; i < computers.Count; ++i)
            {
                bool access = IPEndPoint.Equals(computers[i].ip, ip);
                if (access)
                {
                    computers.RemoveAt(i);
                    for (int j = 0; j < players.Count; ++j)
                    {
                        bool access2 = IPEndPoint.Equals(players[j].ip, ip);
                        if (access2)
                        {
                            players.RemoveAt(j);
                            --j;
                        }
                    }
                    --i;
                    break;
                }
            }
        }
    }

    public void addPlayer(string name, IPEndPoint ip)
    {
        PlayerInfo pi = new PlayerInfo();
        pi.name = name;
        pi.id = idCounter++;
        pi.ip = ip;
        players.Add(pi);
    }

    public void removePlayer(string name, IPEndPoint ip)
    {
        bool highPriority = IPEndPoint.Equals(ip, myIp);
        for (int i = 0; i < players.Count; ++i)
        {
            bool access = IPEndPoint.Equals(players[i].ip, ip);
            if (access || highPriority)
            {
                if (players[i].name == name)
                {
                    players.RemoveAt(i);
                    break;
                }
            }
        }
    }

    //Wysyła obiekt do wszystkich urządzeń w domenie rozgłoszeniowej, nawet do samego siebie
    public void sendBroadcast(object o)
    {
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.port = port;
        qp.sendMode = SendMode.SM_BROADCAST;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        queue.endpoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
        sendQueue.Enqueue(queue);
        //Debug.Log("Broadcast");
    }
    //Pilnuje aby obiekt dotarł do każdego komputera w grze, poza komputerem z którego wysłano obiekt.
    public void sendToAllComputers(object o)
    {
        if (networkState == NetworkState.NET_DISABLED || networkState == NetworkState.NET_ENABLED) return;
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.port = port;
        qp.sendMode = SendMode.SM_ALL_IN_NETWORK;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        //throw new NotImplementedException();
        switch (networkState)
        {
            case NetworkState.NET_SERVER:
                {
                    foreach (Computer comp in computers)
                    {
                        IPEndPoint ip = comp.ip;
                        if (IPEndPoint.Equals(ip, myIp)) continue;
                        QueuePack tmp = new QueuePack();
                        tmp.endpoint = ip;
                        tmp.qp = queue.qp;
                        sendQueue.Enqueue(tmp);
                    }
                    break;
                }
            case NetworkState.NET_CLIENT:
                {
                    queue.endpoint = serverIp;
                    sendQueue.Enqueue(queue);
                    break;
                }
            default:
                sendQueue.Enqueue(queue);
                break;
        }
    }
    //Wysyła obiekt do komputera o podanym ip
    public void sendToComputer(object o, IPEndPoint ip)
    {
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.port = port;
        qp.sendMode = SendMode.SM_COMPUTER;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        queue.endpoint = ip;
        sendQueue.Enqueue(queue);
    }
    //Wysyła obiekt do komputera na którym gra gracz o danym id, nawet jeżeli to komputer z którego wysłano obiekt.
    public void sendToPlayer(object o, int playerId)
    {
        throw new NotImplementedException();
        if (networkState == NetworkState.NET_DISABLED || networkState == NetworkState.NET_ENABLED) return;
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.targetPlayerId = playerId;
        qp.port = port;
        qp.sendMode = SendMode.SM_PLAYER;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        switch (networkState)
        {
            case NetworkState.NET_CLIENT:
                queue.endpoint = serverIp;
                sendQueue.Enqueue(queue);
                break;
            case NetworkState.NET_SERVER:
                foreach (PlayerInfo player in players)
                {
                    if (player.id == playerId)
                    {
                        queue.endpoint = player.ip;
                        sendQueue.Enqueue(queue);
                        break;
                    }
                }
                break;
        }
    }
    //Wysyła obiekt do serwera i serwer wysyła go do wszystkich komputerów łącznie z serwerem. Służy to głównie do traktowania gry jakby była na jakiejś chmurze (czyli model w którym użytkownik nie jest przypisany do stanowiska).
    public void sendToServerToAll(object o)
    {
        if (networkState == NetworkState.NET_DISABLED || networkState == NetworkState.NET_ENABLED) return;
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.port = port;
        qp.sendMode = SendMode.SM_TO_SERVER_TO_ALL;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        queue.endpoint = serverIp;
        sendQueue.Enqueue(queue);
    }
    //Wysyła obiekt do serwera, jeżeli serwer to wysyła to wyśle sam do siebie.
    public void sendToServer(object o)
    {
        if (networkState == NetworkState.NET_DISABLED || networkState == NetworkState.NET_ENABLED) return;
        string json = JsonUtility.ToJson(o);
        QueryPack qp = new QueryPack();
        qp.json = json;
        qp.type = o.GetType().FullName;
        qp.port = port;
        qp.sendMode = SendMode.SM_TO_SERVER;
        QueuePack queue = new QueuePack();
        queue.qp = qp;
        queue.endpoint = serverIp;
        sendQueue.Enqueue(queue);
    }
    public void runSerwer()
    {
        setStateServer();
    }
    public void connectToSerwer(IPEndPoint ip)
    {
        //setStateEnabled();
        if (this.getNetworkState() != NetworkState.NET_DISABLED)
        {
            joinSemaphore = ip;
            joiner = new Thread(() => JoinThread());
            joiner.Start();
            instance.sendToComputer(new Q_JOIN_REQUEST(), ip);
        }
    }
    private void JoinThread()
    {
        //Debug.Log("Odliczanie start !!!");
        Thread.Sleep(joinTimeout);
        joinSemaphore = null;
        joiner = null;
        //Debug.Log("Odliczanie null !!!");
        //////////////////////////////
    }
    public IPEndPoint getJoinIp()
    {
        return joinSemaphore;
    }
    //zmienia stan sieci na kliencki
    public void acceptJoin(IPEndPoint ip)
    {
        setStateClient(ip);
        if (joiner != null) joiner.Abort();
        joinSemaphore = null;
        joiner = null;

        connector = new Thread(() => AliveThread());
        connector.Start();
    }
    private void AliveThread()
    {
        while (getNetworkState() == NetworkState.NET_CLIENT)
        {/////////////////////////////////////
            Thread.Sleep(aliveTimeout);
            sendToServer(new Q_IM_ALIVE());
        }
    }
    public void enableNetwork()
    {
        setStateEnabled();
    }
    public void disableNetwork()
    {
        setStateDisabled();
    }
    public bool isKnownComputer(IPEndPoint ip)
    {
        if (networkState == NetworkState.NET_SERVER)
        {
            bool fail = false;
            foreach (Computer c in computers)
            {
                if (IPEndPoint.Equals(c.ip, ip)) fail = true;
            }
            if (!fail)
            {
                return true;
            }
        }
        return false;
    }
    public bool addComputer(IPEndPoint ip)
    {
        if (networkState == NetworkState.NET_SERVER)
        {
            bool fail = false;
            foreach (Computer c in computers)
            {
                if (IPEndPoint.Equals(c.ip, ip)) fail = true;
            }
            if (!fail)
            {
                computers.Add(new Computer() { ip = ip });
                return true;
            }
        }
        return false;
    }
    ~NetworkManager()
    {
        if (receiver != null)
        {
            receiver.Abort();
            if (listener != null) listener.Close();
        }
    }
    //server nie odbiera wiadomości od obcych komputerów (start gry)
    public void setLockMode()
    {
        lockMode = true;
    }
    public void kill()
    {
        stopReceiver();
    }
    public NetworkState getNetworkState()
    {
        return networkState;
    }
    public void setComputerTimeZero(IPEndPoint ip)
    {
        if (networkState != NetworkState.NET_SERVER) return;
        foreach (Computer comp in computers)
        {
            if (IPEndPoint.Equals(comp.ip, ip)) comp.offlineTime = 0;
        }
    }
    public void setServerTimeZero()
    {
        if (networkState != NetworkState.NET_CLIENT) return;
        serverOfflineTime = 0;
    }
    public void update()
    {
        if (networkState == NetworkState.NET_SERVER)
        {
            foreach (Computer comp in computers)
            {
                comp.offlineTime += Time.deltaTime;
            }
        }
        if (networkState == NetworkState.NET_CLIENT)
        {
            serverOfflineTime += Time.deltaTime;
        }
        if (disableTrigger == true)
        {
            Debug.Log("Nieoczekiwany błąd. Sieć wyłączona.");
            disableTrigger = false;
            setStateDisabled();
        }
        if (listenerErrorTrigger == true)
        {
            listenerErrorTrigger = false;
            Debug.Log("Błąd podczas tworzenia nasłuchiwania na porcie " + port + ". Możliwe, że jest z jakiegoś powodu zajęty. Próbuje naprawić problem.");
            if (networkState == NetworkState.NET_SERVER)
            {
                //setStateDisabled();
                setStateEnabled();
                Debug.Log("Nie można naprawić problemu dla serwera. Port jest blokowany przez inną aplikację.");
            }
            if (networkState == NetworkState.NET_ENABLED || networkState == NetworkState.NET_CLIENT)
            {
                connectionPort++;
                setStateEnabled();
                Debug.Log("Port został zmieniony na " + port);
            }
        }
        sendAllQueriesInQueue();
        executeAllQueriesInQueue();
    }
    private void sendAllQueriesInQueue()
    {
        if (networkState != NetworkState.NET_DISABLED && sendQueue != null)
        {
            for (; sendQueue.Count > 0;)
            {
                QueuePack queue = sendQueue.Dequeue();
                string json = QueryPack.getJson(queue.qp);
                sendObject(json, queue.endpoint);
            }
        }
    }
    private void executeAllQueriesInQueue()
    {
        if (networkState != NetworkState.NET_DISABLED && receiveQueue != null)
        {
            for (; receiveQueue.Count > 0;)
            {
                QueuePack queue = receiveQueue.Dequeue();
                Q_OBJECT query = Q_OBJECT.Deserialize(queue.qp.json, queue.qp.type);
                query.executeQuery(queue);
            }
        }
    }
    public IPAddress getMyIp()
    {
        IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());
        IPAddress ipAddress = null;
        foreach (IPAddress a in localIPs)
        {
            if (a.AddressFamily == AddressFamily.InterNetwork)
                ipAddress = a;
        }
        return ipAddress;
    }
    private void sendObject(string json, IPEndPoint ip)
    {
        byte[] sendbuf = Encoding.UTF8.GetBytes(json);
        s.SendTo(sendbuf, ip);
        //Debug.Log("Message sent: " + json);
    }
    private void setStateServer()
    {
        setStateDisabled();
        port = broadcastPort;
        myIp = new IPEndPoint(getMyIp(), port);
        runReceiver();
        if (networkState == NetworkState.NET_SERVER) return;
        networkState = NetworkState.NET_SERVER;
        players = new List<PlayerInfo>();
        computers = new List<Computer>();
        addComputer(myIp);
        serverIp = new IPEndPoint(getMyIp(), broadcastPort);
    }
    private void setStateClient(IPEndPoint serverIp)
    {
        setStateDisabled();
        runReceiver();
        if (networkState == NetworkState.NET_CLIENT) return;
        networkState = NetworkState.NET_CLIENT;
        this.serverIp = serverIp;
    }
    private void setStateEnabled()
    {
        setStateDisabled();
        runReceiver();
        if (networkState == NetworkState.NET_ENABLED) return;
        networkState = NetworkState.NET_ENABLED;
    }
    private void setStateDisabled()
    {
        serverOfflineTime = 0;
        lockMode = false;
        port = connectionPort;
        myIp = new IPEndPoint(getMyIp(), port);
        if (networkState == NetworkState.NET_DISABLED) return;
        sendQueue = null;
        receiveQueue = null;
        players = null;
        computers = null;
        serverIp = null;
        stopReceiver();
        networkState = NetworkState.NET_DISABLED;
    }

    private void runReceiver()
    {
        if (receiver == null)
        {
            sendQueue = new Queue<QueuePack>();
            receiveQueue = new Queue<QueuePack>();
            receiver = new Thread(() => ReceiverThread(Thread.CurrentThread, listenerCounter++));
            receiver.Start();
        }
    }

    private void stopReceiver()
    {
        if (receiver != null)
        {
            receiver.Abort();
            if (listener != null) listener.Close();
            receiver = null;
        }
    }

    private void ReceiverThread(Thread main, int id = 0)
    {
        //Debug.Log("id:"+id+" NetworkManager - ReceiverThread Start");
        try
        {
            bool done = false;

            IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, port);
            listener = new UdpClient(port);
            listener.Client.ReceiveTimeout = receiverTimeout;
            while (!done)
            {
                //Thread.Sleep(1000);
                if (!main.IsAlive) throw new Exception("NetworkManager - Aplikacja zamknieta");
                try
                {
                    //Debug.Log("Waiting for broadcast");
                    byte[] bytes = listener.Receive(ref groupEP);

                    //Debug.Log("Odebrano");
                    //Debug.Log(Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                    string json = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    QueryPack queryPack = JsonUtility.FromJson<QueryPack>(json);
                    QueuePack queuePack = new QueuePack();
                    queuePack.endpoint = groupEP;
                    queuePack.endpoint.Port = queryPack.port;
                    queuePack.qp = queryPack;
                    processQueueMessage(queuePack);
                    //Console.WriteLine("Received broadcast from {0} :\n {1}\n",groupEP.ToString(),Encoding.ASCII.GetString(bytes, 0, bytes.Length));                
                }
                catch (Exception e)
                {
                    //Debug.Log("Blad in");
                }
            }
            listener.Close();
        }
        catch (ThreadAbortException e)
        {
            //Debug.Log("id:" + id + " Abort");        
        }
        catch (SocketException e)
        {
            Debug.Log("id:" + id + " Port error");
            listenerErrorTrigger = true;
        }
        catch (Exception e)
        {
            Debug.Log("id:" + id + " Blad");
            disableTrigger = true;
        }
        finally
        {
            //Debug.Log("id:" + id + " NetworkManager - ReceiverThread Stop");
        }
    }

    private void processQueueMessage(QueuePack queuePack)
    {
        bool wtf = !IPEndPoint.Equals(queuePack.endpoint, serverIp);
        if (networkState == NetworkState.NET_CLIENT && wtf) return;
        if (networkState == NetworkState.NET_SERVER && lockMode && !isKnownComputer(queuePack.endpoint)) return;
        switch (queuePack.qp.sendMode)
        {
            case SendMode.SM_BROADCAST:
                if (!IPEndPoint.Equals(queuePack.endpoint, myIp))
                    receiveQueue.Enqueue(queuePack);
                break;
            case SendMode.SM_ALL_IN_NETWORK:
                receiveQueue.Enqueue(queuePack);
                if (this.networkState == NetworkState.NET_SERVER)
                {
                    IPEndPoint source = queuePack.endpoint;
                    foreach (Computer computer in computers)
                    {
                        if (IPEndPoint.Equals(source, computer.ip) || IPEndPoint.Equals(myIp, computer.ip)) continue;
                        QueuePack tmp2 = new QueuePack();
                        tmp2.endpoint = computer.ip;
                        tmp2.qp = queuePack.qp;
                        tmp2.qp.port = serverIp.Port;
                        sendQueue.Enqueue(tmp2);
                    }
                }
                break;
            case SendMode.SM_PLAYER:
                if (this.networkState == NetworkState.NET_SERVER)
                {
                    foreach (PlayerInfo player in players)
                    {
                        if (player.id == queuePack.qp.targetPlayerId)
                        {
                            queuePack.endpoint = player.ip;
                            sendQueue.Enqueue(queuePack);
                            break;
                        }
                    }
                }
                else
                {
                    receiveQueue.Enqueue(queuePack);
                }
                break;
            case SendMode.SM_TO_SERVER_TO_ALL:
                foreach (Computer comp in computers)
                {
                    IPEndPoint ip = comp.ip;
                    QueryPack tmp = queuePack.qp;
                    tmp.sendMode = SendMode.SM_COMPUTER;
                    QueuePack queue = new QueuePack();
                    queue.qp = tmp;
                    queue.endpoint = ip;
                    sendQueue.Enqueue(queue);
                }
                break;
            case SendMode.SM_TO_SERVER:
                receiveQueue.Enqueue(queuePack);
                break;
            default:
                receiveQueue.Enqueue(queuePack);
                break;
        }
    }
}