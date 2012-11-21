using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace PenTouch
{
    static class Network
    {
        private static StreamSocket socket;
        private static DataWriter writer;

        static Network()
        {
            socket = new StreamSocket();
            writer = null;
        }

        public static string process(string data)
        {
            string[] DataList = data.Split('P');
            if (DataList.Count() < 1)
                return data;
            for (int i = 1; i < DataList.Count(); i++)
            {
                //Debug.WriteLine(DataList[i]);
                if (DataList[i].Length == 0 || DataList[i][DataList[i].Length - 1] != '!')
                    return "P" + DataList[i];
                Debug.WriteLine(DataList[i]);
                string[] temp = DataList[i].Split(',');
                //for (int j = 0; j < temp.Count(); j++)
                //Debug.WriteLine(temp[i]);
                Point p1, p2, p3, org;
                float pressure;
                p1.X = Double.Parse(temp[0]);
                p1.Y = Double.Parse(temp[1]);
                p2.X = Double.Parse(temp[2]);
                p2.Y = Double.Parse(temp[3]);
                p3.X = Double.Parse(temp[4]);
                p3.Y = Double.Parse(temp[5]);
                org.X = Double.Parse(temp[6]);
                org.Y = Double.Parse(temp[7]);
                pressure = (float)Double.Parse(temp[8].Split('!')[0]);

                OnNetworkRecieved(p1, p2, p3, org, pressure);
            }
            return "";
        }

        public static async void connect()
        {
			HostName hostname = new HostName("myaustin.iptime.org");
            String servicename = "21122";
            Debug.WriteLine("Connecting");

            try
            {
                // Connect to the server (in our case the listener we created in previous step).
                await socket.ConnectAsync(hostname, servicename);

                Debug.WriteLine("Connected");

                Debug.WriteLine("onconnection start");
                DataReader reader = new DataReader(socket.InputStream);
                try
                {
                    String data = "";
                    while (true)
                    {/*
                        uint sizeFieldCount = await reader.LoadAsync(sizeof(uint));
                        if (sizeFieldCount != sizeof(uint))
                        {
                            // The underlying socket was closed before we were able to read the whole data.
                            Debug.WriteLine("onconnection end");
                            return;
                        }
                        // Read the string.*/
                        //uint stringLength = reader.ReadUInt32();
                        uint stringLength = 1;

                        if (stringLength != 0)
                        {
                            //Debug.WriteLine(stringLength);

                            uint actualStringLength = await reader.LoadAsync(stringLength);
                            /*if (stringLength != actualStringLength)
                            {
                                // The underlying socket was closed before we were able to read the whole data.
                                Debug.WriteLine("onconnection end");
                                return;
                            }*/
                            //Debug.WriteLine(actualStringLength);
                            data += reader.ReadString(actualStringLength);

                            // Display the string on the screen. The event is invoked on a non-UI thread, so we need to marshal the text back to the UI thread.
                            //Debug.WriteLine(String.Format("Receive data: \"{0}\"", data));
                            data = process(data);
                        }
                    }
                }
                catch (Exception exception)
                {
                    // If this is an unknown status it means that the error is fatal and retry will likely fail.
                    if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                    {
                        throw;
                    }
                    Debug.WriteLine("Read stream failed with error: " + exception.Message);
                }

                // Mark the socket as connected. Set the value to null, as we care only about the fact that the property is set.
                //CoreApplication.Properties.Add("connected", null);
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                Debug.WriteLine("Connect failed with error: " + exception.Message);
            }
        }

        public static async void sendData(Point p1, Point p2, Point p3, Point org, float pressure)
        {
            if (writer == null)
                writer = new DataWriter(socket.OutputStream);
            String data = "";
            data += "P" + p1.X.ToString() + "," + p1.Y.ToString();
            data += "," + p2.X.ToString() + "," + p2.Y.ToString();
            data += "," + p3.X.ToString() + "," + p3.Y.ToString();
            data += "," + org.X.ToString() + "," + org.Y.ToString();
            data += "," + pressure + "!";
            //writer.WriteInt32(data.Length);
            writer.WriteString(data);
            try
            {
                await writer.StoreAsync();
                Debug.WriteLine("\"" + "DATA" + "\" sent successfully.");
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error if fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                Debug.WriteLine("Send failed with error: " + exception.Message);
            }
        }

        public delegate void OnNetworkRecievedDelegate(Point p1, Point p2, Point p3, Point p4, float pressure);
        public static event OnNetworkRecievedDelegate OnNetworkRecieved;
    }
}
