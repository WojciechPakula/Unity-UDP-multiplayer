using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

public class Test : MonoBehaviour {
    public static Test instance;

    public GameObject cubeClient;
    public GameObject cubeServer;

    public IPEndPoint ip = null;
    // Use this for initialization
    void Start () {
        instance = this;
        Application.runInBackground = true;
        Debug.Log("R - run server");
        Debug.Log("E - enable network");
        Debug.Log("B - find server");
        Debug.Log("C - connect to server");
        Debug.Log("H - send hello");
        Debug.Log("scenariusz");
        Debug.Log("Uruchom okienko i wcisnij R");
        Debug.Log("W drugim okienku wciśnij - E, B, C, H");
        Debug.Log("Komunikat hello powinien się pojawić w oknie serwera");
    }

    // Update is called once per frame
    void Update () {
        NetworkManager.instance.update();

        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("R");
            NetworkManager.instance.runSerwer();
            
        }
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (ip != null)
            {
                Debug.Log("C");
                NetworkManager.instance.connectToSerwer(ip);
            }          
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            Debug.Log("B");
            NetworkManager.instance.sendBroadcast(new Q_SERVER_INFO_REQUEST());
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("H");
            NetworkManager.instance.sendToAllComputers(new Q_HELLO { text = "komunikat"});
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("E");
            NetworkManager.instance.enableNetwork();
        }
        cubeUpdate();
        
    }

    private void cubeUpdate()
    {
        if (NetworkManager.instance.getNetworkState() == NetworkState.NET_SERVER)
        {
            if (Input.GetKey(KeyCode.UpArrow))
            {
                cubeServer.transform.Translate(Vector3.forward * Time.deltaTime * 4);
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                cubeServer.transform.Rotate((new Vector3(0, -90, 0)) * Time.deltaTime);
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                cubeServer.transform.Rotate((new Vector3(0, 90, 0)) * Time.deltaTime);
            }
            NetworkManager.instance.sendToAllComputers(new Q_CUBE_POSITION { position = cubeServer.transform.position, rotation = cubeServer.transform.rotation, type = 0 });
        }
        if (NetworkManager.instance.getNetworkState() == NetworkState.NET_CLIENT)
        {
            if (Input.GetKey(KeyCode.UpArrow))
            {
                cubeClient.transform.Translate(Vector3.forward * Time.deltaTime * 4);
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                cubeClient.transform.Rotate((new Vector3(0, -90, 0)) * Time.deltaTime);
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                cubeClient.transform.Rotate((new Vector3(0, 90, 0)) * Time.deltaTime);
            }
            NetworkManager.instance.sendToAllComputers(new Q_CUBE_POSITION { position = cubeClient.transform.position, rotation = cubeClient.transform.rotation, type = 1 });
        }
    }

    private void OnDestroy()
    {
        NetworkManager.instance.kill();
    }


}
