using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Shapes;
using Windows.UI;

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
                    uint stringLength = 1;
                    while (true)
                    { 
                        uint actualStringLength = await reader.LoadAsync(stringLength);
                            
                        data = reader.ReadString(actualStringLength);
                        if (data.Equals("S"))
                        {
                            uint actualdataLength = await reader.LoadAsync(4);
                            byte[] color_array = new byte[4];
                            reader.ReadBytes(color_array);
                            Color temp_color = new Color();
                            temp_color.A = color_array[0];
                            temp_color.R = color_array[1];
                            temp_color.G = color_array[2];
                            temp_color.B = color_array[3];
                            SolidColorBrush color = new SolidColorBrush(temp_color);
                            double x1,x2,y1,y2,thick;
                            var list = new List<Windows.UI.Xaml.UIElement>();
                            while (true)
                            {
                                actualStringLength = await reader.LoadAsync(1);
                                data = reader.ReadString(actualStringLength);
                                if (data.Equals("P"))
                                {
                                    actualStringLength = await reader.LoadAsync(sizeof(double) * 5);
                                    x1 = reader.ReadDouble();
                                    y1 = reader.ReadDouble();
                                    x2 = reader.ReadDouble();
                                    y2 = reader.ReadDouble();
                                    thick = reader.ReadDouble();

                                    Line l = new Line()
                                    {
                                        X1 = x1,
                                        Y1 = y1,
                                        X2 = x2,
                                        Y2 = y2,
                                        StrokeThickness = thick,
                                        Stroke = color,
                                        StrokeStartLineCap = PenLineCap.Round,
                                        StrokeEndLineCap = PenLineCap.Round,
                                        StrokeLineJoin = PenLineJoin.Round,
                                    };

                                    list.Add(l);
                                }
                                else if (data.Equals("E"))
                                {
                                    OnNetworkRecieved(list);
                                    Debug.WriteLine("Get one stroke");
                                    break;
                                }
                                else
                                    Debug.WriteLine("Expected P or E, {0} ,Network Error!!",data);
                            }
                        }
                        else
                            Debug.WriteLine("Expected S, {0} , Network Error!!",data);
                        
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

        /*public static async void sendData(Point p1, Point p2, Point p3, Point org, float pressure)
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
        }*/

        public static async void sendData(List<Windows.UI.Xaml.UIElement> collection)
        {
            if (writer == null)
                writer = new DataWriter(socket.OutputStream);
            //String data = "S";
            if (collection.Count == 0)
                return;
            Windows.UI.Color temp_color = ((collection[0] as Line).Stroke as SolidColorBrush).Color;
            byte[] temp_array = new byte[4];
            temp_array[0] = temp_color.A;
            temp_array[1] = temp_color.R;
            temp_array[2] = temp_color.G;
            temp_array[3] = temp_color.B;
            writer.WriteString("S");
            writer.WriteBytes(temp_array);
            foreach (var child in collection)
            {
                var line = child as Line;

                if (line == null)
                    continue;
                writer.WriteString("P");
                writer.WriteDouble(line.X1);
                writer.WriteDouble(line.Y1);
                writer.WriteDouble(line.X2);
                writer.WriteDouble(line.Y2);
                writer.WriteDouble(line.StrokeThickness);
            }
            writer.WriteString("E");
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

        public delegate void OnNetworkRecievedDelegate(List<Windows.UI.Xaml.UIElement> lineList);
        public static event OnNetworkRecievedDelegate OnNetworkRecieved;
    }
}
